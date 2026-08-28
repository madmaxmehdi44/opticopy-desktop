using OptiCopy.Imaging.Qr;
using Xunit;

namespace OptiCopy.Tests;

public sealed class QrTrackingTests
{
    [Fact]
    public void FirstDecodeAnchorsQuadAndSecondDecodeUsesTrackedPath()
    {
        const string payload = "D1C3|3|0|4660|7|1|360|12345678|tracked";

        var matrix = new QrCodeGenerator().Generate(
            payload,
            new QrCodeOptions(560, 560, 4, QrErrorCorrection.Low));
        var pixels = QrMatrixRasterizer.ToGray8(matrix);
        var decoder = new QrTrackedDecoder();

        var first = decoder.Decode(pixels, matrix.Width, matrix.Height, QrPixelFormat.Gray8);

        Assert.NotNull(first);
        Assert.False(first!.Tracked);
        Assert.Equal(payload, first.Result.Text);
        Assert.True(first.Quad.IsUsable);

        var second = decoder.Decode(
            pixels,
            matrix.Width,
            matrix.Height,
            QrPixelFormat.Gray8,
            first.Quad);

        Assert.NotNull(second);
        Assert.True(second!.Tracked);
        Assert.Equal(payload, second.Result.Text);
    }
}
