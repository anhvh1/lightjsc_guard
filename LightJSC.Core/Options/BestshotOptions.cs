namespace LightJSC.Core.Options;

public sealed class BestshotOptions
{
    public bool Enabled { get; set; } = true;
    public string RootPath { get; set; } = "bestshots";
    public int RetentionDays { get; set; } = 90;
    public bool StoreKnown { get; set; } = true;
    public bool StoreUnknown { get; set; } = true;
    public bool StoreThumb { get; set; } = false;
    public int MaxBytes { get; set; } = 0;
    public int CleanupIntervalHours { get; set; } = 12;
}
