namespace LightJSC.Api.Contracts;

/// <summary>
/// Camera create/update request payload.
/// </summary>
public sealed class CameraRequest
{
    /// <summary>Camera identifier (user-provided or from Active Guard).</summary>
    public string CameraId { get; set; } = string.Empty;
    /// <summary>Optional camera code.</summary>
    public string? Code { get; set; }
    /// <summary>Camera IP or host (optional :port).</summary>
    public string IpAddress { get; set; } = string.Empty;
    /// <summary>RTSP username.</summary>
    public string RtspUsername { get; set; } = string.Empty;
    /// <summary>RTSP password (plaintext for input only).</summary>
    public string? RtspPassword { get; set; }
    /// <summary>ONVIF MediaInput profile.</summary>
    public string RtspProfile { get; set; } = "def_profile1";
    /// <summary>RTSP path (default /ONVIF/MediaInput).</summary>
    public string RtspPath { get; set; } = "/ONVIF/MediaInput";
    /// <summary>Camera series/line (e.g. x, s, u).</summary>
    public string? CameraSeries { get; set; }
    /// <summary>Camera model name.</summary>
    public string? CameraModel { get; set; }
    /// <summary>Enable/disable ingestion.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Camera response payload (no plaintext password).
/// </summary>
public sealed class CameraResponse
{
    /// <summary>Camera identifier.</summary>
    public string CameraId { get; set; } = string.Empty;
    /// <summary>Optional camera code.</summary>
    public string? Code { get; set; }
    /// <summary>Camera IP or host.</summary>
    public string IpAddress { get; set; } = string.Empty;
    /// <summary>RTSP username.</summary>
    public string RtspUsername { get; set; } = string.Empty;
    /// <summary>ONVIF MediaInput profile.</summary>
    public string RtspProfile { get; set; } = string.Empty;
    /// <summary>RTSP path.</summary>
    public string RtspPath { get; set; } = string.Empty;
    /// <summary>Camera series/line (e.g. x, s, u).</summary>
    public string? CameraSeries { get; set; }
    /// <summary>Camera model name.</summary>
    public string? CameraModel { get; set; }
    /// <summary>Enable/disable ingestion.</summary>
    public bool Enabled { get; set; }
    /// <summary>Whether an encrypted password is stored.</summary>
    public bool HasPassword { get; set; }
    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Updated timestamp (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// RTSP connectivity test response.
/// </summary>
public sealed class TestRtspResponse
{
    /// <summary>True if RTSP metadata could be read.</summary>
    public bool Success { get; set; }
}

