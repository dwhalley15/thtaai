using thta_ai.Models;

public interface ITextGenerationService
{
    Task<GenerateResponse> GenerateTextAsync(string prompt, Guid conversationId, bool isNewConversation, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatCompletionChunk> StreamTextAsync(
        string prompt,
        Guid conversationId,
        bool isNewConversation,
        string mode = "text",
        CancellationToken cancellationToken = default);
}