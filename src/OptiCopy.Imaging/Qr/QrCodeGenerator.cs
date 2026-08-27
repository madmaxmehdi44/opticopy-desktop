using System.Text;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

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
    string CharacterSet = "UTF-8",
    int? QrVersion = null,
    int QrMaskPattern = 4);

public sealed class QrCodeGenerator
{
    private readonly QRCodeWriter _writer = new();

    public QrMatrix Generate(string content, QrCodeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        options ??= new QrCodeOptions();
        ValidateOptions(options);

        var hints = CreateHints(options);
        var matrix = _writer.encode(content, BarcodeFormat.QR_CODE, options.Width, options.Height, hints);
        return ToMatrix(matrix);
    }

    public QrMatrix GenerateUtf8(ReadOnlySpan<byte> utf8Content, QrCodeOptions? options = null)
    {
        if (utf8Content.IsEmpty)
            throw new ArgumentException("QR payload cannot be empty.", nameof(utf8Content));

        return Generate(Encoding.UTF8.GetString(utf8Content), options);
    }

    public QrMatrix GenerateBinary(ReadOnlySpan<byte> content, QrCodeOptions? options = null)
    {
        if (content.IsEmpty)
            throw new ArgumentException("QR payload cannot be empty.", nameof(content));

        options ??= new QrCodeOptions(
            ErrorCorrection: QrErrorCorrection.Low,
            CharacterSet: "ISO-8859-1",
            DisableEci: true);

        // Decimen's sender uses QR byte mode, ISO-8859-1 semantics for arbitrary
        // bytes, a 4-module quiet zone, and pins mask pattern 4 for stable
        // geometry across the stream. Latin-1 is a one-to-one byte->char map.
        options = options with
        {
            CharacterSet = "ISO-8859-1",
            DisableEci = true
        };

        return Generate(Encoding.Latin1.GetString(content), options);
    }

    private static Dictionary<EncodeHintType, object> CreateHints(QrCodeOptions options)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = ToLevel(options.ErrorCorrection),
            [EncodeHintType.MARGIN] = options.QuietZone,
            [EncodeHintType.CHARACTER_SET] = options.CharacterSet,
            [EncodeHintType.QR_MASK_PATTERN] = options.QrMaskPattern
        };

        if (options.QrVersion is int version)
            hints[EncodeHintType.QR_VERSION] = version;

        if (options.DisableEci)
            hints[EncodeHintType.DISABLE_ECI] = true;

        return hints;
    }

    private static void ValidateOptions(QrCodeOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Height);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZone);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.QrMaskPattern, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.QrMaskPattern, 7);
        if (options.QrVersion is int version && (version < 1 || version > 40))
            throw new ArgumentOutOfRangeException(nameof(options), "QR version must be between 1 and 40.");
    }

    private static QrMatrix ToMatrix(ZXing.Common.BitMatrix matrix)
    {
        var modules = new bool[checked(matrix.Width * matrix.Height)];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
                modules[y * matrix.Width + x] = matrix[x, y];
        }

        return new QrMatrix(matrix.Width, matrix.Height, modules);
    }

    private static ZXing.QrCode.Internal.ErrorCorrectionLevel ToLevel(QrErrorCorrection correction) => correction switch
    {
        QrErrorCorrection.Low => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
        QrErrorCorrection.Medium => ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
        QrErrorCorrection.Quartile => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
        QrErrorCorrection.High => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
        _ => throw new ArgumentOutOfRangeException(nameof(correction))
    };
}
