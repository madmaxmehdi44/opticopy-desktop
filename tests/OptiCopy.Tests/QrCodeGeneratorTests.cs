using OptiCopy.Imaging.Qr;
using Xunit;

namespace OptiCopy.Tests;

public sealed class QrCodeGeneratorTests
{
    [Fact]
    public void BinaryQrRoundTripsThroughDecoder()
    {
        var payload = new byte[512];
        new Random(4242).NextBytes(payload);

        var generator = new QrCodeGenerator();
        var matrix = generator.GenerateNativeBinary(payload, new QrCodeOptions(
            ErrorCorrection: QrErrorCorrection.Low,
            QuietZone: 0,
            DisableEci: true,
            CharacterSet: "ISO-8859-1",
            QrMaskPattern: 4));

        Assert.True(matrix.Width > 21);
        Assert.Equal(matrix.Width, matrix.Height);

        var pixels = QrMatrixRasterizer.ToGray8(matrix, 4);
        var decoder = new QrCodeDecoder(tryHarder: true);
        var decoded = decoder.Decode(
            pixels,
            matrix.Width * 4,
            matrix.Height * 4,
            QrPixelFormat.Gray8);

        Assert.NotNull(decoded);
        Assert.Equal(ZXing.BarcodeFormat.QR_CODE, decoded!.Format);
        Assert.NotNull(decoded.RawBytes);
        Assert.Equal(payload, decoded.RawBytes!.Select(static value => (byte)value).ToArray());
    }

    [Fact]
    public void FullDecimenFrameFitsQrVersion27InByteMode()
    {
        var frame = new byte[1465];
        new Random(1337).NextBytes(frame);

        var generator = new QrCodeGenerator();
        var matrix = generator.GenerateNativeBinary(frame, new QrCodeOptions(
            ErrorCorrection: QrErrorCorrection.Low,
            QuietZone: 0,
            DisableEci: true,
            CharacterSet: "ISO-8859-1",
            QrMaskPattern: 4));

        Assert.Equal(27, (matrix.Width - 17) / 4);
        Assert.Equal(125, matrix.Width);
    }
}
