public class ChatCompletionChunk
{
    public string Id { get; set; }
    public string Object { get; set; }
    public long Created { get; set; }
    public string Model { get; set; }
    public Guid ConversationId { get; set; }
    public List<ChunkChoice> Choices { get; set; } = new();
}

public class ChunkChoice
{
    public int Index { get; set; }
    public Delta Delta { get; set; }
}

public class Delta
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}
