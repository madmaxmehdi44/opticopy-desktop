using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using OptiCopy.Core.Transfer;
using OptiCopy.Imaging.Qr;
using OptiCopy.Windows.Diagnostics;
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
        AppLogger.Info("SenderPage initialized.");
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.Info("ChooseFile_Click started.");
            if (App.MainWindow is null)
                throw new InvalidOperationException("The OptiCopy window is not initialized.");

            ChooseFileButton.IsEnabled = false;
            StatusLabel.Text = "OPENING FILE PICKER";
            EngineStatus.Text = "WAITING";

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            AppLogger.Info($"Creating Windows App SDK FileOpenPicker. HWND=0x{hwnd.ToInt64():X}, WindowId={windowId.Value}.");

            var picker = new FileOpenPicker(windowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };

            AppLogger.Info("Calling FileOpenPicker.PickSingleFileAsync().");
            var file = await picker.PickSingleFileAsync();
            AppLogger.Info(file is null ? "File picker returned no file." : $"File selected. Path='{file.Path}'.");

            if (file is null)
            {
                EngineStatus.Text = "READY";
                StatusLabel.Text = "NO FILE SELECTED";
                return;
            }

            var payload = await System.IO.File.ReadAllBytesAsync(file.Path);
            AppLogger.Info($"File read succeeded. Bytes={payload.LongLength}.");

            _session = await OpticalTransferSession.CreateAsync(
                payload,
                System.IO.Path.GetFileName(file.Path),
                "application/octet-stream",
                CreateSessionId());
            AppLogger.Info($"Transfer session created. SourceBlocks={_session.Metadata.SourceBlocks}, BlockLength={_session.Metadata.BlockLength}, MinimumFrames={_session.MinimumFrames}.");
            _framesTarget = Math.Max(_session.MinimumFrames, 1u);

            FileNameLabel.Text = System.IO.Path.GetFileName(file.Path);
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
            AppLogger.Info("ChooseFile_Click completed successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("ChooseFile_Click failed.", ex);
            EngineStatus.Text = "ERROR";
            StatusLabel.Text = $"FILE ERROR: {ex.GetType().Name}: {ex.Message}";
            StartButton.IsEnabled = _session is not null;
            await ShowFileErrorAsync(ex);
        }
        finally
        {
            ChooseFileButton.IsEnabled = true;
        }
    }

    private static async Task ShowFileErrorAsync(Exception ex)
    {
        var root = App.MainWindow?.Content.XamlRoot;
        if (root is null)
            return;

        var dialog = new ContentDialog
        {
            Title = "File opening error",
            Content = $"{ex.GetType().Name}: {ex.Message}\n\nFull details:\n{AppLogger.LogFilePath}",
            CloseButtonText = "OK",
            XamlRoot = root
        };

        await dialog.ShowAsync();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        AppLogger.Info("Transmission start requested.");
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
            AppLogger.Info("Transmission paused.");
            _timer.Stop();
            _clock.Stop();
            PauseButton.Content = "Resume";
            EngineStatus.Text = "PAUSED";
            StatusLabel.Text = "PAUSED";
        }
        else
        {
            AppLogger.Info("Transmission resumed.");
            _clock.Start();
            _timer.Start();
            PauseButton.Content = "Pause";
            EngineStatus.Text = "TRANSMITTING";
            StatusLabel.Text = "TRANSMITTING";
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("Transmission stop requested.");
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
            var wireBytes = OptiCopy.Core.Protocol.FrameCodec.Encode(transferFrame.Frame);

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
            AppLogger.Error("RenderFrame failed.", ex);
            _timer.Stop();
            _clock.Stop();
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            StartButton.IsEnabled = _session is not null;
            EngineStatus.Text = "ERROR";
            StatusLabel.Text = $"RENDER ERROR: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            _renderInProgress = false;
        }
    }

    private static WriteableBitmap CreateBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height);
        using var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(bitmap.PixelBuffer);
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
