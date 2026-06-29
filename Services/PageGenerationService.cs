using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using thta_ai.Models;
using System.Runtime.CompilerServices;

public class PageGenerationService : IPageGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly AiGenerationOptions _options;

    private readonly ILogger<PageGenerationService> _logger;

    public PageGenerationService(HttpClient httpClient, IOptions<AiGenerationOptions> options, ILogger<PageGenerationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

    }

    public async Task<GeneratePageResponse> GeneratePageAsync(
    string prompt,
    Guid conversationId,
    bool isNewConversation,
    JsonElement schema,
    CancellationToken cancellationToken = default)
    {
        var planningSchema = MapRawSchemaToPlanning(schema);
        Log("PLANNING SCHEMA (pre-serialise)", JsonSerializer.Serialize(planningSchema, new JsonSerializerOptions { WriteIndented = true }));

        // STEP 1: PLAN
        var plan = await PlanPageWithRetryAsync(
            prompt,
            conversationId,
            planningSchema,
            cancellationToken);

        // STEP 2: EXPAND PLAN -> EMPTY SHELL
        var pageShell = ExpandPlanFromRaw(plan, schema);

        var pageValidation = ValidatePage(pageShell, planningSchema);
        if (!pageValidation.IsValid)
        {
            Log("PAGE VALIDATION FAILED", string.Join("\n", pageValidation.Errors));
            throw new InvalidOperationException("Invalid page shell");
        }

        // STEP 3: FILL CONTENT
        // Want to add retry logic to validate the page too.
        // May want to use a new conversation id for this request if we use the retry as may get confused.
        var finalPage = await GenerateContentAsync(prompt, conversationId, pageShell, cancellationToken);

        return new GeneratePageResponse
        {
            ConversationId = conversationId,
            RawOutput = JsonSerializer.SerializeToElement(finalPage)
        };
    }

    // ─── LLM Calls ────────────────────────────────────────────────────────────

    private async Task<LlmPagePlan> PlanPageAsync(
    string prompt,
    Guid conversationId,
    LlmPlanningSchema schema,
    CancellationToken ct)
    {
        var systemPrompt = BuildPlanningPrompt(schema);
        Log("PLAN SYSTEM PROMPT", systemPrompt);

        var messages = new List<ChatMessage>
    {
        new() { Role = "system", Content = systemPrompt },
        new() { Role = "user", Content = prompt }
    };

        Log("PLAN USER PROMPT", prompt);

        var content = await SendChatAsync(conversationId, messages, ct);
        Log("PLAN LLM RESPONSE", content);

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("LLM returned an empty response during planning.");

        var json = ExtractJson(content);
        Log("PLAN EXTRACTED JSON", json.GetRawText());

        return JsonSerializer.Deserialize<LlmPagePlan>(json.GetRawText())!;
    }

    private async Task<LlmPageResponse> GenerateContentAsync(
    string prompt,
    Guid conversationId,
    LlmPageResponse page,
    CancellationToken ct)
    {
        var systemPrompt = BuildContentPrompt(page);
        Log("CONTENT SYSTEM PROMPT", systemPrompt);

        var messages = new List<ChatMessage>
    {
        new() { Role = "system", Content = systemPrompt },
        new() { Role = "user", Content = prompt }
    };

        var content = await SendChatAsync(conversationId, messages, ct);
        Log("CONTENT LLM RESPONSE", content);

        var json = ExtractJson(content);
        Log("CONTENT EXTRACTED JSON", json.GetRawText());

        return JsonSerializer.Deserialize<LlmPageResponse>(json.GetRawText())!;
    }

    // ─── LLM RETRY LOGIC ─────────────────────────────────────────────────────

    private async Task<LlmPagePlan> PlanPageWithRetryAsync(
    string prompt,
    Guid conversationId,
    LlmPlanningSchema schema,
    CancellationToken ct)
    {
        List<string> lastErrors = [];

        for (int attempt = 0; attempt <= _options.MaxPlanRetries; attempt++)
        {
            var currentPrompt = attempt == 0
                ? prompt
                : BuildPlanFixPrompt(prompt, lastErrors, schema);

            var plan = await PlanPageAsync(currentPrompt, conversationId, schema, ct);
            var validation = ValidatePlan(plan, schema);

            if (validation.IsValid)
                return plan;

            lastErrors = validation.Errors;
            Log("PLAN VALIDATION FAILED", string.Join("\n", lastErrors));
        }

        throw new InvalidOperationException(
            "LLM failed to produce a valid plan after retries:\n" +
            string.Join("\n", lastErrors));
    }

    // ─── HTTP ─────────────────────────────────────────────────────────────────

    private async Task<string> SendChatAsync(
        Guid conversationId,
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest
        {
            ConversationId = conversationId,
            Model = _options.Model,
            Stream = false,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(request);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var response = await _httpClient.SendAsync(httpRequest, linkedCts.Token);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        var doc = JsonDocument.Parse(body);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()!;
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────
    private LlmPlanningSchema MapRawSchemaToPlanning(JsonElement rawSchema)
    {
        var result = new LlmPlanningSchema();

        foreach (var page in rawSchema.EnumerateArray())
        {
            var docType = page.GetProperty("docType");
            var alias = docType.GetProperty("alias").GetString() ?? "";

            // Skip page types that have no BlockGrid properties at all — they
            // have nothing for the LLM to plan (e.g. siteMap).
            var blockGridProps = page.GetProperty("properties")
                .EnumerateArray()
                .Where(p => p.GetProperty("type").GetString() is "Umbraco.BlockGrid")
                .ToList();

            if (blockGridProps.Count == 0)
                continue;

            var pageType = new LlmPlanningPageType { Name = alias };

            foreach (var prop in blockGridProps)
            {
                var regionAlias = prop.GetProperty("alias").GetString() ?? "";

                var region = new LlmRegion
                {
                    Name = regionAlias,
                    Areas = [],
                    AllowedBlocks = []
                };

                if (!prop.TryGetProperty("blocks", out var blocks))
                {
                    pageType.Regions.Add(region);
                    continue;
                }

                foreach (var block in blocks.EnumerateArray())
                {
                    var blockName = block.GetProperty("name").GetString() ?? "";
                    var hasAreas = block.TryGetProperty("areas", out var areas) && areas.GetArrayLength() > 0;

                    if (!hasAreas)
                    {
                        region.AllowedBlocks.Add(blockName);
                        continue;
                    }

                    // Area container block — add the container itself to AllowedBlocks
                    // and expose its areas so the LLM knows what can go inside.
                    region.AllowedBlocks.Add(blockName);

                    foreach (var area in areas.EnumerateArray())
                    {
                        var areaAlias = area.GetProperty("alias").GetString() ?? "";
                        var columnSpan = area.TryGetProperty("columnSpan", out var cs) ? cs.GetInt32() : 1;

                        if (region.Areas.Any(a => a.Alias == areaAlias))
                            continue;

                        var allowedInArea = new List<string>();
                        if (area.TryGetProperty("allowedBlocks", out var allowedBlocks))
                        {
                            foreach (var allowed in allowedBlocks.EnumerateArray())
                                allowedInArea.Add(allowed.GetProperty("name").GetString() ?? "");
                        }

                        region.Areas.Add(new LlmPlanningArea
                        {
                            Alias = areaAlias,
                            ColumnSpan = columnSpan,
                            AllowedBlocks = allowedInArea
                        });
                    }
                }

                pageType.Regions.Add(region);
            }

            result.PageTypes.Add(pageType);
        }

        return result;
    }

    private LlmPageResponse ExpandPlanFromRaw(LlmPagePlan plan, JsonElement rawSchema)
    {
        // Find the matching page in the raw schema by docType alias
        JsonElement? matchedPage = null;
        foreach (var page in rawSchema.EnumerateArray())
        {
            var alias = page.GetProperty("docType").GetProperty("alias").GetString();
            if (alias == plan.PageType)
            {
                matchedPage = page;
                break;
            }
        }

        if (matchedPage is null)
            throw new InvalidOperationException($"Unknown page type: {plan.PageType}");

        var page2 = new LlmPageResponse
        {
            PageType = plan.PageType,
            Fields = new Dictionary<string, object?>()
        };

        // Add simple (non-block) fields
        foreach (var prop in matchedPage.Value.GetProperty("properties").EnumerateArray())
        {
            var type = prop.GetProperty("type").GetString();
            if (type is "Umbraco.BlockGrid" or "Umbraco.BlockList") continue;
            var alias = prop.GetProperty("alias").GetString() ?? "";
            page2.Fields[alias] = "";
        }

        foreach (var region in plan.Regions)
        {
            foreach (var plannedBlock in region.Blocks)
            {
                var blockDef = FindBlockInRawSchema(plannedBlock.Id, matchedPage.Value);
                if (blockDef is null) continue;

                var built = BuildBlockShellFromRaw(plannedBlock, blockDef.Value, rawSchema);

                built.Region = region.Name; // (you may need to add this property)

                page2.Blocks.Add(built);
            }
        }

        return page2;
    }

    private JsonElement? FindBlockInRawSchema(string nameOrId, JsonElement page)
    {
        foreach (var prop in page.GetProperty("properties").EnumerateArray())
        {
            var type = prop.GetProperty("type").GetString();
            if (type is not ("Umbraco.BlockGrid" or "Umbraco.BlockList")) continue;
            if (!prop.TryGetProperty("blocks", out var blocks)) continue;

            foreach (var block in blocks.EnumerateArray())
            {
                var name = block.GetProperty("name").GetString();
                var id = block.GetProperty("id").GetString();
                if (name == nameOrId || id == nameOrId)
                    return block;
            }
        }
        return null;
    }

    private LlmBlock BuildBlockShellFromRaw(LlmPlannedBlock planned, JsonElement blockDef, JsonElement rawSchema)
    {
        var block = new LlmBlock
        {
            Id = blockDef.GetProperty("id").GetString() ?? "",
            Name = blockDef.GetProperty("name").GetString() ?? "",
            Alias = blockDef.GetProperty("name").GetString() ?? "",
            Fields = new Dictionary<string, object?>()
        };

        if (blockDef.TryGetProperty("properties", out var props))
        {
            foreach (var prop in props.EnumerateArray())
            {
                var propType = prop.GetProperty("type").GetString();
                var alias = prop.GetProperty("alias").GetString() ?? "";

                if (propType is "Umbraco.BlockGrid" or "Umbraco.BlockList")
                {
                    // Nested block list — add as empty list for now
                    block.NestedBlocks[alias] = new List<LlmBlock>();
                }
                else
                {
                    block.Fields[alias] = "";
                }
            }
        }

        // Only add areas the LLM planned
        if (planned.Areas != null && blockDef.TryGetProperty("areas", out var areas))
        {
            foreach (var (areaAlias, plannedChildren) in planned.Areas)
            {
                foreach (var area in areas.EnumerateArray())
                {
                    if (area.GetProperty("alias").GetString() != areaAlias) continue;

                    var childBlocks = new List<LlmBlock>();
                    foreach (var child in plannedChildren)
                    {
                        // Search all pages for this block definition
                        foreach (var page in rawSchema.EnumerateArray())
                        {
                            var found = FindBlockInRawSchema(child.Id, page);
                            if (found.HasValue)
                            {
                                childBlocks.Add(BuildBlockShellFromRaw(child, found.Value, rawSchema));
                                break;
                            }
                        }
                    }
                    block.Areas[areaAlias] = childBlocks;
                    break;
                }
            }
        }

        return block;
    }

    // ─── Prompt Building ──────────────────────────────────────────────────────

    private string BuildPlanFixPrompt(string originalPrompt, List<string> errors, LlmPlanningSchema schema)
    {
        var sb = new StringBuilder();
        foreach (var pt in schema.PageTypes)
        {
            sb.AppendLine($"PAGE TYPE: {pt.Name}");
            foreach (var r in pt.Regions)
            {
                sb.AppendLine($"  REGION: {r.Name}");
                sb.AppendLine($"    AllowedBlocks: {string.Join(", ", r.AllowedBlocks)}");
            }
        }

        return $"""
        Your previous response was invalid. Fix it and return only raw JSON.

        ORIGINAL REQUEST: {originalPrompt}

        ERRORS TO FIX:
        {string.Join("\n", errors)}

        AVAILABLE SCHEMA:
        {sb.ToString().TrimEnd()}

        RULES:
        - Use only the pageTypes and block names listed above
        - Match the regions array structure exactly as shown in the original prompt
        - Return valid JSON only, no markdown
        """;
    }

    // Need to update this prompt as it will be out of date.
    private string BuildPlanningPrompt(LlmPlanningSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var pageType in schema.PageTypes)
        {
            sb.AppendLine($"PAGE TYPE: {pageType.Name}");

            foreach (var region in pageType.Regions)
            {
                sb.AppendLine($"  REGION: {region.Name}");

                var directBlocks = region.AllowedBlocks
                    .Where(b => !region.Areas.Any(a => a.Alias == b))
                    .ToList();

                if (directBlocks.Count > 0)
                    sb.AppendLine($"    Direct blocks: {string.Join(", ", directBlocks)}");

                // List area-container blocks with their area aliases
                var areaContainerNames = new[] { "One Column Area", "Two Column Area", "Three Column Area" };
                var containers = region.AllowedBlocks.Where(b => areaContainerNames.Contains(b)).ToList();

                foreach (var container in containers)
                {
                    sb.AppendLine($"    Area container: {container}");
                    foreach (var area in region.Areas)
                        sb.AppendLine($"      - area alias \"{area.Alias}\" (span {area.ColumnSpan}) — any direct block may go here");
                }
            }
            sb.AppendLine();
        }

        return $$"""
        You are a CMS page layout planning engine. Select a page type and the blocks to use. Do NOT fill in any content.

        ## AVAILABLE PAGE TYPES AND BLOCKS
        {{sb.ToString().TrimEnd()}}

        ## RULES
        - Choose exactly ONE pageType (use the exact name shown above)
        - Choose blocks only from the "Direct blocks" list for each region
        - Area containers (One Column Area, Two Column Area, Three Column Area) hold other blocks inside named areas
        - Do NOT invent block or region names
        - Do NOT fill in any content or field values
        - Return ONLY raw JSON, no markdown, no explanation

        ## OUTPUT FORMAT — follow exactly
        {
          "pageType": "contentPage",
          "regions": [
            {
              "name": "headerContent",
              "blocks": [
                { "id": "Medium Header", "areas": {} }
              ]
            },
            {
              "name": "mainContent",
              "blocks": [
                { "id": "Text Block", "areas": {} },
                {
                  "id": "Two Column Area",
                  "areas": {
                    "left-column": [{ "id": "Text Block" }],
                    "right-column": [{ "id": "Media Block" }]
                  }
                }
              ]
            }
          ]
        }
        """;
    }

    private string BuildContentPrompt(LlmPageResponse page)
    {
        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions
        {
            WriteIndented = true,  // ← was false
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return $"""
                You are a CMS content writer. Fill every empty string ("") in this JSON with realistic content.

                RULES:
                - Return the COMPLETE JSON structure unchanged — same keys, same nesting, same arrays
                - Replace ONLY empty string values ("")
                - Do NOT add, remove, or rename any keys
                - Do NOT wrap in markdown
                - Return valid JSON only

                INPUT:
                {json}
                """;
    }

    // ─── JSON Helpers ─────────────────────────────────────────────────────────

    private static JsonElement ExtractJson(string content)
    {
        var start = content.IndexOfAny(['{', '[']);

        if (start == -1)
            throw new InvalidOperationException($"No JSON found: {content}");

        var slice = content[start..];

        slice = StripFences(slice);

        var json = ExtractFirstValidJson(slice);

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string StripFences(string input)
    {
        return input
            .Replace("```json", "")
            .Replace("```", "")
            .Trim();
    }

    private static string ExtractFirstValidJson(string input)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escape = false;

        int start = -1;

        for (int i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (start == -1)
            {
                if (ch == '{' || ch == '[')
                {
                    start = i;
                    stack.Push(ch == '{' ? '}' : ']');
                    if (ch == '[') throw new NotSupportedException("Root arrays not supported here");
                }
                continue;
            }

            if (escape)
            {
                escape = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (ch == '{')
                stack.Push('}');
            else if (ch == '[')
                stack.Push(']');
            else if (ch == '}' || ch == ']')
            {
                if (stack.Count == 0)
                    break;

                stack.Pop();

                if (stack.Count == 0)
                {
                    return input.Substring(start, i - start + 1);
                }
            }
        }

        throw new InvalidOperationException("Could not extract valid JSON block");
    }

    // ─── Debug Logging ─────────────────────────────────────────────────────────

    private readonly List<string> _debugLog = [];

    private void Log(string label, string content)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] ╔══ {label} ══╗\n{content}\n╚══ END {label} ══╝\n\n";
        File.AppendAllText("C:\\temp\\thta_ai_debug.log", line);
    }


    // ─── Validation ─────────────────────────────────────────────────────────────

    private ValidationResult ValidatePlan(LlmPagePlan plan, LlmPlanningSchema schema)
    {
        var errors = new List<string>();

        var pageType = schema.PageTypes.FirstOrDefault(x => x.Name == plan.PageType);
        if (pageType is null)
        {
            var valid = string.Join(", ", schema.PageTypes.Select(p => p.Name));
            return new ValidationResult
            {
                IsValid = false,
                Errors = [$"Invalid pageType '{plan.PageType}'. Valid types: {valid}"]
            };
        }

        if (plan.Regions is null || plan.Regions.Count == 0)
        {
            errors.Add("Plan contains no regions.");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        foreach (var regionPlan in plan.Regions)
        {
            var region = pageType.Regions.FirstOrDefault(r => r.Name == regionPlan.Name);
            if (region is null)
            {
                var validRegions = string.Join(", ", pageType.Regions.Select(r => r.Name));
                errors.Add($"Unknown region '{regionPlan.Name}'. Valid regions for '{plan.PageType}': {validRegions}");
                continue;
            }

            foreach (var block in regionPlan.Blocks ?? [])
            {
                if (!region.AllowedBlocks.Contains(block.Id))
                {
                    errors.Add(
                        $"Block '{block.Id}' is not allowed in region '{region.Name}'. " +
                        $"Allowed: {string.Join(", ", region.AllowedBlocks)}");
                }
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    private ValidationResult ValidatePage(LlmPageResponse page, LlmPlanningSchema schema)
    {
        var errors = new List<string>();

        var pageType = schema.PageTypes.FirstOrDefault(x => x.Name == page.PageType);
        if (pageType is null)
            return new ValidationResult { IsValid = false, Errors = ["Invalid pageType"] };

        foreach (var block in page.Blocks)
        {
            var region = pageType.Regions.FirstOrDefault(r => r.Name == block.Region);

            if (region is null)
            {
                errors.Add($"Invalid region on block {block.Id}: {block.Region}");
                continue;
            }

            if (!region.AllowedBlocks.Contains(block.Id))
            {
                errors.Add($"Block {block.Id} not allowed in region {block.Region}");
            }

            ValidateNested(block, errors);
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private void ValidateNested(LlmBlock block, List<string> errors)
    {
        foreach (var kv in block.NestedBlocks)
        {
            foreach (var child in kv.Value)
            {
                if (string.IsNullOrWhiteSpace(child.Id))
                    errors.Add($"Nested block missing Id in {block.Id}");
            }
        }
    }
}