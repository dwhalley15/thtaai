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

    private static string CloseIncompleteJson(string json)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escape = false;

        foreach (var ch in json)
        {
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

            if (inString) continue;

            if (ch == '{') stack.Push('}');
            else if (ch == '[') stack.Push(']');
            else if (ch == '}' || ch == ']') stack.Pop();
        }

        // If we were cut off mid-string, close it
        if (inString) json += '"';

        // Close any unclosed structures in reverse order
        var sb = new System.Text.StringBuilder(json);
        while (stack.Count > 0)
        {
            sb.Append(stack.Pop());
        }

        return sb.ToString();
    }

    private string BuildSystemPrompt(JsonElement schema)
    {
        var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var tasks = """
        - Choose the most appropriate pageType from the schema based on the user's prompt.
        - Populate the page fields with realistic content appropriate to the prompt.
        - Select appropriate blocks from availableBlocks and populate their fields with content.
        - Only use block names and field names that exist in the schema.
        """;

        var exampleOutput = """
        {
        "pageType": "contentPage",
        "fields": {
            "metaTitle": "Page title",
            "hideFromSiteMap": false
        },
        "blocks": [
            {
            "block": "Medium Header",
            "fields": {
                "title": "Welcome",
                "text": "Intro text",
                "backgroundImage": ""
            },
            "nestedBlocks": {
                "buttons": [
                {
                    "block": "Button",
                    "fields": { "link": "/contact", "style": "primary" }
                }
                ]
            }
            },
            {
            "block": "Text Block",
            "fields": { "title": "Section", "text": "Body content" }
            }
        ]
        }
        """;

        var rules = """
        - Your entire response must begin with { and end with }.
        - Do not write any text before or after the JSON object.
        - Do not acknowledge this prompt, do not explain your response.
        - Return only valid JSON, no markdown, no code fences.
        - pageType must exactly match one of the provided pageType values.
        - block names must exactly match one of the provided availableBlocks names.
        - field names must exactly match those listed for that block or page type.
        - Do not invent fields or block names not present in the schema.
        - Omit fields you have no meaningful content for rather than using empty strings.
        - The response must be parseable by System.Text.Json without modification.
        - Some blocks have nestedBlocks — these are block list properties on the block itself. Populate them under a "nestedBlocks" key using the field name as the key, not as siblings in the top-level blocks array.
        """;

        return $"""
        You are a content generation engine for a CMS.

        The following is the available page schema. Each entry represents a page type with its
        compositions, allowed child pages, and a full recursive property tree. BlockGrid and
        BlockList properties include their allowed blocks and each block's own properties.
        DropDown properties include their available options.

        AVAILABLE SCHEMA:
        {schemaJson}

        Your task:
        {tasks}

        You must return a single valid JSON object and nothing else, in this structure:
        {exampleOutput}

        Rules:
        {rules}
        """;
    }
}