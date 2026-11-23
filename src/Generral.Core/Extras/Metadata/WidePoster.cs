using SkiaSharp;

namespace Generral.Core.Extras.Metadata;

/// <summary>
/// Fits a portrait poster into a 16:9 frame for Plex, which shows episode images 16:9 and would crop it: the whole poster at full
/// height over a blurred, darkened copy that covers the frame (spec 08 § Artwork).
/// </summary>
public static class WidePoster
{
    /// <summary>Bump when the drawing changes, so that fitted files are redrawn.</summary>
    public const int Version = 1;

    public const int Width = 1920;
    public const int Height = 1080;

    /// <summary>Images at least this wide (width / height) are kept as they are.</summary>
    public const double LandscapeAspect = 1.5;

    public static string Key(string url) => $"wide-v{Version}:{url}";

    /// <returns>A JPEG, <paramref name="source"/> itself when it is already landscape, or null when it can't be decoded.</returns>
    public static byte[]? Fit(byte[] source)
    {
        using var image = SKImage.FromEncodedData(source);
        if (image is null || image.Width == 0 || image.Height == 0)
        {
            return null;
        }

        if ((double)image.Width / image.Height >= LandscapeAspect)
        {
            return source;
        }

        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        var cover = Math.Max((float)Width / image.Width, (float)Height / image.Height);
        var coverWidth = image.Width * cover;
        var coverHeight = image.Height * cover;
        using (var blur = new SKPaint())
        {
            blur.ImageFilter = SKImageFilter.CreateBlur(40, 40, SKShaderTileMode.Clamp);
            blur.ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(0, 0, 0, 128), SKBlendMode.SrcOver);
            canvas.DrawImage(image, SKRect.Create((Width - coverWidth) / 2, (Height - coverHeight) / 2, coverWidth, coverHeight), sampling, blur);
        }

        var fitWidth = image.Width * (float)Height / image.Height;
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.DrawImage(image, SKRect.Create((Width - fitWidth) / 2, 0, fitWidth, Height), sampling, paint);
        }

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}
