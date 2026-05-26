public class ChatCompletionRequest
{
    public string? Intent { get; set; }
    public bool Stream { get; set; } = false;
    public Guid ConversationId { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();
}

public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}