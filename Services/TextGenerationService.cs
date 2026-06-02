using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using thta_ai.Models;
using System.Runtime.CompilerServices;

public class TextGenerationService : ITextGenerationService
{

    private readonly HttpClient _httpClient;
    private readonly AiGenerationOptions _options;

    public TextGenerationService(HttpClient httpClient, IOptions<AiGenerationOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<GenerateResponse> GenerateTextAsync(string prompt, Guid conversationId, bool isNewConversation, CancellationToken cancellationToken = default)
    {

        var messages = new List<ChatMessage>();
        if (isNewConversation)
        {
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = BuildSystemPrompt()
            });
        }

        messages.Add(new ChatMessage
        {
            Role = "user",
            Content = prompt
        });

        var request = new ChatCompletionRequest
        {
            Intent = "Reasoning",
            Stream = false,
            ConversationId = conversationId,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(request);

        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl}/v1/chat/completions"
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

        var parsed = JsonDocument.Parse(body);

        return new GenerateResponse
        {
            ConversationId = conversationId,
            Text = parsed.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty
        };
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamTextAsync(
    string prompt,
    Guid conversationId,
    bool isNewConversation,
    string mode = "text",
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();

        if (isNewConversation)
        {
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = mode == "html" ? BuildHtmlSystemPrompt() : BuildSystemPrompt()
            });
        }

        messages.Add(new ChatMessage
        {
            Role = "user",
            Content = prompt
        });

        var request = new ChatCompletionRequest
        {
            Intent = "Reasoning",
            Stream = true,
            ConversationId = conversationId,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(request);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.BaseUrl}/v1/chat/completions");

        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data:"))
            {
                continue;
            }
        
            var payload = line["data:".Length..].Trim();

            if (payload == "[DONE]")
            {
                yield break;
            }
                
            var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(
            payload,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (chunk == null)
            {
                continue;
            }

            var content = chunk.Choices?
                .FirstOrDefault()?
                .Delta?
                .Content;

            yield return chunk;
        }
    }

    private string BuildSystemPrompt()
    {
        return """
        You generate content based on the user's request.

        Rules:
        - Your entire response must consist only of the requested content.
        - Do not explain, comment, or describe your process
        - Do not prepend labels such as "Answer:", "Result:", or "Output:"
        - Do not wrap the response in quotes
        - Do not use markdown unless explicitly requested
        - Do not return JSON unless explicitly requested
        - Do not use code fences
        - Preserve plain text formatting where appropriate
        """;
    }

    private string BuildHtmlSystemPrompt()
{
    return """
    You generate HTML content based on the user's request for use in a rich text editor.

    Rules:
    - Your entire response must be valid, clean HTML only
    - Do not include <html>, <head>, or <body> tags
    - Do not explain, comment, or describe your process
    - Do not prepend labels such as "Answer:", "Result:", or "Output:"
    - Use semantic elements: <p>, <h2>, <h3>, <ul>, <ol>, <li>, <strong>, <em>, <a>, <blockquote> etc.
    - Do not use inline styles or <style> tags
    - Do not use markdown
    - Do not use code fences
    - Ensure all tags are properly closed
    """;
}

}