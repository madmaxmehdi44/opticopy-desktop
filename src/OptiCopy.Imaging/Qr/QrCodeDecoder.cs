using ZXing;
using ZXing.Common;

namespace OptiCopy.Imaging.Qr;

public sealed record QrDecodeResult(string Text, BarcodeFormat Format, int[]? RawBytes = null);

public sealed record QrPositionedDecodeResult(
    QrDecodeResult Result,
    QrQuad Quad,
    int Modules);

public sealed class QrCodeDecoder
{
    private readonly BarcodeReaderGeneric _reader;
    // ZXing.Net documents PureBarcode as appropriate for synthetic monochrome
    // symbols. Keep it as a fallback only; the normal detector remains first so
    // camera frames are still decoded through the ordinary acquisition path.
    private readonly BarcodeReaderGeneric _pureReader;

    public QrCodeDecoder(bool tryHarder = false)
    {
        _reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = tryHarder,
                TryInverted = false,
                CharacterSet = "UTF-8"
            }
        };

        _pureReader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = tryHarder,
                TryInverted = false,
                PureBarcode = true,
                CharacterSet = "UTF-8"
            }
        };
    }

    public QrDecodeResult? Decode(ReadOnlySpan<byte> pixels, int width, int height, QrPixelFormat pixelFormat)
    {
        var positioned = DecodeWithPosition(pixels, width, height, pixelFormat);
        return positioned?.Result;
    }

    /// <summary>
    /// Full QR acquisition with the geometric information needed by the
    /// Decimen-style tracked path. ZXing.Net exposes finder result points but
    /// not the exact GridSampler perspective quad used by decimen-codec, so
    /// the managed path retains a conservative QR region around those points.
    /// </summary>
    public QrPositionedDecodeResult? DecodeWithPosition(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        QrPixelFormat pixelFormat)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var expectedBytesPerPixel = pixelFormat switch
        {
            QrPixelFormat.Gray8 => 1,
            QrPixelFormat.Rgb24 => 3,
            QrPixelFormat.Bgra32 => 4,
            QrPixelFormat.Rgba32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unsupported pixel format.")
        };

        var expectedLength = checked(width * height * expectedBytesPerPixel);
        if (pixels.Length < expectedLength)
            throw new ArgumentException("Pixel buffer is shorter than the declared image dimensions.", nameof(pixels));

        var rawPixels = pixels[..expectedLength].ToArray();
        var bitmapFormat = ToBitmapFormat(pixelFormat);
        var result = _reader.Decode(rawPixels, width, height, bitmapFormat)
            ?? _pureReader.Decode(rawPixels, width, height, bitmapFormat);
        if (result is null)
            return null;

        var rawBytes = result.RawBytes;
        if (result.ResultMetadata is not null &&
            result.ResultMetadata.TryGetValue(ResultMetadataType.BYTE_SEGMENTS, out var segments) &&
            segments is System.Collections.IEnumerable enumerable)
        {
            var bytes = new List<byte>();
            foreach (var segment in enumerable)
            {
                if (segment is byte[] segmentBytes)
                    bytes.AddRange(segmentBytes);
            }

            if (bytes.Count > 0)
                rawBytes = bytes.ToArray();
        }

        var decodeResult = new QrDecodeResult(
            result.Text,
            result.BarcodeFormat,
            rawBytes?.Select(static b => (int)b).ToArray());

        return new QrPositionedDecodeResult(
            decodeResult,
            BuildQuad(result.ResultPoints, width, height),
            EstimateModules(result));
    }

    private static QrQuad BuildQuad(ResultPoint[]? points, int width, int height)
    {
        if (points is null || points.Length == 0)
        {
            return new QrQuad(
                new QrPoint(0, 0),
                new QrPoint(width, 0),
                new QrPoint(width, height),
                new QrPoint(0, height));
        }

        var minX = points.Min(static p => (double)p.X);
        var maxX = points.Max(static p => (double)p.X);
        var minY = points.Min(static p => (double)p.Y);
        var maxY = points.Max(static p => (double)p.Y);

        var span = Math.Max(maxX - minX, maxY - minY);
        // ResultPoints describe the finder-pattern anchors, not the complete
        // symbol. A generous margin is therefore required for a cropped
        // public-ZXing re-detection to retain the whole QR and quiet zone.
        var pad = Math.Max(12.0, span * 0.25);
        minX = Math.Max(0.0, minX - pad);
        minY = Math.Max(0.0, minY - pad);
        maxX = Math.Min((double)width, maxX + pad);
        maxY = Math.Min((double)height, maxY + pad);

        return new QrQuad(
            new QrPoint(minX, minY),
            new QrPoint(maxX, minY),
            new QrPoint(maxX, maxY),
            new QrPoint(minX, maxY));
    }

    private static int EstimateModules(Result result)
    {
        // ZXing.Net does not expose the sampled module matrix/version through
        // Result. Keep zero as "unknown" rather than inventing a dimension.
        return 0;
    }

    private static RGBLuminanceSource.BitmapFormat ToBitmapFormat(QrPixelFormat pixelFormat) => pixelFormat switch
    {
        QrPixelFormat.Gray8 => RGBLuminanceSource.BitmapFormat.Gray8,
        QrPixelFormat.Rgb24 => RGBLuminanceSource.BitmapFormat.RGB24,
        QrPixelFormat.Bgra32 => RGBLuminanceSource.BitmapFormat.BGRA32,
        QrPixelFormat.Rgba32 => RGBLuminanceSource.BitmapFormat.RGBA32,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unsupported pixel format.")
    };
}

public enum QrPixelFormat
{
    Gray8,
    Rgb24,
    Bgra32,
    Rgba32
}
