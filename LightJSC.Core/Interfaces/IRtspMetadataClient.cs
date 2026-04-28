namespace LightJSC.Core.Interfaces;

public interface IRtspMetadataClient
{
    IAsyncEnumerable<string> StreamMetadataAsync(Uri rtspUri, CancellationToken cancellationToken);
    Task<bool> TestAsync(Uri rtspUri, TimeSpan timeout, CancellationToken cancellationToken);
}

