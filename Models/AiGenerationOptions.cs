public class AiGenerationOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "mistral";
    public int TimeoutSeconds { get; set; } = 300;
    public string PixabayKey { get; set; } = "";
    public int MaxPlanRetries { get; set; } = 2;
    public int MaxContentRetries { get; set; } = 1;
    public int PlanningContextSize { get; set; } = 8192;
    public int ContentContextSize { get; set; } = 8192;
    public double Temperature { get; set; } = 0.3;
    public double TopP { get; set; } = 0.85;
}