namespace LightJSC.Subscriber.Options;

public sealed class SubscriberOptions
{
    public string? HmacSecret { get; set; }
    public bool RequireSignature { get; set; } = false;
    public int MaxItems { get; set; } = 50;
    public int DedupMinutes { get; set; } = 5;
}

