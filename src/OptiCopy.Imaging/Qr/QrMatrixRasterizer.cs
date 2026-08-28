namespace OptiCopy.Imaging.Qr;

public static class QrMatrixRasterizer
{
    public static byte[] ToGray8(QrMatrix matrix, int scale = 1, int quietZone = 0)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);

        var moduleWidth = checked(matrix.Width + quietZone * 2);
        var moduleHeight = checked(matrix.Height + quietZone * 2);
        var width = checked(moduleWidth * scale);
        var height = checked(moduleHeight * scale);
        var pixels = new byte[checked(width * height)];
        pixels.AsSpan().Fill(255);

        for (var moduleY = 0; moduleY < matrix.Height; moduleY++)
        {
            for (var moduleX = 0; moduleX < matrix.Width; moduleX++)
            {
                if (!matrix[moduleX, moduleY])
                    continue;

                var startX = checked((moduleX + quietZone) * scale);
                var startY = checked((moduleY + quietZone) * scale);

                for (var y = 0; y < scale; y++)
                {
                    var row = checked((startY + y) * width + startX);
                    pixels.AsSpan(row, scale).Clear();
                }
            }
        }

        return pixels;
    }
}
