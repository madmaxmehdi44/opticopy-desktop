using OptiCopy.Imaging.Qr;
using Xunit;

namespace OptiCopy.Tests;

public sealed class QrCodeTests
{
    [Fact]
    public void GenerateAndDecodeRoundTripsUtf8Text()
    {
        const string payload = "DOT1|42|7|16|1024|0123456789abcdef|application/octet-stream|sample.bin|SGVsbG8=";

        var generator = new QrCodeGenerator();
        var matrix = generator.Generate(payload, new QrCodeOptions(256, 256, 4, QrErrorCorrection.High));
        var pixels = QrMatrixRasterizer.ToGray8(matrix);
        var decoder = new QrCodeDecoder();

        var decoded = decoder.Decode(pixels, matrix.Width, matrix.Height, QrPixelFormat.Gray8);

        Assert.NotNull(decoded);
        Assert.Equal(payload, decoded!.Text);
        Assert.Equal(ZXing.BarcodeFormat.QR_CODE, decoded.Format);
    }

    [Fact]
    public void GenerateProducesSquareMatrix()
    {
        var matrix = new QrCodeGenerator().Generate("OptiCopy", new QrCodeOptions(300, 300));

        Assert.Equal(matrix.Width, matrix.Height);
        Assert.True(matrix.Width > 0);
        Assert.NotEmpty(matrix.ToArray());
    }

    [Fact]
    public void RasterizerPreservesModuleValues()
    {
        var matrix = new QrCodeGenerator().Generate("OptiCopy", new QrCodeOptions(64, 64));
        var pixels = QrMatrixRasterizer.ToGray8(matrix, 2);

        Assert.Equal(matrix.Width * 2 * matrix.Height * 2, pixels.Length);
        Assert.Contains((byte)0, pixels);
        Assert.Contains((byte)255, pixels);
    }
}
