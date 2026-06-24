public interface IContentMappingService
{
    DocumentCreateModel MapLlmResponse(LlmPageResponse llmResponse, PageSchema schema);
}