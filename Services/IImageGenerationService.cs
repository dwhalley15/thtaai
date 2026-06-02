public interface IImageGenerationService
{
    Task<List<ImageGenerateResponse>> GenerateImagesAsync(string prompt, CancellationToken ct);
}