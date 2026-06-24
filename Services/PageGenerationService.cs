using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using thta_ai.Models;
using System.Runtime.CompilerServices;

public class PageGenerationService : IPageGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly AiGenerationOptions _options;

    public PageGenerationService(HttpClient httpClient, IOptions<AiGenerationOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<GeneratePageResponse> GeneratePageAsync(string prompt, Guid conversationId, bool isNewConversation, JsonElement schema, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        if (isNewConversation)
        {
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = BuildSystemPrompt(schema)
            });
        }

        messages.Add(new ChatMessage
        {
            Role = "user",
            Content = prompt
        });

        var request = new ChatCompletionRequest
        {
            Model = _options.Model,
            Stream = false,
            ConversationId = conversationId,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(request);

        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl}"
        );

        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        httpRequest.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var response = await _httpClient.SendAsync(httpRequest, linkedCts.Token);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        var responseDoc = JsonDocument.Parse(body);

        var content =
            responseDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

        var jsonStart = content!.IndexOfAny(new[] { '{', '[' });

        if (jsonStart == -1)
        {
            throw new InvalidOperationException($"No valid JSON found in model response: {content}");
        }

        var trimmed = content.Substring(jsonStart).TrimEnd();

        // Strip trailing code fence if present
        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3].TrimEnd();
        }

        // If the JSON was truncated, close any unclosed braces/brackets
        trimmed = CloseIncompleteJson(trimmed);

        try
        {
            var generatedPage = JsonDocument.Parse(trimmed);
            return new GeneratePageResponse
            {
                ConversationId = conversationId,
                RawOutput = generatedPage.RootElement.Clone()
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse model response as JSON: {ex.Message}\nContent: {content}");
        }
    }



    // ─── Prompt Building ──────────────────────────────────────────────────────

    private string BuildSystemPrompt(JsonElement schema)
    {
        var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        // Deserialize to typed models so we can build the block summary
        var schemas = JsonSerializer.Deserialize<List<PageSchema>>(schemaJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var blockSummary = BuildBlockSummary(schemas);

        var tasks = """
            - Choose the most appropriate pageType from the schema.
            - Populate fields with realistic content based on the prompt.
            - Choose blocks only from the schema. Copy block names and field names exactly.
            """;

        var exampleOutput = """
            {
              "pageType": "<pageType from schema>",
              "fields": {
                "<fieldAlias>": "<value>"
              },
              "blocks": [
                {
                  "block": "<exact block name from schema directBlocks>",
                  "fields": { "<fieldAlias>": "<value>" },
                  "nestedBlocks": {
                    "<nestedBlockField>": [
                      {
                        "block": "<exact block name from schema>",
                        "fields": { "<fieldAlias>": "<value>" }
                      }
                    ]
                  }
                },
                {
                  "block": "<exact areaContainer name from schema>",
                  "areas": {
                    "<area alias from schema>": [
                      {
                        "block": "<exact block name from schema area allowedBlocks>",
                        "fields": { "<fieldAlias>": "<value>" }
                      }
                    ]
                  }
                }
              ]
            }
            """;

        var rules = """
            - Return only valid JSON. No text before or after. No markdown. No code fences.
            - pageType must exactly match a pageType in the schema.
            - block must exactly match a name from directBlocks or areaContainers in the schema. No exceptions.
            - field names must exactly match those in the schema for that block.
            - blockProperty aliases like "headerContent" or "mainContent" are NOT block names.
            - Area container blocks use "areas" not "nestedBlocks". Key by the area alias from the schema.
            - Blocks only in areaContainers must go inside an area container, never at the top level.
            - Blocks in directBlocks go at the top level.
            - nestedBlocks is only for block list fields on a block (e.g. buttons, cards).
            - Do not invent block names, field names, or page types not present in the schema.
            """;

        return $"""
            You are a content generation engine for a CMS.

            ALLOWED BLOCKS (copy names exactly):
            {blockSummary}

            FULL SCHEMA:
            {schemaJson}

            Your task:
            {tasks}

            Output structure:
            {exampleOutput}

            Rules:
            {rules}
            """;
    }

    private static string BuildBlockSummary(List<PageSchema> schemas)
    {
        var lines = new List<string>();

        foreach (var schema in schemas)
        {
            lines.Add($"pageType: {schema.PageType}");

            foreach (var bp in schema.BlockProperties)
            {
                lines.Add($"  {bp.Alias}:");

                foreach (var b in bp.DirectBlocks)
                    lines.Add($"    - \"{b.Name}\" (place at top level of blocks array)");

                foreach (var ac in bp.AreaContainers)
                {
                    var areaAliases = string.Join(", ", ac.Areas.Select(a => $"\"{a.Alias}\""));
                    lines.Add($"    - \"{ac.Name}\" (area container — use \"areas\" key with: {areaAliases})");

                    foreach (var area in ac.Areas)
                    {
                        var allowed = string.Join(", ", area.AllowedBlocks.Select(ab => $"\"{ab.Name}\""));
                        lines.Add($"        {area.Alias} accepts: {allowed}");
                    }
                }
            }
        }

        return string.Join("\n", lines);
    }

    // ─── JSON Helpers ─────────────────────────────────────────────────────────

    private static string CloseIncompleteJson(string json)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escape = false;

        foreach (var ch in json)
        {
            if (escape) { escape = false; continue; }
            if (ch == '\\' && inString) { escape = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (ch == '{') stack.Push('}');
            else if (ch == '[') stack.Push(']');
            else if (ch == '}' || ch == ']') stack.Pop();
        }

        if (inString) json += '"';

        var sb = new StringBuilder(json);
        while (stack.Count > 0)
            sb.Append(stack.Pop());

        return sb.ToString();
    }


}