namespace LightJSC.Core.Models;

public sealed class Subscriber
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

