using thta_ai.Models;
using System.Text.Json;
public interface IContentMappingService
{
    DocumentCreateModel MapLlmResponse(LlmPageResponse llmResponse, PageSchema schema);

    PageSchema MapRawSchemaToPageSchema(JsonElement rawSchema, string pageType);
}