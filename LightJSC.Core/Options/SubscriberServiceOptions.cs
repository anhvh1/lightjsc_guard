namespace LightJSC.Core.Options;

public sealed class SubscriberServiceOptions
{
    public bool Enabled { get; set; } = true;
    public string? HmacSecret { get; set; }
    public bool RequireSignature { get; set; } = false;
    public int MaxItems { get; set; } = 50;
    public int DedupMinutes { get; set; } = 5;
}
