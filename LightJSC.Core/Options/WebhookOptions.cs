namespace LightJSC.Core.Options;

public sealed class WebhookOptions
{
    public string HmacSecret { get; set; } = string.Empty;
    public bool SendKnown { get; set; }
    public int FaceCooldownSeconds { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
    public int MaxRetries { get; set; } = 5;
    public int CircuitBreakerFailures { get; set; } = 5;
    public int CircuitBreakerBreakSeconds { get; set; } = 60;
}

