using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OptiCopy.Core.Transfer;
using OptiCopy.Imaging.Qr;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Runtime.InteropServices.WindowsRuntime;

namespace OptiCopy.Windows.Pages;

public sealed partial class SenderPage : Page
{
    private readonly QrCodeGenerator _qrGenerator = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _clock = new();
    private OpticalTransferSession? _session;
    private uint _cycleFrames;
    private WriteableBitmap? _bitmap;

    public SenderPage()
    {
        InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(125);
        _timer.Tick += OnTimerTick;
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            ChooseFileButton.IsEnabled = false;
            StatusLabel.Text = "READING";
            var bytes = await File.ReadAllBytesAsync(file.Path);
            _session = OpticalTransferSession.Create(
                bytes,
                file.Name,
                GuessMimeType(file.Name),
                CreateSessionId(),
                blockLength: 768,
                repairFramesPerBlock: 3);
            _cycleFrames = checked((uint)_session.Metadata.SourceBlocks * 2u);

            FileNameLabel.Text = _session.Metadata.FileName;
            FileSizeLabel.Text = $"{FormatBytes(_session.Metadata.OriginalLength)} • {_session.Metadata.MimeType}";
            HashLabel.Text = $"SHA-256: {_session.Metadata.Sha256}";
            BlocksLabel.Text = _session.Metadata.SourceBlocks.ToString();
            BlockLengthLabel.Text = _session.Metadata.BlockLength.ToString();
            CycleLabel.Text = _cycleFrames.ToString();
            ProgressBar.Value = 0;
            ProgressLabel.Text = "0%";
            SpeedLabel.Text = "— fps";
            FrameLabel.Text = "READY";
            StreamLabel.Text = "File loaded. Start the optical stream.";
            EngineStatus.Text = "READY";
            EngineStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["OptiEmeraldBrush"];
            StatusLabel.Text = "READY";

            _bitmap = new WriteableBitmap(560, 560);
            QrImage.Source = _bitmap;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "ERROR";
            StreamLabel.Text = ex.Message;
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
        _timer.Start();
        IsRunning = true;
        IsPaused = false;
        UpdateButtons();
        RenderFrame();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (!IsRunning) return;
        _timer.Stop();
        _clock.Stop();
        IsRunning = false;
        IsPaused = true;
        EngineStatus.Text = "PAUSED";
        StatusLabel.Text = "PAUSED";
        UpdateButtons();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _clock.Stop();
        IsRunning = false;
        IsPaused = false;
        _session?.Reset();
        ProgressBar.Value = 0;
        ProgressLabel.Text = "0%";
        FrameLabel.Text = "READY";
        EngineStatus.Text = _session is null ? "IDLE" : "READY";
        StatusLabel.Text = _session is null ? "READY" : "READY";
        UpdateButtons();
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!IsRunning || _session is null) return;
        RenderFrame();

        if (_session.FramesEmitted >= _cycleFrames)
        {
            _timer.Stop();
            _clock.Stop();
            IsRunning = false;
            IsPaused = false;
            ProgressBar.Value = 1;
            ProgressLabel.Text = "100%";
            EngineStatus.Text = "CYCLE COMPLETE";
            StatusLabel.Text = "COMPLETE — repeat the cycle for difficult optical conditions.";
            UpdateButtons();
        }
    }

    private void RenderFrame()
    {
        if (_session is null || _bitmap is null) return;

        var transferFrame = _session.NextFrame();
        var matrix = _qrGenerator.Generate(
            transferFrame.PayloadBase64,
            new QrCodeOptions(Width: 560, Height: 560, QuietZone: 8, ErrorCorrection: QrErrorCorrection.Medium, DisableEci: true));

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

        using var stream = _bitmap.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(pixels, 0, pixels.Length);
        _bitmap.Invalidate();

        FrameLabel.Text = $"FRAME {transferFrame.Sequence:N0}";
        StreamLabel.Text = $"{_session.FramesEmitted:N0} / {_cycleFrames:N0} frames • session {_session.Metadata.SessionId:X4}";
        var progress = _cycleFrames == 0 ? 0 : Math.Min(1d, (double)_session.FramesEmitted / _cycleFrames);
        ProgressBar.Value = progress;
        ProgressLabel.Text = $"{progress:P0}";
        var seconds = _clock.Elapsed.TotalSeconds;
        SpeedLabel.Text = seconds <= 0 ? "— fps" : $"{_session.FramesEmitted / seconds:0.0} fps";
        EngineStatus.Text = "TRANSMITTING";
        StatusLabel.Text = "TRANSMITTING";
    }

    private bool IsRunning { get; set; }
    private bool IsPaused { get; set; }

    private void UpdateButtons()
    {
        StartButton.IsEnabled = _session is not null && !IsRunning;
        PauseButton.IsEnabled = IsRunning;
        StopButton.IsEnabled = _session is not null && (IsRunning || IsPaused);
        if (IsPaused)
        {
            StartButton.Content = "Resume transmission";
            StartButton.Click -= Start_Click;
            StartButton.Click += Resume_Click;
        }
        else
        {
            StartButton.Content = "Start transmission";
            StartButton.Click -= Resume_Click;
            StartButton.Click -= Start_Click;
            StartButton.Click += Start_Click;
        }
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        IsRunning = true;
        IsPaused = false;
        _clock.Start();
        _timer.Start();
        EngineStatus.Text = "TRANSMITTING";
        StatusLabel.Text = "TRANSMITTING";
        UpdateButtons();
    }

    private static ushort CreateSessionId()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var value = BitConverter.ToUInt16(bytes, 0);
        return value == 0 ? (ushort)1 : value;
    }

    private static string GuessMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".txt" => "text/plain",
        ".json" => "application/json",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".zip" => "application/zip",
        ".7z" => "application/x-7z-compressed",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream"
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
