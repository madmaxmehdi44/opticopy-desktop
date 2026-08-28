using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using OptiCopy.Core.Transfer;
using OptiCopy.Imaging.Qr;
using OptiCopy.Windows.Camera;
using OptiCopy.Windows.Diagnostics;
using WinRT.Interop;

namespace OptiCopy.Windows.Pages;

public sealed partial class ReceiverPage : Page
{
    private sealed record CameraOption(string Id, string Name);

    private readonly WindowsCameraSource _camera = new();
    private readonly QrCodeDecoder _qrDecoder = new();
    private readonly OpticalReceiveSession _receiver = new();
    private readonly Stopwatch _clock = new();
    private int _frameBusy;
    private bool _cameraRunning;

    public ReceiverPage()
    {
        InitializeComponent();
        Loaded += ReceiverPage_Loaded;
        Unloaded += ReceiverPage_Unloaded;
        _camera.FrameArrived += Camera_FrameArrived;
    }

    private async void ReceiverPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CameraStatus.Text = "Enumerating cameras…";
            var cameras = await _camera.GetCamerasAsync();
            CameraSelector.ItemsSource = cameras.Select(static c => new CameraOption(c.Id, c.Name)).ToArray();
            CameraSelector.DisplayMemberPath = nameof(CameraOption.Name);
            CameraSelector.SelectedValuePath = nameof(CameraOption.Id);

            if (CameraSelector.Items.Count > 0)
            {
                CameraSelector.SelectedIndex = 0;
                CameraStatus.Text = $"{CameraSelector.Items.Count} camera(s) available.";
            }
            else
            {
                CameraStatus.Text = "No camera was found.";
                StatusLabel.Text = "NO CAMERA";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Receiver camera enumeration failed.", ex);
            CameraStatus.Text = $"Camera error: {ex.Message}";
            StatusLabel.Text = "CAMERA ERROR";
        }
    }

    private async void CameraSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cameraRunning || CameraSelector.SelectedValue is not string cameraId)
            return;

        CameraStatus.Text = "Camera selected. Press Start camera.";
    }

    private async void StartCamera_Click(object sender, RoutedEventArgs e)
    {
        if (CameraSelector.SelectedValue is not string cameraId)
        {
            StatusLabel.Text = "SELECT A CAMERA";
            return;
        }

        try
        {
            _receiver.Reset();
            SaveButton.IsEnabled = false;
            FileNameLabel.Text = "Waiting for stream";
            FileInfoLabel.Text = "Point the camera at the sender's QR stream.";
            ProgressBar.Value = 0;
            ProgressLabel.Text = "0%";
            FrameLabel.Text = "0 frames";

            StartCameraButton.IsEnabled = false;
            await _camera.StartAsync(cameraId);
            _cameraRunning = true;
            _clock.Restart();
            StopCameraButton.IsEnabled = true;
            CameraOverlayLabel.Text = "CAMERA ON";
            CameraStatus.Text = "Capturing frames…";
            StatusLabel.Text = "SCANNING";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Receiver camera start failed.", ex);
            StartCameraButton.IsEnabled = true;
            StatusLabel.Text = $"CAMERA ERROR: {ex.Message}";
        }
    }

    private async void StopCamera_Click(object sender, RoutedEventArgs e)
    {
        await StopCameraAsync();
    }

    private async void ReceiverPage_Unloaded(object sender, RoutedEventArgs e)
    {
        await StopCameraAsync();
        _camera.FrameArrived -= Camera_FrameArrived;
        await _camera.DisposeAsync();
    }

    private void Camera_FrameArrived(object? sender, CameraFrame frame)
    {
        if (Interlocked.Exchange(ref _frameBusy, 1) != 0)
            return;

        try
        {
            var positioned = _qrDecoder.DecodeWithPosition(
                frame.Pixels,
                frame.Width,
                frame.Height,
                QrPixelFormat.Bgra32);

            if (positioned?.Result.RawBytes is { Length: > 0 } raw)
            {
                var wireBytes = raw.Select(static value => (byte)value).ToArray();
                var result = _receiver.AcceptFrame(wireBytes);
                var progress = _receiver.Progress;

                DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressBar.Value = progress.EstimatedProgress;
                    ProgressLabel.Text = progress.EstimatedProgress.ToString("P0", CultureInfo.InvariantCulture);
                    FrameLabel.Text = $"{progress.NewFrames:N0} new • {progress.DuplicateFrames:N0} duplicates";
                    FileInfoLabel.Text = $"Session {progress.SessionId} • {progress.SolvedBlocks}/{progress.SourceBlocks} blocks • {progress.TotalLength:N0} bytes";
                    StatusLabel.Text = result switch
                    {
                        ReceiveFrameResult.Started => "STREAM LOCKED",
                        ReceiveFrameResult.Accepted => "RECEIVING",
                        ReceiveFrameResult.Duplicate => "RECEIVING",
                        ReceiveFrameResult.Complete => "TRANSFER COMPLETE",
                        ReceiveFrameResult.HashMismatch => "HASH MISMATCH",
                        ReceiveFrameResult.InvalidContainer => "INVALID CONTAINER",
                        ReceiveFrameResult.InvalidPayload => "INVALID FRAME",
                        _ => result.ToString().ToUpperInvariant()
                    };

                    if (_receiver.CompletedFile is { } received)
                    {
                        FileNameLabel.Text = received.FileName;
                        FileInfoLabel.Text = $"{received.MimeType} • {received.Bytes.Length:N0} bytes • SHA-256 verified";
                        SaveButton.IsEnabled = true;
                    }
                });
            }

            if (!_cameraRunning)
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    CameraImage.Source = CreateBitmap(frame.Pixels, frame.Width, frame.Height);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Receiver camera preview update failed.", ex);
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Receiver frame processing failed.", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _frameBusy, 0);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var received = _receiver.CompletedFile;
        if (received is null || App.MainWindow is null)
            return;

        try
        {
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var picker = new FileSavePicker(windowId)
            {
                SuggestedFileName = received.FileName
            };

            var extension = System.IO.Path.GetExtension(received.FileName);
            if (!string.IsNullOrWhiteSpace(extension))
                picker.FileTypeChoices.Add(received.MimeType, new[] { extension });
            else
                picker.FileTypeChoices.Add(received.MimeType, new[] { ".bin" });

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            await System.IO.File.WriteAllBytesAsync(file.Path, received.Bytes);
            StatusLabel.Text = "FILE SAVED";
            AppLogger.Info($"Received file saved. Path='{file.Path}', Bytes={received.Bytes.LongLength}.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Saving received file failed.", ex);
            StatusLabel.Text = $"SAVE ERROR: {ex.Message}";
        }
    }

    private async Task StopCameraAsync()
    {
        if (!_cameraRunning)
            return;

        try
        {
            await _camera.StopAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Receiver camera stop failed.", ex);
        }
        finally
        {
            _cameraRunning = false;
            _clock.Stop();
            StartCameraButton.IsEnabled = CameraSelector.Items.Count > 0;
            StopCameraButton.IsEnabled = false;
            CameraOverlayLabel.Text = "CAMERA OFF";
            CameraStatus.Text = "Camera stopped.";
            if (_receiver.CompletedFile is null)
                StatusLabel.Text = "READY";
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
}
