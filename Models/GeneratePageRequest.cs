using System.Text.Json;

public class GeneratePageRequest
{
    public string Prompt { get; set; } = "";
    public Guid ConversationId { get; set; }
    public bool IsNewConversation { get; set; }
    public JsonElement Schema { get; set; } 
}