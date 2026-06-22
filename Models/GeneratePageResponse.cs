using System.Text.Json;

namespace thta_ai.Models;

public class GeneratePageResponse
{
    public Guid ConversationId { get; set; }
    public JsonElement RawOutput { get; set; }
}