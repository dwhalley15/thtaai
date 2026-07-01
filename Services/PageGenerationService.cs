using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using thta_ai.Models;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

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
    string prompt, Guid conversationId, bool isNewConversation,
    JsonElement schema, CancellationToken cancellationToken = default)
    {
        var planningSchema = MapRawSchemaToPlanning(schema);
        Log("PLANNING SCHEMA (pre-serialise)", JsonSerializer.Serialize(planningSchema, new JsonSerializerOptions { WriteIndented = true }));

        // STEP 1: PLAN (retries until a structurally valid plan is produced)
        var plan = await PlanPageWithRetryAsync(prompt, conversationId, planningSchema, cancellationToken);

        // STEP 2: EXPAND -> EMPTY SHELL (deterministic, no LLM, no validation needed)
        var pageShell = ExpandPlanFromRaw(plan, schema);
        Log("PAGE SHELL", JsonSerializer.Serialize(pageShell, new JsonSerializerOptions { WriteIndented = true }));

        // STEP 3: FILL CONTENT (retries until all fields are filled correctly)
        var contentConversationId = Guid.NewGuid();
        var finalPage = await GenerateContentWithRetryAsync(prompt, contentConversationId, pageShell, cancellationToken);

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
        Log("PLAN SYSTEM PROMPT", $"[~{systemPrompt.Length} chars]\n{systemPrompt}");

        var messages = new List<ChatMessage>
    {
        new() { Role = "system", Content = systemPrompt },
        new() { Role = "user", Content = prompt }
    };

        Log("PLAN USER PROMPT", prompt);

        var content = await SendChatAsync(conversationId, messages, ct, _options.PlanningContextSize);
        Log("PLAN LLM RESPONSE", content);

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("LLM returned an empty response during planning.");

        var json = ExtractJson(content);
        Log("PLAN EXTRACTED JSON", json.GetRawText());

        return JsonSerializer.Deserialize<LlmPagePlan>(json.GetRawText(), LlmJsonOptions)!;
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

        var content = await SendChatAsync(conversationId, messages, ct, _options.ContentContextSize);
        Log("CONTENT LLM RESPONSE", content);

        var json = ExtractJson(content);
        json = NormaliseContentJson(json);
        Log("CONTENT EXTRACTED JSON", json.GetRawText());

        return JsonSerializer.Deserialize<LlmPageResponse>(json.GetRawText(), LlmJsonOptions)!;
    }

    // ─── LLM RETRY LOGIC ─────────────────────────────────────────────────────

    private async Task<LlmPageResponse> GenerateContentWithRetryAsync(
    string prompt, Guid conversationId, LlmPageResponse pageShell, CancellationToken ct)
    {
        List<string> lastErrors = [];
        var currentPrompt = prompt;

        for (int attempt = 0; attempt <= _options.MaxContentRetries; attempt++)
        {
            var filledPage = await GenerateContentAsync(currentPrompt, conversationId, pageShell, ct);
            Log("CONTENT FILLED PAGE", JsonSerializer.Serialize(filledPage, new JsonSerializerOptions { WriteIndented = true }));

            var validation = ValidateContent(pageShell, filledPage);
            if (validation.IsValid)
                return filledPage;

            lastErrors = validation.Errors;
            Log("CONTENT VALIDATION FAILED", string.Join("\n", lastErrors));

            currentPrompt = BuildContentFixPrompt(prompt, lastErrors, pageShell);
        }

        throw new InvalidOperationException(
            "LLM failed to produce valid content after retries:\n" + string.Join("\n", lastErrors));
    }

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
        CancellationToken cancellationToken,
        int? contextSize = null)
    {
        var request = new ChatCompletionRequest
        {
            ConversationId = conversationId,
            Model = _options.Model,
            Stream = false,
            Messages = messages,
            Options = new ChatOptions
            {
                ContextSize = contextSize,
                Temperature = _options.Temperature,
                TopP = _options.TopP
            }
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
                var region = new LlmRegion { Name = regionAlias };

                if (!prop.TryGetProperty("blocks", out var blocks))
                {
                    pageType.Regions.Add(region);
                    continue;
                }

                var containerBlocks = new List<JsonElement>();
                var nonContainerBlocks = new List<JsonElement>();

                foreach (var block in blocks.EnumerateArray())
                {
                    var hasAreas = block.TryGetProperty("areas", out var areas) && areas.GetArrayLength() > 0;
                    if (hasAreas)
                        containerBlocks.Add(block);
                    else
                        nonContainerBlocks.Add(block);
                }

                // Names of all non-container blocks in this region — used to populate
                // area "allowed" lists when the raw schema's allowedBlocks is empty (= "any").
                var nonContainerDefs = nonContainerBlocks
                    .Select(b => BuildPlanningBlockDef(b.GetProperty("name").GetString() ?? "", b))
                    .ToList();

                region.AvailableBlockDefs.AddRange(nonContainerDefs);

                var nonContainerNames = nonContainerDefs.Select(d => d.Name).ToList();

                foreach (var block in containerBlocks)
                {
                    var blockName = block.GetProperty("name").GetString() ?? "";
                    var container = new LlmAreaContainer { BlockName = blockName };

                    foreach (var area in block.GetProperty("areas").EnumerateArray())
                    {
                        var areaAlias = area.GetProperty("alias").GetString() ?? "";
                        var columnSpan = area.TryGetProperty("columnSpan", out var cs) ? cs.GetInt32() : 1;

                        var allowedInArea = new List<string>();
                        if (area.TryGetProperty("allowedBlocks", out var allowedBlocks) && allowedBlocks.GetArrayLength() > 0)
                        {
                            foreach (var allowed in allowedBlocks.EnumerateArray())
                                allowedInArea.Add(allowed.GetProperty("name").GetString() ?? "");
                        }
                        else
                        {
                            // Empty allowedBlocks in Umbraco means "any block" — use the
                            // region's non-container blocks as the concrete allowed list.
                            allowedInArea.AddRange(nonContainerNames);
                        }

                        container.Areas.Add(new LlmPlanningArea
                        {
                            Alias = areaAlias,
                            ColumnSpan = columnSpan,
                            AllowedBlocks = allowedInArea
                        });
                    }

                    region.AreaContainers.Add(container);
                }

                // Only treat non-container blocks as direct (root-level) blocks
                // if this region has NO area containers at all. Otherwise they
                // belong exclusively inside the containers' areas.
                if (containerBlocks.Count == 0)
                {
                    region.DirectBlocks.AddRange(nonContainerDefs);
                }

                pageType.Regions.Add(region);
            }

            result.PageTypes.Add(pageType);
        }

        return result;
    }

    private LlmPlanningBlockDef BuildPlanningBlockDef(string name, JsonElement blockDef)
    {
        var slots = new List<LlmNestedSlot>();

        if (blockDef.TryGetProperty("properties", out var props))
        {
            foreach (var prop in props.EnumerateArray())
            {
                var propType = prop.GetProperty("type").GetString();
                if (propType is not ("Umbraco.BlockList" or "Umbraco.BlockGrid")) continue;

                var alias = prop.GetProperty("alias").GetString() ?? "";
                var allowed = new List<string>();

                if (prop.TryGetProperty("blocks", out var nestedBlocks))
                {
                    foreach (var nb in nestedBlocks.EnumerateArray())
                        allowed.Add(nb.GetProperty("name").GetString() ?? "");
                }

                slots.Add(new LlmNestedSlot { Alias = alias, AllowedBlocks = allowed });
            }
        }

        return new LlmPlanningBlockDef { Name = name, NestedSlots = slots };
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
            page2.FieldTypes[alias] = GetFieldMeta(prop);
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
                    block.FieldTypes[alias] = GetFieldMeta(prop);
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

    private static LlmFieldMeta GetFieldMeta(JsonElement prop)
    {
        var type = prop.GetProperty("type").GetString() ?? "";

        var meta = new LlmFieldMeta { Type = FriendlyFieldType(type) };

        if (type == "Umbraco.DropDown.Flexible" && prop.TryGetProperty("options", out var opts))
        {
            meta.Options = opts.EnumerateArray()
                .Select(o => o.GetString() ?? "")
                .Where(s => s != "")
                .ToList();
        }

        return meta;
    }

    private static string FriendlyFieldType(string umbracoType) => umbracoType switch
    {
        "Umbraco.TextBox" => "short text",
        "Umbraco.TextArea" => "long text",
        "Umbraco.RichText" => "rich text (HTML)",
        "Umbraco.TrueFalse" => "boolean — must be exactly \"true\" or \"false\"",
        "Umbraco.DropDown.Flexible" => "choice — must be exactly one of the listed options",
        "Umbraco.Integer" => "integer",
        "Umbraco.MediaPicker3" => "media reference — leave as empty string, cannot be generated",
        "Umbraco.MultiUrlPicker" => "link — leave as empty string, cannot be generated",
        _ => umbracoType
    };

    // ─── Prompt Building ──────────────────────────────────────────────────────

    private string BuildContentFixPrompt(string originalPrompt, List<string> errors, LlmPageResponse shell)
    {
        var json = JsonSerializer.Serialize(shell, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return $"""
        Your previous response was incomplete or invalid. Fix it and return only raw JSON.

        ORIGINAL REQUEST: {originalPrompt}

        ERRORS TO FIX:
        {string.Join("\n", errors)}

        RULES:
        - Return the COMPLETE JSON structure unchanged — same keys, same nesting, same arrays
        - Fill EVERY empty string ("") with realistic content
        - Do NOT add, remove, or rename any keys
        - Do NOT wrap in markdown
        - Return valid JSON only

        INPUT:
        {json}
        """;
    }

    private string BuildPlanFixPrompt(string originalPrompt, List<string> errors, LlmPlanningSchema schema)
    {
        var sb = new StringBuilder();
        foreach (var pt in schema.PageTypes)
        {
            sb.AppendLine($"PAGE TYPE: {pt.Name}");
            foreach (var r in pt.Regions)
            {
                sb.AppendLine($"  REGION: {r.Name}");

                foreach (var b in r.DirectBlocks)
                {
                    if (b.NestedSlots.Count == 0)
                    {
                        sb.AppendLine($"    Block: {b.Name}");
                    }
                    else
                    {
                        sb.AppendLine($"    Block: {b.Name}");
                        foreach (var slot in b.NestedSlots)
                        {
                            var allowed = slot.AllowedBlocks.Count > 0
                                ? string.Join(", ", slot.AllowedBlocks)
                                : "any block";
                            sb.AppendLine($"      nested slot \"{slot.Alias}\": {allowed}");
                        }
                    }
                }

                // AFTER
                foreach (var c in r.AreaContainers)
                {
                    sb.AppendLine($"    Area container: {c.BlockName}");
                    foreach (var area in c.Areas)
                    {
                        if (area.AllowedBlocks.Count == 0)
                        {
                            sb.AppendLine($"      area alias \"{area.Alias}\" (max 1 block):");
                            continue;
                        }

                        sb.AppendLine($"      area alias \"{area.Alias}\" (max 1 block):");
                        foreach (var allowedName in area.AllowedBlocks)
                        {
                            var def = r.AvailableBlockDefs.FirstOrDefault(b => b.Name == allowedName);
                            sb.AppendLine($"        * {allowedName}");
                            if (def is { NestedSlots.Count: > 0 })
                            {
                                foreach (var slot in def.NestedSlots)
                                {
                                    var slotAllowed = slot.AllowedBlocks.Count > 0
                                        ? string.Join(", ", slot.AllowedBlocks)
                                        : "any block";
                                    sb.AppendLine($"            nested slot \"{slot.Alias}\": {slotAllowed}");
                                }
                            }
                        }
                    }
                }
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
        - Use only the pageTypes, block names, area aliases, and nested slot names listed above
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

                if (region.DirectBlocks.Count > 0)
                {
                    sb.AppendLine("    Direct blocks:");
                    foreach (var b in region.DirectBlocks)
                    {
                        if (b.NestedSlots.Count == 0)
                        {
                            sb.AppendLine($"      - {b.Name}");
                        }
                        else
                        {
                            sb.AppendLine($"      - {b.Name}");
                            foreach (var slot in b.NestedSlots)
                            {
                                var allowed = slot.AllowedBlocks.Count > 0
                                    ? string.Join(", ", slot.AllowedBlocks)
                                    : "any block";
                                sb.AppendLine($"          nested slot \"{slot.Alias}\": {allowed}");
                            }
                        }
                    }
                }

                foreach (var container in region.AreaContainers)
                {
                    sb.AppendLine($"    Area container: {container.BlockName}");

                    var allSameAllowed = container.Areas.Count > 1 &&
                        container.Areas.All(a => a.AllowedBlocks.SequenceEqual(container.Areas[0].AllowedBlocks));

                    if (allSameAllowed)
                    {
                        var aliases = string.Join(", ", container.Areas.Select(a => $"\"{a.Alias}\""));
                        sb.AppendLine($"      - areas {aliases} (each max 1 block) — allowed blocks:");
                        AppendBlockList(sb, container.Areas[0].AllowedBlocks, region, "          ");
                    }
                    else
                    {
                        foreach (var area in container.Areas)
                        {
                            sb.AppendLine($"      - area alias \"{area.Alias}\" (span {area.ColumnSpan}, max 1 block) — allowed blocks:");
                            AppendBlockList(sb, area.AllowedBlocks, region, "          ");
                        }
                    }
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
            - Choose direct blocks only from each region's "Direct blocks" list
            - Some direct blocks have nested slots (e.g. "buttons", "accordions") — you may add 0 or more blocks from that slot's allowed list into "nestedBlocks"
            - Area containers (e.g. One Column Area, Two Column Area, Three Column Area) hold other blocks inside their named areas — only place blocks from an area's "allowed" list into that area
            - Do NOT invent block, region, area, or nested slot names
            - Do NOT fill in any content or field values
            - Return ONLY raw JSON, no markdown, no explanation

            ## OUTPUT FORMAT
            The structure below is an ILLUSTRATIVE EXAMPLE ONLY — it shows the JSON shape, not a template to copy.
            The block names, area aliases, nested slots, region names, and number of blocks used here are made up
            and will NOT match the real schema above. You must build the actual JSON using real names taken from
            the "AVAILABLE PAGE TYPES AND BLOCKS" section, choosing whatever blocks and counts genuinely fit the
            page being requested — not what's shown below.

            {
            "pageType": "<a real pageType name from above>",
            "regions": [
                {
                "name": "<a real region name>",
                "blocks": [
                    {
                    "id": "<a real direct-block name allowed in this region>",
                    "areas": {},
                    "nestedBlocks": {
                        "<a real nested slot alias>": [{ "id": "<a real allowed block name>" }]
                    }
                    },
                    {
                    "id": "<a real area-container block name>",
                    "areas": {
                        "<a real area alias>": [{ "id": "<a real allowed block name>" }]
                    }
                    }
                ]
                }
            ]
            }

            Notes on the example above:
            - "blocks" can contain any number of entries (zero or more), not exactly two
            - "areas" and "nestedBlocks" should only be included when the chosen block actually has them, and may contain zero or more entries
            - "regions" should include every region you're using content for, each with as many or as few blocks as makes sense
            """;
    }

    private void AppendBlockList(StringBuilder sb, List<string> allowedNames, LlmRegion region, string indent)
    {
        foreach (var allowedName in allowedNames)
        {
            var def = region.AvailableBlockDefs.FirstOrDefault(b => b.Name == allowedName);
            sb.AppendLine($"{indent}* {allowedName}");
            if (def is { NestedSlots.Count: > 0 })
            {
                foreach (var slot in def.NestedSlots)
                {
                    var slotAllowed = slot.AllowedBlocks.Count > 0
                        ? string.Join(", ", slot.AllowedBlocks)
                        : "any block";
                    sb.AppendLine($"{indent}    nested slot \"{slot.Alias}\": {slotAllowed}");
                }
            }
        }
    }

    private string BuildContentPrompt(LlmPageResponse page)
    {
        var fieldGuide = BuildFieldGuide(page);

        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return $"""
            You are a CMS content writer. Fill every empty string ("") in this JSON with realistic content.

            FIELD TYPES AND CONSTRAINTS:
            {fieldGuide}

            RULES:
            - Return the COMPLETE JSON structure unchanged — same keys, same nesting, same arrays
            - Replace ONLY empty string values ("")
            - For boolean fields, use exactly "true" or "false"
            - For choice fields, use exactly one of the listed options, verbatim, nothing else
            - For fields marked "leave as empty string, cannot be generated", do NOT fill them in — leave them as ""
            - Do NOT add, remove, or rename any keys
            - Do NOT wrap in markdown
            - Return valid JSON only

            INPUT:
            {json}
            """;
    }

    private string BuildFieldGuide(LlmPageResponse page)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Page fields:");
        foreach (var (alias, meta) in page.FieldTypes)
            sb.AppendLine(FormatFieldLine(alias, meta));

        foreach (var block in page.Blocks)
            AppendBlockFieldGuide(sb, block, "");

        return sb.ToString().TrimEnd();
    }

    private void AppendBlockFieldGuide(StringBuilder sb, LlmBlock block, string path)
    {
        var label = string.IsNullOrEmpty(path) ? block.Name : $"{block.Name} ({path})";
        sb.AppendLine($"Block '{label}':");
        foreach (var (alias, meta) in block.FieldTypes)
            sb.AppendLine(FormatFieldLine(alias, meta));

        foreach (var (areaAlias, children) in block.Areas)
            foreach (var child in children)
                AppendBlockFieldGuide(sb, child, areaAlias);

        foreach (var (slotAlias, children) in block.NestedBlocks)
            foreach (var child in children)
                AppendBlockFieldGuide(sb, child, slotAlias);
    }

    private static string FormatFieldLine(string alias, LlmFieldMeta meta)
    {
        var line = $"  - {alias}: {meta.Type}";
        if (meta.Options is { Count: > 0 })
            line += $" — options: {string.Join(", ", meta.Options)}";
        return line;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

        // Lookup of every block definition in the schema by name, regardless of
        // whether it's a region-level direct block, an area container, an
        // area-allowed block, or a nested-slot block. Used to recursively
        // validate children's own structure.
        var allBlockDefsByName = BuildBlockDefLookup(schema);

        foreach (var regionPlan in plan.Regions)
        {
            var region = pageType.Regions.FirstOrDefault(r => r.Name == regionPlan.Name);
            if (region is null)
            {
                var validRegions = string.Join(", ", pageType.Regions.Select(r => r.Name));
                errors.Add($"Unknown region '{regionPlan.Name}'. Valid regions for '{plan.PageType}': {validRegions}");
                continue;
            }

            var topLevelAllowed = region.DirectBlocks.Select(b => b.Name)
                .Concat(region.AreaContainers.Select(c => c.BlockName))
                .ToList();

            foreach (var block in regionPlan.Blocks ?? [])
            {
                if (!topLevelAllowed.Contains(block.Id))
                {
                    errors.Add(
                        $"Block '{block.Id}' is not allowed in region '{region.Name}'. " +
                        $"Allowed: {string.Join(", ", topLevelAllowed)}");
                    continue;
                }

                ValidatePlannedBlock(block, allBlockDefsByName, errors, $"region '{region.Name}'");
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    // Recursively validates a planned block's nestedBlocks and areas against its
    // own definition, then recurses into each child the same way.
    private void ValidatePlannedBlock(
        LlmPlannedBlock block,
        Dictionary<string, LlmPlanningBlockDef> blockDefsByName,
        List<string> errors,
        string contextLabel)
    {
        if (!blockDefsByName.TryGetValue(block.Id, out var def))
        {
            // Block exists somewhere as a name match was required to get here at the
            // top level, but for nested calls this means the name isn't a recognised
            // block definition at all anywhere in the schema.
            errors.Add($"Unknown block '{block.Id}' referenced in {contextLabel}.");
            return;
        }

        if (block.NestedBlocks is not null)
        {
            foreach (var (slotAlias, children) in block.NestedBlocks)
            {
                var slot = def.NestedSlots.FirstOrDefault(s => s.Alias == slotAlias);
                if (slot is null)
                {
                    var validSlots = string.Join(", ", def.NestedSlots.Select(s => s.Alias));
                    errors.Add($"Unknown nested slot '{slotAlias}' on block '{block.Id}' in {contextLabel}. Valid slots: {validSlots}");
                    continue;
                }

                foreach (var child in children)
                {
                    if (slot.AllowedBlocks.Count > 0 && !slot.AllowedBlocks.Contains(child.Id))
                    {
                        errors.Add(
                            $"Block '{child.Id}' is not allowed in nested slot '{slotAlias}' of '{block.Id}' in {contextLabel}. " +
                            $"Allowed: {string.Join(", ", slot.AllowedBlocks)}");
                        continue;
                    }

                    ValidatePlannedBlock(child, blockDefsByName, errors, $"nested slot '{slotAlias}' of '{block.Id}'");
                }
            }
        }

        if (block.Areas is not null && def.AreaContainerAreas is not null)
        {
            foreach (var (areaAlias, children) in block.Areas)
            {
                var area = def.AreaContainerAreas.FirstOrDefault(a => a.Alias == areaAlias);
                if (area is null)
                {
                    var validAreas = string.Join(", ", def.AreaContainerAreas.Select(a => a.Alias));
                    errors.Add($"Unknown area '{areaAlias}' in '{block.Id}' in {contextLabel}. Valid areas: {validAreas}");
                    continue;
                }

                // NEW — check capacity before validating individual children
                if (children.Count > area.MaxItems)
                {
                    errors.Add(
                        $"Area '{areaAlias}' of '{block.Id}' in {contextLabel} allows at most {area.MaxItems} block, got {children.Count}.");
                    continue;
                }

                foreach (var child in children)
                {
                    if (area.AllowedBlocks.Count > 0 && !area.AllowedBlocks.Contains(child.Id))
                    {
                        errors.Add(
                            $"Block '{child.Id}' is not allowed in area '{areaAlias}' of '{block.Id}' in {contextLabel}. " +
                            $"Allowed: {string.Join(", ", area.AllowedBlocks)}");
                        continue;
                    }

                    ValidatePlannedBlock(child, blockDefsByName, errors, $"area '{areaAlias}' of '{block.Id}'");
                }
            }
        }
    }

    private Dictionary<string, LlmPlanningBlockDef> BuildBlockDefLookup(LlmPlanningSchema schema)
    {
        var lookup = new Dictionary<string, LlmPlanningBlockDef>();

        void Add(LlmPlanningBlockDef def)
        {
            lookup[def.Name] = def; // last write wins on name collisions
        }

        foreach (var pageType in schema.PageTypes)
        {
            foreach (var region in pageType.Regions)
            {
                foreach (var direct in region.DirectBlocks)
                {
                    Add(direct);
                    foreach (var slot in direct.NestedSlots)
                        AddNestedSlotBlockDefs(slot, lookup);
                }

                foreach (var container in region.AreaContainers)
                {
                    Add(new LlmPlanningBlockDef
                    {
                        Name = container.BlockName,
                        NestedSlots = [],
                        AreaContainerAreas = container.Areas
                    });

                    // AFTER — AvailableBlockDefs is now a superset of DirectBlocks (DirectBlocks
                    // gets populated from it when the region has no containers), so this single
                    // lookup replaces the two-tier DirectBlocks/stub logic.
                    foreach (var area in container.Areas)
                    {
                        foreach (var allowedName in area.AllowedBlocks)
                        {
                            if (lookup.ContainsKey(allowedName)) continue;

                            var def = region.AvailableBlockDefs.FirstOrDefault(b => b.Name == allowedName)
                                      ?? new LlmPlanningBlockDef { Name = allowedName };

                            Add(def);
                            foreach (var slot in def.NestedSlots)
                                AddNestedSlotBlockDefs(slot, lookup);
                        }
                    }
                }
            }
        }

        return lookup;
    }

    private void AddNestedSlotBlockDefs(LlmNestedSlot slot, Dictionary<string, LlmPlanningBlockDef> lookup)
    {
        foreach (var allowedName in slot.AllowedBlocks)
        {
            if (!lookup.ContainsKey(allowedName))
                lookup[allowedName] = new LlmPlanningBlockDef { Name = allowedName };
        }
    }

    private ValidationResult ValidateContent(LlmPageResponse shell, LlmPageResponse filled)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (filled.PageType != shell.PageType)
            errors.Add($"pageType changed from '{shell.PageType}' to '{filled.PageType}'");

        foreach (var key in shell.Fields.Keys)
        {
            if (!filled.Fields.TryGetValue(key, out var value) || IsEmpty(value))
                warnings.Add($"Field '{key}' was not filled in.");
        }

        if (filled.Blocks.Count != shell.Blocks.Count)
            errors.Add($"Expected {shell.Blocks.Count} top-level blocks, got {filled.Blocks.Count}.");

        for (int i = 0; i < Math.Min(shell.Blocks.Count, filled.Blocks.Count); i++)
            ValidateBlockContent(shell.Blocks[i], filled.Blocks[i], errors, warnings);

        if (warnings.Count > 0)
            Log("CONTENT VALIDATION WARNINGS", string.Join("\n", warnings));

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    private void ValidateBlockContent(LlmBlock shellBlock, LlmBlock filledBlock, List<string> errors, List<string> warnings)
    {
        if (filledBlock.Id != shellBlock.Id)
            errors.Add($"Block id mismatch: expected '{shellBlock.Id}', got '{filledBlock.Id}'");

        foreach (var key in shellBlock.Fields.Keys)
        {
            if (!filledBlock.Fields.TryGetValue(key, out var value) || IsEmpty(value))
                warnings.Add($"Field '{key}' on block '{shellBlock.Name}' ({shellBlock.Id}) was not filled in.");
        }

        foreach (var (areaAlias, shellChildren) in shellBlock.Areas)
        {
            if (!filledBlock.Areas.TryGetValue(areaAlias, out var filledChildren))
            {
                errors.Add($"Area '{areaAlias}' on block '{shellBlock.Name}' missing in content response.");
                continue;
            }

            if (filledChildren.Count != shellChildren.Count)
            {
                errors.Add($"Area '{areaAlias}' on block '{shellBlock.Name}' expected {shellChildren.Count} children, got {filledChildren.Count}.");
                continue;
            }

            for (int i = 0; i < shellChildren.Count; i++)
                ValidateBlockContent(shellChildren[i], filledChildren[i], errors, warnings);
        }

        foreach (var (nestedAlias, shellChildren) in shellBlock.NestedBlocks)
        {
            if (!filledBlock.NestedBlocks.TryGetValue(nestedAlias, out var filledChildren))
                continue; // nested blocks are allowed to remain empty unless you want them filled too

            for (int i = 0; i < Math.Min(shellChildren.Count, filledChildren.Count); i++)
                ValidateBlockContent(shellChildren[i], filledChildren[i], errors, warnings);
        }
    }



    private static JsonElement NormaliseContentJson(JsonElement root)
    {
        // Already correct shape (case-insensitive check)
        if (root.TryGetProperty("blocks", out _) || root.TryGetProperty("Blocks", out _))
            return root;

        // Model reverted to the planning-phase "regions" wrapper — flatten it.
        var hasRegions = root.TryGetProperty("regions", out var regions)
                       || root.TryGetProperty("Regions", out regions);
        if (!hasRegions)
            return root; // nothing we can repair; let validation report the real error

        var node = JsonNode.Parse(root.GetRawText())!.AsObject();
        var regionsKey = node.ContainsKey("regions") ? "regions" : "Regions";

        var flatBlocks = new JsonArray();
        foreach (var region in node[regionsKey]!.AsArray())
        {
            var regionObj = region!.AsObject();
            var blocksKey = regionObj.ContainsKey("blocks") ? "blocks"
                           : regionObj.ContainsKey("Blocks") ? "Blocks" : null;
            if (blocksKey is null) continue;

            foreach (var block in regionObj[blocksKey]!.AsArray())
                flatBlocks.Add(block!.DeepClone());
        }

        node.Remove(regionsKey);
        node["blocks"] = flatBlocks;

        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static bool IsEmpty(object? value)
    {
        if (value is null) return true;
        if (value is JsonElement je) return je.ValueKind == JsonValueKind.String && je.GetString() == "";
        return value is string s && s == "";
    }
}