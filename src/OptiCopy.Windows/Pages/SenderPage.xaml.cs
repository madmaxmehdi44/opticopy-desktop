using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using OptiCopy.Core.Protocol;
using OptiCopy.Core.Transfer;
using OptiCopy.Data;
using OptiCopy.Imaging.Qr;
using OptiCopy.Windows.Diagnostics;
using WinRT.Interop;

namespace OptiCopy.Windows.Pages;

public sealed partial class SenderPage : Page
{
    private const int MaxQrDisplayPixels = 560;
    private const int QuietZoneModules = 4;
    private const double TargetFps = 12.0;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly QrCodeGenerator _qrGenerator = new();

    private OpticalTransferSession? _v3Session;
    private LegacyDot2TransferSession? _session;
    private WriteableBitmap? _qrBitmap;
    private int _qrBitmapWidth;
    private int _qrBitmapHeight;
    private int _qrVersion;
    private bool _renderInProgress;
    private Guid? _historyId;

    public SenderPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / TargetFps) };
        _timer.Tick += Timer_Tick;
        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        AppLogger.Info($"SenderPage initialized. LiveProtocol=DOT2, TargetFPS={TargetFps:0}, ChunkBytes={LegacyDot2TransferSession.DefaultChunkSize}.");
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
                _clock.Stop();
                await FinalizeHistoryAsync(TransferStatus.Cancelled, "Replaced by a new file selection.");
                _session?.Reset();
                _v3Session?.Reset();
            }

            if (App.MainWindow is null)
                throw new InvalidOperationException("The OptiCopy window is not initialized.");

            ChooseFileButton.IsEnabled = false;
            StartButton.IsEnabled = false;
            StatusLabel.Text = "OPENING FILE PICKER";
            EngineStatus.Text = "WAITING";

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var picker = new FileOpenPicker(windowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                EngineStatus.Text = "READY";
                StatusLabel.Text = "NO FILE SELECTED";
                StartButton.IsEnabled = _session is not null;
                return;
            }

            if (string.IsNullOrWhiteSpace(file.Path))
                throw new IOException("The selected file did not provide a usable local path.");

            var payload = await File.ReadAllBytesAsync(file.Path);
            if (payload.LongLength == 0)
                throw new InvalidDataException("The selected file is empty.");
            if (payload.LongLength > OpticalFileContainer.MaxFileBytes)
                throw new NotSupportedException($"The selected file is {FormatBytes(payload.LongLength)}, but the protocol limit is {FormatBytes(OpticalFileContainer.MaxFileBytes)}.");

            var fileName = Path.GetFileName(file.Path);
            var mimeType = GuessMimeType(fileName);
            var transferId = CreateSessionId();

            // Keep the canonical Decimen v3 session available in Core, but use
            // the Android-compatible DOT2 stream for the live Windows sender.
            _v3Session = await OpticalTransferSession.CreateAsync(
                payload,
                fileName,
                mimeType,
                transferId);
            _session = LegacyDot2TransferSession.Create(
                payload,
                fileName,
                mimeType,
                transferId,
                LegacyDot2TransferSession.DefaultChunkSize,
                LegacyDot2TransferSession.DefaultParityRatio);

            FileNameLabel.Text = fileName;
            FileSizeLabel.Text = FormatBytes(payload.LongLength);
            HashLabel.Text = $"SHA-256: {_session.Metadata.Sha256}";
            BlocksLabel.Text = _session.Metadata.DataChunks.ToString(CultureInfo.InvariantCulture);
            BlockLengthLabel.Text = _session.Metadata.ChunkSize.ToString(CultureInfo.InvariantCulture);
            CycleLabel.Text = _session.CycleLength.ToString(CultureInfo.InvariantCulture);
            FrameLabel.Text = "READY";
            StreamLabel.Text = $"DOT2 • {_session.Metadata.ChunkSize.ToString("N0", CultureInfo.InvariantCulture)} B/chunk • RS +{(_session.Metadata.ParityChunks / (double)_session.Metadata.DataChunks * 100.0):0}% • {TargetFps:0} fps";
            EngineStatus.Text = "READY";
            StatusLabel.Text = "READY";
            ProgressBar.Value = 0;
            ProgressLabel.Text = "0%";
            SpeedLabel.Text = "— fps";
            ResetQrBitmap();
            _qrVersion = 0;
            StartButton.IsEnabled = true;
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

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;

        try
        {
            var restartedId = CreateSessionId();
            _session = _session.Restart(restartedId);
            _v3Session = _v3Session?.Restart(restartedId);
            _session.Reset();
            _v3Session?.Reset();
            _clock.Restart();
            _renderInProgress = false;
            _qrVersion = 0;
            ResetQrBitmap();

            _historyId = Guid.NewGuid();
            await App.History.AddAsync(new TransferHistoryEntry(
                _historyId.Value,
                DateTimeOffset.UtcNow,
                null,
                TransferDirection.Send,
                TransferStatus.Started,
                _session.Metadata.FileName,
                _session.Metadata.MimeType,
                _session.Metadata.OriginalSize,
                _session.Metadata.OriginalSize,
                _session.Metadata.Sha256,
                _session.Metadata.TransferId,
                0,
                _session.Metadata.DataChunks,
                _session.Metadata.ChunkSize,
                null));

            RenderFrame();
            _timer.Start();
            StartButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            EngineStatus.Text = "TRANSMITTING";
            StatusLabel.Text = "TRANSMITTING";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Start_Click failed.", ex);
            _timer.Stop();
            _clock.Stop();
            await FinalizeHistoryAsync(TransferStatus.Failed, ex.Message);
            EngineStatus.Text = "ERROR";
            StatusLabel.Text = $"START ERROR: {ex.GetType().Name}: {ex.Message}";
            StartButton.IsEnabled = _session is not null;
        }
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
        else if (_session is not null)
        {
            _clock.Start();
            _timer.Start();
            PauseButton.Content = "Pause";
            EngineStatus.Text = "TRANSMITTING";
            StatusLabel.Text = "TRANSMITTING";
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _clock.Stop();
        await FinalizeHistoryAsync(TransferStatus.Cancelled, null);
        _session?.Reset();
        _v3Session?.Reset();
        _renderInProgress = false;
        _qrVersion = 0;
        ResetQrBitmap();
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
        if (!_renderInProgress)
            RenderFrame();
    }

    private void RenderFrame()
    {
        if (_session is null || _renderInProgress)
            return;

        _renderInProgress = true;
        try
        {
            var transferFrame = _session.NextFrame();
            var packet = transferFrame.Packet;

            if (!packet.StartsWith("DOT2|", StringComparison.Ordinal))
                throw new InvalidDataException("Live sender produced a non-DOT2 packet.");

            // DOT2 is the text protocol consumed by the current Android
            // PacketDecoder. It uses ISO-8859-1-compatible ASCII characters.
            var matrix = _qrGenerator.Generate(
                packet,
                new QrCodeOptions(
                    Width: 0,
                    Height: 0,
                    QuietZone: 0,
                    ErrorCorrection: QrErrorCorrection.Low,
                    DisableEci: true,
                    CharacterSet: "ISO-8859-1",
                    QrVersion: null,
                    QrMaskPattern: 4));

            if (matrix.Width < 21 || (matrix.Width - 17) % 4 != 0)
                throw new InvalidOperationException($"Invalid QR module dimension: {matrix.Width}.");

            _qrVersion = (matrix.Width - 17) / 4;
            if (_qrVersion is < 1 or > 40)
                throw new InvalidOperationException($"Invalid QR version: {_qrVersion}.");

            var totalModules = checked(matrix.Width + QuietZoneModules * 2);
            var scale = Math.Max(1, MaxQrDisplayPixels / totalModules);
            var displaySize = checked(totalModules * scale);
            var pixels = QrMatrixRasterizer.ToBgra32(matrix, scale, QuietZoneModules);
            SetQrBitmap(pixels, displaySize, displaySize);

            var cycleLength = Math.Max(1, _session.CycleLength);
            var sequenceInCycle = transferFrame.Sequence % cycleLength;
            var cycleNumber = transferFrame.Sequence / cycleLength + 1;
            var progress = (double)(sequenceInCycle + 1) / cycleLength;

            FrameLabel.Text = $"FRAME {transferFrame.Sequence.ToString(CultureInfo.InvariantCulture)}";
            StreamLabel.Text = $"DOT2 • QR V{_qrVersion} • ECC L • RS +{(_session.Metadata.ParityChunks / (double)_session.Metadata.DataChunks * 100.0):0}% • cycle {cycleNumber.ToString(CultureInfo.InvariantCulture)} • {packet.Length.ToString("N0", CultureInfo.InvariantCulture)} chars • {TargetFps:0} fps";
            ProgressBar.Value = progress;
            ProgressLabel.Text = progress.ToString("P0", CultureInfo.InvariantCulture);

            var seconds = _clock.Elapsed.TotalSeconds;
            if (seconds > 0)
                SpeedLabel.Text = $"{(_session.FramesEmitted / seconds).ToString("0.0", CultureInfo.InvariantCulture)} fps";

            AppLogger.Info($"Rendered DOT2 frame {transferFrame.Sequence}. Transfer={_session.Metadata.TransferId:X4}, Cycle={cycleNumber}, InCycle={sequenceInCycle + 1}/{cycleLength}, PacketChars={packet.Length}, QRVersion={_qrVersion}, Modules={matrix.Width}, QuietZone={QuietZoneModules}, Scale={scale}, Raster={displaySize}x{displaySize}, Parity={transferFrame.IsParity}.");
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
            _ = FinalizeHistoryAsync(TransferStatus.Failed, ex.Message);
        }
        finally
        {
            _renderInProgress = false;
        }
    }

    private void SetQrBitmap(byte[] pixels, int width, int height)
    {
        var expected = checked(width * height * 4);
        if (pixels.Length != expected)
            throw new InvalidDataException($"QR bitmap buffer length mismatch: expected {expected} bytes, got {pixels.Length}.");

        if (_qrBitmap is null || _qrBitmapWidth != width || _qrBitmapHeight != height)
        {
            _qrBitmap = new WriteableBitmap(width, height);
            _qrBitmapWidth = width;
            _qrBitmapHeight = height;
            QrImage.Width = width;
            QrImage.Height = height;
            QrImage.Source = _qrBitmap;
        }

        using var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(_qrBitmap.PixelBuffer);
        stream.Position = 0;
        stream.Write(pixels, 0, pixels.Length);
        _qrBitmap.Invalidate();
    }

    private void ResetQrBitmap()
    {
        _qrBitmap = null;
        _qrBitmapWidth = 0;
        _qrBitmapHeight = 0;
        QrImage.Source = null;
    }

    private async Task FinalizeHistoryAsync(TransferStatus status, string? error)
    {
        if (_historyId is not { } id || _session is null)
            return;

        try
        {
            await App.History.UpdateAsync(id, entry => entry with
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Status = status,
                Frames = _session.FramesEmitted,
                SessionId = _session.Metadata.TransferId,
                Error = error
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to finalize sender history.", ex);
        }
        finally
        {
            _historyId = null;
        }
    }

    private static ushort CreateSessionId()
    {
        return checked((ushort)RandomNumberGenerator.GetInt32(1, ushort.MaxValue + 1));
    }

    private static string GuessMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
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
