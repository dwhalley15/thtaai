using System.Text.Json.Serialization;

public class PixabayQuery
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("image_type")]
    public string ImageType { get; set; } = "";

    [JsonPropertyName("orientation")]
    public string Orientation { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("colors")]
    public string Colors { get; set; } = "";

    [JsonPropertyName("safesearch")]
    public bool SafeSearch { get; set; }
}

public class PixabayResponse
{
    [JsonPropertyName("hits")]
    public List<PixabayHit> Hits { get; set; } = new();
}

public class PixabayHit
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("pageURL")]
    public string PageURL { get; set; } = string.Empty;

    [JsonPropertyName("largeImageURL")]
    public string LargeImageURL { get; set; } = string.Empty;

    [JsonPropertyName("previewURL")]
    public string PreviewURL { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;
}