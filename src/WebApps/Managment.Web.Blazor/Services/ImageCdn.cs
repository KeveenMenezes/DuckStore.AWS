namespace Managment.Web.Blazor.Services;

/// <summary>
/// Builds product image URLs from the configured CDN base (ADR-0034) — the API only carries
/// imageIds, never URLs. The processed bucket holds {160,320,640,1024,1600} x {avif,webp,jpg}
/// per imageId; the admin only ever needs the 160 webp thumbnail.
/// </summary>
public sealed class ImageCdn(string baseUrl)
{
    public const string Placeholder = "img/placeholder.svg";

    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public string Variant(string imageId, int width = 160, string format = "webp") =>
        $"{_baseUrl}/images/{imageId}/{width}.{format}";

    /// <summary>Thumbnail of the main image, or the placeholder when there are no images.</summary>
    public string ThumbOrPlaceholder(IReadOnlyList<ProductImageInfo> images)
    {
        var main = images.FirstOrDefault(i => i.IsMain) ?? images.FirstOrDefault();
        return main is null ? Placeholder : Variant(main.ImageId);
    }
}
