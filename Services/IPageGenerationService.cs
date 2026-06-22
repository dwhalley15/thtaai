using thta_ai.Models;
using System.Text.Json;
public interface IPageGenerationService
{
    Task<GeneratePageResponse> GeneratePageAsync(string prompt, Guid conversationId, bool isNewConversation, JsonElement schema, CancellationToken cancellationToken = default);
}