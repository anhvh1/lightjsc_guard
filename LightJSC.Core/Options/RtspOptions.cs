namespace LightJSC.Core.Options;

public sealed class RtspOptions
{
    public int KeepAliveSeconds { get; set; } = 15;
    public int ConnectTimeoutSeconds { get; set; } = 10;
    public int ReceiveTimeoutSeconds { get; set; } = 30;
    public int ReconnectMinSeconds { get; set; } = 1;
    public int ReconnectMaxSeconds { get; set; } = 60;
    public int MaxMetadataFrameBytes { get; set; } = 1048576;
}

