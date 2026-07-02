using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using thta_ai.Models;
using System.Text.Json;

namespace thta_ai.Controllers
{
    [ApiVersion("1.0")]
    [ApiExplorerSettings(GroupName = "thta_ai")]
    public class thtaaiApiController : thtaaiApiControllerBase
    {
        private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;

        private readonly ITextGenerationService _generator;

        private readonly IPageGenerationService _pageGenerator;

        private readonly IImageGenerationService _imageGenerator;

        private readonly IMediaUploadService _mediaUploadService;
        private readonly IContentMappingService _contentMappingService;

        public thtaaiApiController(IBackOfficeSecurityAccessor backOfficeSecurityAccessor, ITextGenerationService generator, IPageGenerationService pageGenerator, IImageGenerationService imageGenerator, IMediaUploadService mediaUploadService, IContentMappingService contentMappingService)
        {
            _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
            _generator = generator;
            _pageGenerator = pageGenerator;
            _imageGenerator = imageGenerator;
            _mediaUploadService = mediaUploadService;
            _contentMappingService = contentMappingService;
        }

        [HttpPost("generatePage")]
        [ProducesResponseType(typeof(GeneratePageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GeneratePage([FromBody] GeneratePageRequest request)
        {
            var result = await _pageGenerator.GeneratePageAsync(
                request.Prompt,
                request.ConversationId,
                request.IsNewConversation,
                request.Schema);

            return Ok(new GeneratePageResponse
            {
                ConversationId = result.ConversationId,
                RawOutput = result.RawOutput
            });
        }

        [HttpPost("generateImage")]
        [ProducesResponseType(typeof(List<ImageGenerateResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GenerateImage(
        [FromBody] ImageGenerateRequest request,
        CancellationToken cancellationToken)
        {
            var results = await _imageGenerator.GenerateImagesAsync(request.Prompt, cancellationToken);

            return Ok(results);
        }

        [HttpPost("uploadImage")]
        public async Task<IActionResult> UploadImage(
        [FromBody] UploadImageRequest request,
        CancellationToken ct)
        {
            var result = await _mediaUploadService.CreateFromUrlAsync(
                request.ImageUrl,
                request.AltText,
                ct);

            return Ok(new
            {
                mediaKey = result.Key
            });
        }

        [HttpPost("mapContent")]
        [ProducesResponseType(typeof(DocumentCreateModel), StatusCodes.Status200OK)]
        public IActionResult MapContent([FromBody] MapContentRequest request)
        {
            var pageSchema = _contentMappingService.MapRawSchemaToPageSchema(request.Schema, request.LlmResponse.PageType);
            var mapped = _contentMappingService.MapLlmResponse(request.LlmResponse, pageSchema);
            return Ok(mapped);
        }

        [HttpPost("generateStream")]
        [Produces("text/event-stream")]
        [ProducesResponseType(typeof(void), StatusCodes.Status200OK)]
        public async Task GenerateStream(
        [FromBody] GenerateRequest request,
        CancellationToken cancellationToken)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";

            await foreach (var chunk in _generator.StreamTextAsync(
                request.Prompt,
                request.ConversationId,
                request.IsNewConversation,
                request.Mode,
                cancellationToken))
            {
                var content = chunk.Choices?
                    .FirstOrDefault()?
                    .Delta?
                    .Content;

                if (!string.IsNullOrEmpty(content))
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        delta = content,
                        conversationId = chunk.ConversationId
                    });

                    await Response.WriteAsync($"data: {payload}\n\n");
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }

            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync(cancellationToken);

        }
    }
}
