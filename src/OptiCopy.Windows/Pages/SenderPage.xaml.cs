using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using OptiCopy.Core.Transfer;
using OptiCopy.Imaging.Qr;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OptiCopy.Windows.Pages;

public sealed partial class SenderPage : Page
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private OpticalTransferSession? _session;
    private uint _framesTarget;

    public SenderPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        _timer.Tick += Timer_Tick;
        StartButton.IsEnabled = false;
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var payload = buffer.ToArray();
            _session = OpticalTransferSession.Create(payload, file.Name, "application/octet-stream", CreateSessionId());
            _framesTarget = Math.Max(_session.MinimumFrames, 1u);

            FileNameLabel.Text = file.Name;
            FileSizeLabel.Text = FormatBytes(payload.LongLength);
            HashLabel.Text = $"SHA-256: {_session.Metadata.Sha256}";
            BlocksLabel.Text = _session.Metadata.SourceBlocks.ToString();
            BlockLengthLabel.Text = _session.Metadata.BlockLength.ToString();
            CycleLabel.Text = _framesTarget.ToString();
            FrameLabel.Text = "READY";
            StreamLabel.Text = "File loaded. Start transmission.";
            EngineStatus.Text = "READY";
            StatusLabel.Text = "READY";
            ProgressBar.Value = 0;
            ProgressLabel.Text = "0%";
            SpeedLabel.Text = "— fps";
            StartButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            EngineStatus.Text = "ERROR";
            StatusLabel.Text = ex.Message;
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _session.Reset();
        _clock.Restart();
        RenderFrame();
        _timer.Start();
        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        EngineStatus.Text = "TRANSMITTING";
        StatusLabel.Text = "TRANSMITTING";
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _clock.Stop();
            PauseButton.Content = "Resume";
            EngineStatus.Text = "PAUSED";
            StatusLabel.Text = "PAUSED";
        }
        else
        {
            _clock.Start();
            _timer.Start();
            PauseButton.Content = "Pause";
            EngineStatus.Text = "TRANSMITTING";
            StatusLabel.Text = "TRANSMITTING";
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _clock.Stop();
        _session?.Reset();
        ProgressBar.Value = 0;
        ProgressLabel.Text = "0%";
        SpeedLabel.Text = "— fps";
        FrameLabel.Text = "READY";
        EngineStatus.Text = "READY";
        StatusLabel.Text = "STOPPED";
        PauseButton.Content = "Pause";
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        StartButton.IsEnabled = _session is not null;
    }

    private void Timer_Tick(object? sender, object e) => RenderFrame();

    private void RenderFrame()
    {
        if (_session is null) return;

        var transferFrame = _session.NextFrame();
        var matrix = QrCodeGenerator.Generate(
            transferFrame.PayloadBase64,
            new QrCodeOptions(560, 560, 8, QrErrorCorrection.Medium, true));

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

        QrImage.Source = CreateBitmap(pixels, matrix.Width, matrix.Height);
        FrameLabel.Text = $"FRAME {transferFrame.Sequence}";
        StreamLabel.Text = $"{_session.FramesEmitted:N0} frames emitted";
        var progress = Math.Min(1d, (double)_session.FramesEmitted / _framesTarget);
        ProgressBar.Value = progress;
        ProgressLabel.Text = $"{progress:P0}";

        var seconds = _clock.Elapsed.TotalSeconds;
        if (seconds > 0)
            SpeedLabel.Text = $"{_session.FramesEmitted / seconds:0.0} fps";

        if (_session.FramesEmitted >= _framesTarget)
        {
            _timer.Stop();
            _clock.Stop();
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            StartButton.IsEnabled = true;
            PauseButton.Content = "Pause";
            EngineStatus.Text = "COMPLETE";
            StatusLabel.Text = "CYCLE COMPLETE";
        }
    }

    private static BitmapImage CreateBitmap(byte[] pixels, int width, int height)
    {
        // QR-to-SoftwareBitmap bridge is isolated here for the Windows rendering phase.
        _ = pixels;
        _ = width;
        _ = height;
        return new BitmapImage();
    }

    private static ushort CreateSessionId()
    {
        var value = BitConverter.ToUInt16(Guid.NewGuid().ToByteArray(), 0);
        return value == 0 ? (ushort)1 : value;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
