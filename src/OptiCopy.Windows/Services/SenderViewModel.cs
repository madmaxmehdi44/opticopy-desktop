using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using OptiCopy.Core.Transfer;
using OptiCopy.Imaging.Qr;

namespace OptiCopy.Windows.Services;

public partial class SenderViewModel : ObservableObject
{
    private readonly Stopwatch _clock = new();
    private OpticalTransferSession? _session;
    private readonly QrCodeGenerator _qrGenerator = new();

    [ObservableProperty] private string _fileName = "No file selected";
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private long _fileSize;
    [ObservableProperty] private string _fileSizeText = "—";
    [ObservableProperty] private string _sha256 = string.Empty;
    [ObservableProperty] private int _sourceBlocks;
    [ObservableProperty] private int _blockLength;
    [ObservableProperty] private uint _currentSequence;
    [ObservableProperty] private uint _framesEmitted;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "Select a file to begin.";
    [ObservableProperty] private string _speedText = "—";
    [ObservableProperty] private BitmapImage? _qrImage;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPaused;

    public ObservableCollection<string> RecentStates { get; } = new();
    public bool HasFile => _session is not null;

    public async Task LoadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Selected file was not found.", path);
        if (info.Length > uint.MaxValue)
            throw new NotSupportedException("Files larger than 4 GiB are not supported by the current wire format.");

        Status = "Reading file…";
        var data = await File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var mime = ResolveMime(info.Extension);
        var sessionId = CreateSessionId();
        _session = OpticalTransferSession.Create(data, info.Name, mime, sessionId);

        FileName = info.Name;
        FilePath = info.FullName;
        FileSize = info.Length;
        FileSizeText = FormatBytes(info.Length);
        Sha256 = _session.Metadata.Sha256;
        SourceBlocks = _session.Metadata.SourceBlocks;
        BlockLength = _session.Metadata.BlockLength;
        CurrentSequence = 0;
        FramesEmitted = 0;
        Progress = 0;
        IsRunning = false;
        IsPaused = false;
        OnPropertyChanged(nameof(HasFile));
        Status = "READY — press Start to transmit.";
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (_session is null) return;
        IsRunning = true;
        IsPaused = false;
        _clock.Restart();
        Status = "TRANSMITTING";
        RenderNextFrame();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        IsPaused = true;
        IsRunning = false;
        _clock.Stop();
        Status = "PAUSED";
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Resume()
    {
        Start();
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        IsRunning = false;
        IsPaused = false;
        _clock.Stop();
        Status = "STOPPED";
    }

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private void Next()
    {
        if (_session is null) return;
        if (!IsRunning) IsRunning = true;
        RenderNextFrame();
    }

    private void RenderNextFrame()
    {
        if (_session is null) return;

        var transferFrame = _session.NextFrame();
        CurrentSequence = transferFrame.Sequence;
        FramesEmitted = _session.FramesEmitted;

        var completionWindow = Math.Max(_session.MinimumFrames, _session.Metadata.SourceBlocks * 2u);
        Progress = Math.Min(1.0, completionWindow == 0 ? 0 : (double)FramesEmitted / completionWindow);
        var elapsed = _clock.Elapsed.TotalSeconds;
        if (elapsed > 0)
            SpeedText = $"{FramesEmitted / elapsed:0.0} frames/s";

        var matrix = _qrGenerator.Generate(transferFrame.PayloadBase64);
        var pixels = QrMatrixRasterizer.ToGray8(matrix, 8);
        QrImage = BitmapFactory.CreateGray8(pixels, matrix.Width * 8, matrix.Height * 8);
        Status = $"FRAME {CurrentSequence} • {FramesEmitted} emitted";
    }

    private bool CanStart() => _session is not null && !IsRunning;
    private bool CanPause() => IsRunning;
    private bool CanStop() => _session is not null && (IsRunning || IsPaused);
    private bool CanAdvance() => _session is not null && IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPausedChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private static ushort CreateSessionId()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var value = BitConverter.ToUInt16(bytes, 0);
        return value == 0 ? (ushort)1 : value;
    }

    private static string ResolveMime(string extension) => extension.ToLowerInvariant() switch
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

internal static class BitmapFactory
{
    public static BitmapImage CreateGray8(byte[] pixels, int width, int height)
    {
        // Placeholder for the WinUI bitmap bridge; the raw raster remains owned by the imaging layer.
        // The actual SoftwareBitmap/BitmapImage bridge is wired in the Windows rendering phase.
        return new BitmapImage();
    }
}
