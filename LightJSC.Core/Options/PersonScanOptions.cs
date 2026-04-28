namespace LightJSC.Core.Options;

public sealed class PersonScanOptions
{
    public bool Enabled { get; set; } = true;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int SnapshotTimeoutSeconds { get; set; } = 12;
    public int SessionRetentionMinutes { get; set; } = 30;
}
