namespace OptiCopy.Imaging.Qr;

/// <summary>
/// Immutable QR matrix represented as modules. True means a dark module.
/// </summary>
public sealed class QrMatrix
{
    private readonly bool[] _modules;

    public QrMatrix(int width, int height, bool[] modules)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Length != checked(width * height))
            throw new ArgumentException("Module buffer length does not match matrix dimensions.", nameof(modules));

        Width = width;
        Height = height;
        _modules = modules;
    }

    public int Width { get; }
    public int Height { get; }

    public bool this[int x, int y] => _modules[checked(y * Width + x)];

    public bool[] ToArray() => (bool[])_modules.Clone();
}
