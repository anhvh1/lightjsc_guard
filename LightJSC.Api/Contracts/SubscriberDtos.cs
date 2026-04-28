namespace LightJSC.Api.Contracts;

/// <summary>
/// Webhook subscriber create payload.
/// </summary>
public sealed class SubscriberRequest
{
    /// <summary>Subscriber display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Webhook endpoint URL.</summary>
    public string EndpointUrl { get; set; } = string.Empty;
    /// <summary>Enable/disable subscriber.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Webhook subscriber response.
/// </summary>
public sealed class SubscriberResponse
{
    /// <summary>Subscriber identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Subscriber display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Webhook endpoint URL.</summary>
    public string EndpointUrl { get; set; } = string.Empty;
    /// <summary>Enable/disable subscriber.</summary>
    public bool Enabled { get; set; }
    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

