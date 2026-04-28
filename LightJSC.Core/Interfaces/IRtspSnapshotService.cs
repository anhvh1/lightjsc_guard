namespace LightJSC.Core.Interfaces;

public interface IRtspSnapshotService
{
    Task<byte[]> CaptureJpegAsync(Uri rtspUri, CancellationToken cancellationToken);
}
