using OptiCopy.Core.Protocol;
using OptiCopy.Imaging.Qr;
using Xunit;

namespace OptiCopy.Tests;

public sealed class QrCodeTests
{
    [Fact]
    public void GenerateAndDecodeRoundTripsUtf8Text()
    {
        const string payload = "DOT1|42|7|16|1024|0123456789abcdef|application/octet-stream|sample.bin|SGVsbG8=";

        var matrix = QrCodeGenerator.Generate(payload, new QrCodeOptions(256, 256, 4, QrErrorCorrection.High));
        var pixels = QrMatrixRasterizer.ToGray8(matrix);
        var decoder = new QrCodeDecoder();

        var decoded = decoder.Decode(pixels, matrix.Width, matrix.Height, QrPixelFormat.Gray8);

        Assert.NotNull(decoded);
        Assert.Equal(payload, decoded!.Text);
        Assert.Equal(ZXing.BarcodeFormat.QR_CODE, decoded.Format);
    }

    [Fact]
    public void GenerateBinaryRoundTripsExactDecimenFrameBytes()
    {
        var payload = Enumerable.Range(0, 360).Select(static i => (byte)i).ToArray();
        var frame = new Frame(
            FrameCodec.WireVersion,
            0,
            0x1234,
            7,
            1,
            (ushort)payload.Length,
            (uint)payload.Length,
            Fnv1a.Hash(payload),
            payload);
        var wire = FrameCodec.Encode(frame);

        var matrix = QrCodeGenerator.GenerateBinary(
            wire,
            new QrCodeOptions(560, 560, 4, QrErrorCorrection.Low, true, "ISO-8859-1"));
        var pixels = QrMatrixRasterizer.ToGray8(matrix);
        var decoded = new QrCodeDecoder().Decode(pixels, matrix.Width, matrix.Height, QrPixelFormat.Gray8);

        Assert.NotNull(decoded);
        Assert.Equal(ZXing.BarcodeFormat.QR_CODE, decoded!.Format);
        Assert.Equal(wire, decoded.RawBytes!.Select(static b => (byte)b).ToArray());
    }

    [Fact]
    public void GenerateProducesSquareMatrix()
    {
        var matrix = QrCodeGenerator.Generate("OptiCopy", new QrCodeOptions(300, 300));

        Assert.Equal(matrix.Width, matrix.Height);
        Assert.True(matrix.Width > 0);
        Assert.NotEmpty(matrix.ToArray());
    }

    [Fact]
    public void RasterizerPreservesModuleValues()
    {
        var matrix = QrCodeGenerator.Generate("OptiCopy", new QrCodeOptions(64, 64));
        var pixels = QrMatrixRasterizer.ToGray8(matrix, 2);

        Assert.Equal(matrix.Width * 2 * matrix.Height * 2, pixels.Length);
        Assert.Contains((byte)0, pixels);
        Assert.Contains((byte)255, pixels);
    }
}
