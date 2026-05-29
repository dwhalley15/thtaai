namespace thta_ai.Models;

public class GenerateRequest
{
    public Guid ConversationId { get; set; }
    public string Prompt { get; set; } = string.Empty;

    public bool IsNewConversation { get; set; }

    public string Mode { get; set; } = "text";
}