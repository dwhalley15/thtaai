using Umbraco.Cms.Core.Models;

public interface IMediaUploadService
{
    Task<IMedia> CreateFromUrlAsync(string url, string name, CancellationToken ct);
}