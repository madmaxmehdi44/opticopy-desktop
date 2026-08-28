using System.Diagnostics;
using System.Text;

namespace OptiCopy.Windows.Diagnostics;

internal static class AppLogger
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OptiCopy",
        "logs");

    private static string LogPath => Path.Combine(DirectoryPath, "opticopy.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O"));
            builder.Append(" [");
            builder.Append(level);
            builder.Append("] ");
            builder.Append(message);
            if (exception is not null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            var line = builder.ToString();
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }

            Debug.WriteLine(line);
        }
        catch
        {
            // Diagnostics must never break the application.
        }
    }
}
