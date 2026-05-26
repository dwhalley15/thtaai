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

        public thtaaiApiController(IBackOfficeSecurityAccessor backOfficeSecurityAccessor, ITextGenerationService generator)
        {
            _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
            _generator = generator;
        }

        [HttpGet("ping")]
        [ProducesResponseType<string>(StatusCodes.Status200OK)]
        public string Ping() => "Pong";

        [HttpGet("whatsTheTimeMrWolf")]
        [ProducesResponseType(typeof(DateTime), 200)]
        public DateTime WhatsTheTimeMrWolf() => DateTime.Now;

        [HttpGet("whatsMyName")]
        [ProducesResponseType<string>(StatusCodes.Status200OK)]
        public string WhatsMyName()
        {
            // So we can see a long request in the dashboard with a spinning progress wheel
            Thread.Sleep(2000);

            var currentUser = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
            return currentUser?.Name ?? "I have no idea who you are";
        }

        [HttpGet("whoAmI")]
        [ProducesResponseType<IUser>(StatusCodes.Status200OK)]
        public IUser? WhoAmI() => _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;


        [HttpPost("generate")]
        [ProducesResponseType(typeof(GenerateResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Generate([FromBody] GenerateRequest request)
        {
            var result = await _generator.GenerateTextAsync(request.Prompt, request.ConversationId, request.IsNewConversation);

            return Ok(new GenerateResponse
            {
                ConversationId = result.ConversationId,
                Text = result.Text
            });
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
