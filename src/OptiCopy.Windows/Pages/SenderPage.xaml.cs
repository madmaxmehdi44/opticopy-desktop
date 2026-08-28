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
    private const int MaxQrDisplayPixels = 560;
    private const double TargetFps = 24.0;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private OpticalTransferSession? _session;
    private bool _renderInProgress;

    public SenderPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFps) };
        _timer.Tick += Timer_Tick;
        StartButton.IsEnabled = false;
        AppLogger.Info($"SenderPage initialized. TargetFPS={TargetFps:0}.");
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

            if (string.IsNullOrWhiteSpace(file.Path))
                throw new IOException("The selected file did not provide a usable local path.");

            var payload = await System.IO.File.ReadAllBytesAsync(file.Path);
            AppLogger.Info($"File read succeeded. Bytes={payload.LongLength}.");

            var fileName = System.IO.Path.GetFileName(file.Path);
            var mimeType = GuessMimeType(fileName);
            _session = await OpticalTransferSession.CreateAsync(
                payload,
                fileName,
                mimeType,
                CreateSessionId());

            AppLogger.Info($"Transfer session created. SourceBlocks={_session.Metadata.SourceBlocks}, BlockLength={_session.Metadata.BlockLength}, CycleLength={_session.CycleLength}, FrameBytes={OpticalTransferSession.DefaultFrameBytes}, MimeType={mimeType}.");

            FileNameLabel.Text = fileName;
            FileSizeLabel.Text = FormatBytes(payload.LongLength);
            HashLabel.Text = $"SHA-256: {_session.Metadata.Sha256}";
            BlocksLabel.Text = _session.Metadata.SourceBlocks.ToString(CultureInfo.InvariantCulture);
            BlockLengthLabel.Text = _session.Metadata.BlockLength.ToString(CultureInfo.InvariantCulture);
            CycleLabel.Text = _session.CycleLength.ToString(CultureInfo.InvariantCulture);
            FrameLabel.Text = "READY";
            StreamLabel.Text = $"Decimen v3 • {OpticalTransferSession.DefaultFrameBytes:N0}-byte wire frames • {TargetFps:0} fps target";
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

        AppLogger.Info("Transmission start requested. Creating a fresh Decimen session id.");
        _session = _session.Restart(CreateSessionId());
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
        else if (_session is not null)
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

            // Match the Decimen sender's QR path: native module grid first,
            // then nearest-neighbour integer scaling. All wire frames have the
            // same byte length, so the QR geometry remains stable throughout
            // the carousel.
            var nativeMatrix = new QrCodeGenerator().GenerateNativeBinary(
                wireBytes,
                new QrCodeOptions(
                    Width: 0,
                    Height: 0,
                    QuietZone: 4,
                    ErrorCorrection: QrErrorCorrection.Low,
                    DisableEci: true,
                    CharacterSet: "ISO-8859-1",
                    QrMaskPattern: 4));

            var scale = Math.Max(1, MaxQrDisplayPixels / nativeMatrix.Width);
            var displaySize = checked(nativeMatrix.Width * scale);
            var pixels = QrMatrixRasterizer.ToGray8(nativeMatrix, scale);

            QrImage.Width = displaySize;
            QrImage.Height = displaySize;
            QrImage.Source = CreateBitmap(pixels, displaySize, displaySize);

            var cycleLength = Math.Max(1u, _session.CycleLength);
            var sequenceInCycle = transferFrame.Sequence % cycleLength;
            var cycleNumber = transferFrame.Sequence / cycleLength + 1;
            var progress = (double)(sequenceInCycle + 1) / cycleLength;

            AppLogger.Info($"Rendered frame {transferFrame.Sequence}. Cycle={cycleNumber}, InCycle={sequenceInCycle + 1}/{cycleLength}, WireBytes={wireBytes.Length}, QRModules={nativeMatrix.Width}, Scale={scale}, Raster={displaySize}x{displaySize}.");
            FrameLabel.Text = $"FRAME {transferFrame.Sequence.ToString(CultureInfo.InvariantCulture)}";
            StreamLabel.Text = $"DECIMEN V3 • cycle {cycleNumber.ToString(CultureInfo.InvariantCulture)} • {wireBytes.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes • {TargetFps:0} fps target";
            ProgressBar.Value = progress;
            ProgressLabel.Text = progress.ToString("P0", CultureInfo.InvariantCulture);

            var seconds = _clock.Elapsed.TotalSeconds;
            if (seconds > 0)
                SpeedLabel.Text = $"{(_session.FramesEmitted / seconds).ToString("0.0", CultureInfo.InvariantCulture)} fps";
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

    private static string GuessMimeType(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".gz" or ".gzip" => "application/gzip",
            ".7z" => "application/x-7z-compressed",
            ".rar" => "application/vnd.rar",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            _ => "application/octet-stream"
        };
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
