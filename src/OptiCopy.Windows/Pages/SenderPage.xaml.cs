using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OptiCopy.Core.Transfer;
using OptiCopy.Imaging.Qr;
using global::System.Runtime.InteropServices.WindowsRuntime;
using global::Windows.Storage.Pickers;
using WinRT.Interop;

namespace OptiCopy.Windows.Pages;

public sealed partial class SenderPage : Page
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private OpticalTransferSession? _session;
    private uint _framesTarget;
    private bool _renderInProgress;

    public SenderPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        _timer.Tick += Timer_Tick;
        StartButton.IsEnabled = false;
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.MainWindow is null)
                throw new InvalidOperationException("The OptiCopy window is not initialized.");

            ChooseFileButton.IsEnabled = false;
            StatusLabel.Text = "OPENING FILE PICKER";
            EngineStatus.Text = "WAITING";

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                ViewMode = global::Windows.Storage.Pickers.PickerViewMode.List
            };
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                EngineStatus.Text = "READY";
                StatusLabel.Text = "NO FILE SELECTED";
                return;
            }

            var payload = await File.ReadAllBytesAsync(file.Path);
            _session = await OpticalTransferSession.CreateAsync(
                payload,
                file.Name,
                "application/octet-stream",
                CreateSessionId());
            _framesTarget = Math.Max(_session.MinimumFrames, 1u);

            FileNameLabel.Text = file.Name;
            FileSizeLabel.Text = FormatBytes(payload.LongLength);
            HashLabel.Text = $"SHA-256: {_session.Metadata.Sha256}";
            BlocksLabel.Text = _session.Metadata.SourceBlocks.ToString(CultureInfo.InvariantCulture);
            BlockLengthLabel.Text = _session.Metadata.BlockLength.ToString(CultureInfo.InvariantCulture);
            CycleLabel.Text = _framesTarget.ToString(CultureInfo.InvariantCulture);
            FrameLabel.Text = "READY";
            StreamLabel.Text = "Decimen v3 binary stream ready. Start transmission.";
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
            StatusLabel.Text = $"FILE ERROR: {ex.Message}";
            StartButton.IsEnabled = _session is not null;
        }
        finally
        {
            ChooseFileButton.IsEnabled = true;
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _session.Reset();
        _clock.Restart();
        _renderInProgress = false;
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
        _renderInProgress = false;
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

    private void Timer_Tick(object? sender, object e)
    {
        if (_renderInProgress) return;
        RenderFrame();
    }

    private void RenderFrame()
    {
        if (_session is null || _renderInProgress) return;
        _renderInProgress = true;

        try
        {
            var transferFrame = _session.NextFrame();
            var wireBytes = transferFrame.Frame.Payload is null
                ? throw new InvalidOperationException("Transfer frame payload is missing.")
                : OptiCopy.Core.Protocol.FrameCodec.Encode(transferFrame.Frame);

            var matrix = new QrCodeGenerator().GenerateBinary(
                wireBytes,
                new QrCodeOptions(560, 560, 4, QrErrorCorrection.Low, true, "ISO-8859-1"));

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
            FrameLabel.Text = $"FRAME {transferFrame.Sequence.ToString(CultureInfo.InvariantCulture)}";
            StreamLabel.Text = $"DECIMEN V3 • {wireBytes.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes • {_session.FramesEmitted.ToString("N0", CultureInfo.InvariantCulture)} frames emitted";
            var progress = Math.Min(1d, (double)_session.FramesEmitted / _framesTarget);
            ProgressBar.Value = progress;
            ProgressLabel.Text = progress.ToString("P0", CultureInfo.InvariantCulture);

            var seconds = _clock.Elapsed.TotalSeconds;
            if (seconds > 0)
                SpeedLabel.Text = $"{(_session.FramesEmitted / seconds).ToString("0.0", CultureInfo.InvariantCulture)} fps";

            if (_session.FramesEmitted >= _framesTarget)
            {
                _timer.Stop();
                _clock.Stop();
                PauseButton.IsEnabled = false;
                StopButton.IsEnabled = false;
                StartButton.IsEnabled = true;
                PauseButton.Content = "Pause";
                EngineStatus.Text = "CYCLE COMPLETE";
                StatusLabel.Text = "CYCLE COMPLETE";
            }
        }
        catch (Exception ex)
        {
            _timer.Stop();
            _clock.Stop();
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            StartButton.IsEnabled = _session is not null;
            EngineStatus.Text = "ERROR";
            StatusLabel.Text = $"RENDER ERROR: {ex.Message}";
        }
        finally
        {
            _renderInProgress = false;
        }
    }

    private static WriteableBitmap CreateBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height);
        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        return bitmap;
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

        return $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
