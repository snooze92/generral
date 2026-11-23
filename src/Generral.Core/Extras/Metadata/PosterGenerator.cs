using SkiaSharp;

namespace Generral.Core.Extras.Metadata;

/// <summary>What a generated poster shows: <see cref="Heading"/> big, <see cref="Caption"/> small above it.</summary>
public sealed record GeneratedPoster(string Heading, string? Caption)
{
    /// <summary>Bump when the drawing changes, so that generated files are redrawn.</summary>
    public const int Version = 1;

    public string Key => $"generated:v{Version}:{Caption}\n{Heading}";
}

/// <summary>Plain 2:3 posters (title over a dark gradient) for series and seasons without artwork (spec 08 § Artwork).</summary>
public static class PosterGenerator
{
    public const int Width = 1000;
    public const int Height = 1500;
    private const float Margin = 90;

    private static readonly Lazy<SKTypeface> Font = new(() =>
    {
        using var stream = typeof(PosterGenerator).Assembly.GetManifestResourceStream("Generral.Core.NotoSans-Bold.ttf")!;
        return SKTypeface.FromStream(stream);
    });

    /// <returns>A JPEG.</returns>
    public static byte[] Render(GeneratedPoster poster)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        using (var background = new SKPaint())
        {
            background.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(Width * 0.35f, Height), [new SKColor(0x2A, 0x33, 0x40), new SKColor(0x0B, 0x0E, 0x13)], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Width, Height, background);
        }

        using var accent = new SKPaint { Color = new SKColor(0x35, 0xC5, 0xB4), IsAntialias = true };
        canvas.DrawRect(Margin, Height * 0.62f, 140, 10, accent);

        using var font = new SKFont(Font.Value);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var (headingLines, headingSize) = Fit(font, poster.Heading, 260, 90, 4);
        font.Size = headingSize;
        var lineHeight = headingSize * 1.08f;
        var y = Height * 0.62f - 50 - (headingLines.Count - 1) * lineHeight;
        foreach (var line in headingLines)
        {
            canvas.DrawText(line, Margin, y, SKTextAlign.Left, font, paint);
            y += lineHeight;
        }

        if (!string.IsNullOrWhiteSpace(poster.Caption))
        {
            var (captionLines, captionSize) = Fit(font, poster.Caption, 64, 40, 3);
            font.Size = captionSize;
            paint.Color = new SKColor(0xC8, 0xD0, 0xDA);
            y = Height * 0.62f + 30 + captionSize;
            foreach (var line in captionLines)
            {
                canvas.DrawText(line, Margin, y, SKTextAlign.Left, font, paint);
                y += captionSize * 1.2f;
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    /// <summary>The largest size (down to <paramref name="min"/>) at which the text wraps into at most <paramref name="maxLines"/> lines; the last line is cut with an ellipsis if needed.</summary>
    private static (List<string> Lines, float Size) Fit(SKFont font, string text, float max, float min, int maxLines)
    {
        var width = Width - 2 * Margin;
        List<string> lines = [];
        for (var size = max; size >= min; size -= 6)
        {
            font.Size = size;
            lines = Wrap(font, text, width);
            if (lines.Count <= maxLines && lines.All(l => font.MeasureText(l) <= width))
            {
                return (lines, size);
            }
        }

        font.Size = min;
        lines = Wrap(font, text, width);
        var kept = lines.Take(maxLines).Select(l => Ellipsize(font, l, width)).ToList();
        if (lines.Count > maxLines)
        {
            kept[^1] = Ellipsize(font, kept[^1] + "…", width);
        }

        return (kept, min);
    }

    private static List<string> Wrap(SKFont font, string text, float width)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && font.MeasureText(candidate) > width)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static string Ellipsize(SKFont font, string line, float width)
    {
        while (line.Length > 1 && font.MeasureText(line) > width)
        {
            line = line[..^2].TrimEnd() + "…";
        }

        return line;
    }
}
