using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using thta_ai.Models;
using System.Runtime.CompilerServices;

public class ImageGenerationService : IImageGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly AiGenerationOptions _options;

    public ImageGenerationService(HttpClient httpClient, IOptions<AiGenerationOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<List<ImageGenerateResponse>> GenerateImagesAsync(string prompt, CancellationToken ct)
    {

        var messages = new List<ChatMessage>
        {
            new ChatMessage
            {
                Role = "user",
                Content = BuildImageSearchPrompt(prompt)
            }
        };

        var request = new ChatCompletionRequest
        {
            Model = _options.Model,
            Stream = false,
            Messages = messages,
            Options = new ChatOptions
            {
                ContextSize = _options.ContentContextSize,
                Temperature = _options.Temperature,
                TopP = _options.TopP
            }
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
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var response = await _httpClient.SendAsync(httpRequest, linkedCts.Token);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        var parsed = JsonDocument.Parse(body);

        var text = parsed.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        var extractedJson = ExtractJson(text);

        var aiQuery = JsonSerializer.Deserialize<PixabayQuery>(extractedJson);

        Console.WriteLine($"Query: '{aiQuery?.Query}'");
        Console.WriteLine($"ImageType: '{aiQuery?.ImageType}'");
        Console.WriteLine($"Orientation: '{aiQuery?.Orientation}'");
        Console.WriteLine($"Category: '{aiQuery?.Category}'");
        Console.WriteLine($"Colors: '{aiQuery?.Colors}'");

        Console.WriteLine("LLM returned Pixabay query: " + JsonSerializer.Serialize(aiQuery));

        if (aiQuery == null)
            return new List<ImageGenerateResponse>();

        return await SearchPixabayAsync(aiQuery);
    }

    private string BuildImageSearchPrompt(string userPrompt)
    {
        return $$"""
        You are an expert image search query optimizer for Pixabay.

        Convert the user request into HIGH QUALITY search parameters.

        Return ONLY valid JSON:

        {
        "query": "comma or space separated keywords optimized for stock photography search",
        "category": "",
        "orientation": "orientation must be exactly one of:
                            - all
                            - horizontal
                            - vertical",
        "colors": "",
        "image_type": "photo",
        "safesearch": true
        }

        CRITICAL RULES:
        - Expand short phrases into visual keyword clusters
        - Remove filler words (a, the, of, looking for, image of)
        - Add visual descriptors (nature, macro, close-up, studio, etc.)
        - Prefer 4–8 keywords in "query"
        - Never use full sentences
        - Think like a stock photographer tagging images
        - Always prioritise searchability over grammar

        User request:
        "{{userPrompt}}"
        """;
    }

    private string ExtractJson(string text)
    {
        text = text.Trim();

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start == -1 || end == -1)
            throw new Exception("No JSON object found in LLM output.");

        return text.Substring(start, end - start + 1);
    }

    private async Task<List<ImageGenerateResponse>> SearchPixabayAsync(PixabayQuery q)
    {
        var url = new StringBuilder("https://pixabay.com/api/?key=")
            .Append(_options.PixabayKey);

        if (!string.IsNullOrWhiteSpace(q.Query))
        {
            var query = q.Query
            .Replace(",", "")
                .Trim();
            url.Append("&q=")
                .Append(Uri.EscapeDataString(query));
        }

        if (!string.IsNullOrWhiteSpace(q.ImageType))
            url.Append("&image_type=").Append(q.ImageType);

        if (!string.IsNullOrWhiteSpace(q.Orientation))
            url.Append("&orientation=").Append(q.Orientation);

        if (!string.IsNullOrWhiteSpace(q.Category))
            url.Append("&category=").Append(q.Category);

        if (!string.IsNullOrWhiteSpace(q.Colors))
            url.Append("&colors=").Append(q.Colors);

        url.Append("&safesearch=true");
        url.Append("&per_page=6");

        using var client = new HttpClient();

        var json = await client.GetStringAsync(url.ToString());

        var data = JsonSerializer.Deserialize<PixabayResponse>(json);

        return data?.Hits?
            .Select(x => new ImageGenerateResponse
            {
                MediaUrl = x.LargeImageURL,
                SourceUrl = x.PageURL,
                PreviewUrl = x.PreviewURL,
                AltText = x.Tags,
                Title = x.Tags
            })
            .ToList()
            ?? new List<ImageGenerateResponse>();
    }
}