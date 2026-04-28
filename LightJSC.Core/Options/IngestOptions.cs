namespace LightJSC.Core.Options;

public sealed class IngestOptions
{
    public int ChannelCapacity { get; set; } = 2048;
    public bool BestShotOnly { get; set; } = true;
    public int DedupWindowMs { get; set; } = 1500;
    public int MinFeatureDimension { get; set; } = 0;
    public int MaxEventTimeSkewMinutes { get; set; } = 10;
    public bool LogRawMetadata { get; set; } = false;
    public int MaxRawMetadataChars { get; set; } = 4000;
    public bool LogParsedEvents { get; set; } = false;
    public string MetadataLogPath { get; set; } = "logs/face-metadata.log";
    public string RawMetadataLogPath { get; set; } = "logs/rtsp-raw-metadata.log";
}

