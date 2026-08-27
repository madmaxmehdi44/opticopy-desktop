namespace OptiCopy.Imaging.Qr;

public static class QrMatrixRasterizer
{
    public static byte[] ToGray8(QrMatrix matrix, int scale = 1)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var width = checked(matrix.Width * scale);
        var height = checked(matrix.Height * scale);
        var pixels = new byte[checked(width * height)];

        for (var moduleY = 0; moduleY < matrix.Height; moduleY++)
        {
            for (var moduleX = 0; moduleX < matrix.Width; moduleX++)
            {
                var value = matrix[moduleX, moduleY] ? (byte)0 : (byte)255;
                for (var y = 0; y < scale; y++)
                {
                    var row = (moduleY * scale + y) * width + moduleX * scale;
                    pixels.AsSpan(row, scale).Fill(value);
                }
            }
        }

        return pixels;
    }
}
