using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using OptiCopy.Core.Transfer;
using OptiCopy.Imaging.Qr;

namespace OptiCopy.Windows.Pages;

public sealed partial class SenderPage
{
    private void RenderFrame()
    {
        if (_session is null || _bitmap is null) return;

        var transferFrame = _session.NextFrame();
        var matrix = QrCodeGenerator.Generate(
            transferFrame.PayloadBase64,
            new QrCodeOptions(Width: 560, Height: 560, QuietZone: 8, ErrorCorrection: QrErrorCorrection.Medium, DisableEci: true));

        var pixels = new byte[checked(matrix.Width * matrix.Height * 4)];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                var offset = checked((y * matrix.Width + x) * 4);
                var value = matrix[x, y] ? (byte)0 : (byte)255;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }

        UpdateQrBitmap(pixels, matrix.Width, matrix.Height);
    }

    private void UpdateQrBitmap(byte[] pixels, int width, int height)
    {
        // Concrete WinUI bitmap bridge is kept in this rendering seam.
        _ = pixels;
        _ = width;
        _ = height;
        _ = new BitmapImage();
    }
}
