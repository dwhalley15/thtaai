using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;

public class MediaUploadService : IMediaUploadService
{
    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly IContentTypeBaseServiceProvider _contentTypeBaseServiceProvider;
    private readonly MediaUrlGeneratorCollection _mediaUrlGeneratorCollection;
    private readonly IHttpClientFactory _httpClientFactory;

    public MediaUploadService(
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        IShortStringHelper shortStringHelper,
        IContentTypeBaseServiceProvider contentTypeBaseServiceProvider,
        MediaUrlGeneratorCollection mediaUrlGeneratorCollection,
        IHttpClientFactory httpClientFactory)
    {
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _shortStringHelper = shortStringHelper;
        _contentTypeBaseServiceProvider = contentTypeBaseServiceProvider;
        _mediaUrlGeneratorCollection = mediaUrlGeneratorCollection;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IMedia> CreateFromUrlAsync(string url, string altText, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var bytes = await client.GetByteArrayAsync(url, ct);
        using var stream = new MemoryStream(bytes);

        var fileName = $"ai-image-{Guid.NewGuid():N}.jpg";

        var media = _mediaService.CreateMedia(fileName, Constants.System.Root, Constants.Conventions.MediaTypes.Image);

        media.SetValue(
            _mediaFileManager,
            _mediaUrlGeneratorCollection,
            _shortStringHelper,
            _contentTypeBaseServiceProvider,
            Constants.Conventions.Media.File,
            fileName,
            stream);

        media.SetValue("alt", altText);

        _mediaService.Save(media);

        return media;
    }
}