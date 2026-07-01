public class ChatCompletionRequest
{
    public string? Model { get; set; }
    public bool Stream { get; set; } = false;
    public Guid ConversationId { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();
    public ChatOptions? Options { get; set; }
}

public class ChatOptions
{
    public int? ContextSize { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
}

public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}