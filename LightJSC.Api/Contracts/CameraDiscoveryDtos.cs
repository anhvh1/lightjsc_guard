namespace LightJSC.Api.Contracts;

/// <summary>
/// Camera discovery response payload.
/// </summary>
public sealed class DiscoveredCameraResponse
{
    /// <summary>ONVIF endpoint reference identifier.</summary>
    public string? DeviceId { get; set; }
    /// <summary>Camera IP address.</summary>
    public string? IpAddress { get; set; }
    /// <summary>Camera name from ONVIF scopes.</summary>
    public string? Name { get; set; }
    /// <summary>Camera model from ONVIF scopes.</summary>
    public string? Model { get; set; }
    /// <summary>Camera series/line derived from the model (e.g. X, S).</summary>
    public string? CameraSeries { get; set; }
    /// <summary>MAC address from ONVIF scopes.</summary>
    public string? MacAddress { get; set; }
    /// <summary>ONVIF device service URL.</summary>
    public string? XAddr { get; set; }
    /// <summary>Raw ONVIF scopes string.</summary>
    public string? Scopes { get; set; }
}
