namespace OptiCopy.Imaging.Camera;

public interface ICameraSource : IAsyncDisposable
{
    IReadOnlyList<string> GetCameras();
    ValueTask StartAsync(string cameraId, CancellationToken cancellationToken = default);
    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
