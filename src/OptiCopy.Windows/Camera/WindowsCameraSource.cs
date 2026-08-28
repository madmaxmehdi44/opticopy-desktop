using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Storage.Streams;

namespace OptiCopy.Windows.Camera;

public sealed record CameraFrame(byte[] Pixels, int Width, int Height);

/// <summary>
/// Windows camera backend based on MediaFrameReader. It exposes CPU-readable
/// BGRA8 frames and deliberately contains no QR/protocol logic.
/// </summary>
public sealed class WindowsCameraSource : IAsyncDisposable
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public event EventHandler<CameraFrame>? FrameArrived;

    public static async Task<IReadOnlyList<(string Id, string Name)>> GetCamerasAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask(cancellationToken).ConfigureAwait(false);
        return devices.Select(static device => (device.Id, device.Name)).ToArray();
    }

    public async Task StartAsync(string cameraId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopCoreAsync().ConfigureAwait(false);

            _capture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = cameraId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };
            await _capture.InitializeAsync(settings).AsTask(cancellationToken).ConfigureAwait(false);

            var source = _capture.FrameSources.Values
                .Where(static item => item.Info.SourceKind == MediaFrameSourceKind.Color)
                .OrderByDescending(static item => item.CurrentFormat?.VideoFormat?.Width ?? 0)
                .FirstOrDefault();

            if (source is null)
                throw new InvalidOperationException("The selected camera has no color video frame source.");

            _reader = await _capture.CreateFrameReaderAsync(source, "ARGB32").AsTask(cancellationToken).ConfigureAwait(false);
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += Reader_FrameArrived;

            var status = await _reader.StartAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (status != MediaFrameReaderStartStatus.Success)
                throw new InvalidOperationException($"Camera frame reader could not start: {status}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Reader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null)
                return;

            using var bgra = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? bitmap
                : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var width = bgra.PixelWidth;
            var height = bgra.PixelHeight;
            var buffer = new global::Windows.Storage.Streams.Buffer(checked((uint)(width * height * 4)));
            bgra.CopyToBuffer(buffer);
            var pixels = global::System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
            FrameArrived?.Invoke(this, new CameraFrame(pixels, width, height));
        }
        catch
        {
            // Camera faults are surfaced by the consumer through the next
            // successful/failed lifecycle operation; frame callbacks must not
            // terminate the MediaFrameReader event source.
        }
    }

    private async Task StopCoreAsync()
    {
        if (_reader is not null)
        {
            _reader.FrameArrived -= Reader_FrameArrived;
            try
            {
                await _reader.StopAsync();
            }
            catch
            {
                // Teardown is best effort.
            }
            _reader.Dispose();
            _reader = null;
        }

        _capture?.Dispose();
        _capture = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}