namespace LightJSC.Core.Models;

public sealed class CameraCredential
{
    public string CameraId { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string RtspUsername { get; set; } = string.Empty;
    public string RtspPasswordEncrypted { get; set; } = string.Empty;
    public string RtspProfile { get; set; } = "def_profile1";
    public string RtspPath { get; set; } = "/ONVIF/MediaInput";
    public string? CameraSeries { get; set; }
    public string? CameraModel { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

