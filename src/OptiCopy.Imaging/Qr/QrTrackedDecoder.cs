namespace OptiCopy.Imaging.Qr;

/// <summary>
/// Decimen-style QR tracking facade.
///
/// The reference receiver keeps the previous symbol quadrilateral and module
/// count, tries a cheap tracked decode first, then falls back to a full scan.
/// ZXing.Net does not expose decimen-codec's internal GridSampler/QRCode decoder
/// primitives, so the managed implementation preserves the same control flow:
/// cached geometry narrows the search to a small crop and ±2 px refinement
/// window; a miss falls back to the normal QR detector.
/// </summary>
public sealed class QrTrackedDecoder
{
    private const int DriftPixels = 2;
    private readonly QrCodeDecoder _decoder;

    public QrTrackedDecoder(bool tryHarder = true)
    {
        _decoder = new QrCodeDecoder(tryHarder);
    }

    public QrTrackingResult? Decode(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        QrPixelFormat pixelFormat,
        QrQuad? previousQuad = null)
    {
        if (previousQuad is { } quad && quad.IsUsable)
        {
            var tracked = TryTracked(
                pixels,
                width,
                height,
                pixelFormat,
                quad);

            if (tracked is not null)
                return tracked with { Tracked = true };
        }

        var full = _decoder.DecodeWithPosition(pixels, width, height, pixelFormat);
        return full is null
            ? null
            : new QrTrackingResult(
                full.Result,
                full.Quad,
                full.Modules,
                Tracked: false);
    }

    private QrTrackingResult? TryTracked(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        QrPixelFormat pixelFormat,
        QrQuad quad)
    {
        var bounds = quad.GetBounds(width, height).Expand(DriftPixels, width, height);
        if (bounds.Width < 8 || bounds.Height < 8)
            return null;

        for (var dy = -DriftPixels; dy <= DriftPixels; dy++)
        {
            for (var dx = -DriftPixels; dx <= DriftPixels; dx++)
            {
                var crop = bounds.Offset(dx, dy).Clamp(width, height);
                if (crop.Width < 8 || crop.Height < 8)
                    continue;

                var cropped = CopyCrop(pixels, width, height, pixelFormat, crop);
                var result = _decoder.DecodeWithPosition(
                    cropped,
                    crop.Width,
                    crop.Height,
                    pixelFormat);

                if (result is null)
                    continue;

                return new QrTrackingResult(
                    result.Result,
                    result.Quad.Offset(crop.X, crop.Y),
                    result.Modules,
                    Tracked: true);
            }
        }

        return null;
    }

    private static byte[] CopyCrop(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        QrPixelFormat pixelFormat,
        QrRect rect)
    {
        var bpp = pixelFormat switch
        {
            QrPixelFormat.Gray8 => 1,
            QrPixelFormat.Rgb24 => 3,
            QrPixelFormat.Bgra32 => 4,
            QrPixelFormat.Rgba32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, null)
        };

        var output = new byte[checked(rect.Width * rect.Height * bpp)];
        var sourceStride = checked(width * bpp);
        var destinationStride = checked(rect.Width * bpp);

        for (var y = 0; y < rect.Height; y++)
        {
            var sourceOffset = checked((rect.Y + y) * sourceStride + rect.X * bpp);
            var destinationOffset = y * destinationStride;
            pixels.Slice(sourceOffset, destinationStride)
                .CopyTo(output.AsSpan(destinationOffset, destinationStride));
        }

        return output;
    }
}

public sealed record QrTrackingResult(
    QrDecodeResult Result,
    QrQuad Quad,
    int Modules,
    bool Tracked);

public readonly record struct QrPoint(double X, double Y)
{
    public QrPoint Offset(int dx, int dy) => new(X + dx, Y + dy);
}

public readonly record struct QrQuad(
    QrPoint TopLeft,
    QrPoint TopRight,
    QrPoint BottomRight,
    QrPoint BottomLeft)
{
    public bool IsUsable => Width >= 4 && Height >= 4;

    public double Width => Math.Max(
        Distance(TopLeft, TopRight),
        Distance(BottomLeft, BottomRight));

    public double Height => Math.Max(
        Distance(TopLeft, BottomLeft),
        Distance(TopRight, BottomRight));

    public QrRect GetBounds(int imageWidth, int imageHeight)
    {
        var minX = Math.Floor(Math.Min(Math.Min(TopLeft.X, TopRight.X), Math.Min(BottomLeft.X, BottomRight.X)));
        var maxX = Math.Ceiling(Math.Max(Math.Max(TopLeft.X, TopRight.X), Math.Max(BottomLeft.X, BottomRight.X)));
        var minY = Math.Floor(Math.Min(Math.Min(TopLeft.Y, TopRight.Y), Math.Min(BottomLeft.Y, BottomRight.Y)));
        var maxY = Math.Ceiling(Math.Max(Math.Max(TopLeft.Y, TopRight.Y), Math.Max(BottomLeft.Y, BottomRight.Y)));

        var x = Math.Clamp((int)minX, 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp((int)minY, 0, Math.Max(0, imageHeight - 1));
        var right = Math.Clamp((int)maxX, x + 1, imageWidth);
        var bottom = Math.Clamp((int)maxY, y + 1, imageHeight);
        return new QrRect(x, y, right - x, bottom - y);
    }

    public QrQuad Offset(int x, int y) => new(
        TopLeft.Offset(x, y),
        TopRight.Offset(x, y),
        BottomRight.Offset(x, y),
        BottomLeft.Offset(x, y));

    private static double Distance(QrPoint a, QrPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public readonly record struct QrRect(int X, int Y, int Width, int Height)
{
    public QrRect Expand(int pixels, int imageWidth, int imageHeight) => new(
        Math.Max(0, X - pixels),
        Math.Max(0, Y - pixels),
        Math.Min(imageWidth - Math.Max(0, X - pixels), Width + pixels * 2),
        Math.Min(imageHeight - Math.Max(0, Y - pixels), Height + pixels * 2));

    public QrRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public QrRect Clamp(int imageWidth, int imageHeight)
    {
        var x = Math.Clamp(X, 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp(Y, 0, Math.Max(0, imageHeight - 1));
        var right = Math.Clamp(X + Width, x + 1, imageWidth);
        var bottom = Math.Clamp(Y + Height, y + 1, imageHeight);
        return new QrRect(x, y, right - x, bottom - y);
    }
}
