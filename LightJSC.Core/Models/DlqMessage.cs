namespace LightJSC.Core.Models;

public sealed class DlqMessage
{
    public Guid Id { get; set; }
    public Guid SubscriberId { get; set; }
    public string EndpointUrl { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

