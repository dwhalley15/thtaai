using thta_ai.Models;
public interface IContentMappingService
{
    DocumentCreateModel MapLlmResponse(LlmPageResponse llmResponse, PageSchema schema);
}