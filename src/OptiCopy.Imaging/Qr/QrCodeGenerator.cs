using System.Text;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace OptiCopy.Imaging.Qr;

public enum QrErrorCorrection
{
    Low,
    Medium,
    Quartile,
    High
}

public sealed record QrCodeOptions(
    int Width = 512,
    int Height = 512,
    int QuietZone = 4,
    QrErrorCorrection ErrorCorrection = QrErrorCorrection.Medium,
    bool DisableEci = true,
    string CharacterSet = "UTF-8");

public sealed class QrCodeGenerator
{
    public static QrMatrix Generate(string content, QrCodeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        options ??= new QrCodeOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Height);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZone);

        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = ToLevel(options.ErrorCorrection),
            [EncodeHintType.MARGIN] = options.QuietZone,
            [EncodeHintType.CHARACTER_SET] = options.CharacterSet
        };
        if (options.DisableEci)
            hints[EncodeHintType.DISABLE_ECI] = true;

        var writer = new QRCodeWriter();
        var matrix = writer.encode(content, BarcodeFormat.QR_CODE, options.Width, options.Height, hints);
        var modules = new bool[checked(matrix.Width * matrix.Height)];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
                modules[y * matrix.Width + x] = matrix[x, y];
        }

        return new QrMatrix(matrix.Width, matrix.Height, modules);
    }

    public static QrMatrix GenerateUtf8(ReadOnlySpan<byte> utf8Content, QrCodeOptions? options = null)
    {
        return Generate(Encoding.UTF8.GetString(utf8Content), options);
    }

    private static ErrorCorrectionLevel ToLevel(QrErrorCorrection correction) => correction switch
    {
        QrErrorCorrection.Low => ErrorCorrectionLevel.L,
        QrErrorCorrection.Medium => ErrorCorrectionLevel.M,
        QrErrorCorrection.Quartile => ErrorCorrectionLevel.Q,
        QrErrorCorrection.High => ErrorCorrectionLevel.H,
        _ => throw new ArgumentOutOfRangeException(nameof(correction))
    };
}
