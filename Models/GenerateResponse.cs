namespace thta_ai.Models;

public class GenerateResponse
{
    public Guid ConversationId { get; set; }
    public string Text { get; set; } = string.Empty;
}