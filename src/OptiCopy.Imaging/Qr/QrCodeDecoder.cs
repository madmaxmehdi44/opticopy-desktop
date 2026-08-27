using ZXing;
using ZXing.Common;

namespace OptiCopy.Imaging.Qr;

public sealed record QrDecodeResult(string Text, BarcodeFormat Format, int[]? RawBytes = null);

public sealed class QrCodeDecoder
{
    private readonly BarcodeReaderGeneric _reader;

    public QrCodeDecoder()
    {
        _reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = false,
                TryInverted = true,
                CharacterSet = "UTF-8"
            }
        };
    }

    public QrDecodeResult? Decode(ReadOnlySpan<byte> pixels, int width, int height, QrPixelFormat pixelFormat)
    {
        ArgumentException.ThrowIfNullOrEmpty(pixelFormat.ToString());
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

        var result = _reader.Decode(pixels[..expectedLength].ToArray(), width, height, ToBitmapFormat(pixelFormat));
        return result is null ? null : new QrDecodeResult(result.Text, result.BarcodeFormat, result.RawBytes?.Select(static b => (int)b).ToArray());
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
