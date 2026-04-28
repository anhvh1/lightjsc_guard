namespace LightJSC.Core.Options;

public sealed class EnrollmentOptions
{
    public string CgiPath { get; set; } = "/cgi-bin/adam.cgi";
    public string AppName { get; set; } = "FaceBestshotApp";
    public int AppDataType { get; set; } = 3;
    public string FeatureVersion { get; set; } = "legacy";
    public string? AppDataTemplateBase64 { get; set; }
    public string? AppDataTemplateFile { get; set; }
    public int MaxJpegBytes { get; set; } = 200_000;
    public int MinResizeDimension { get; set; } = 64;
    public string ErrorLogPath { get; set; } = "logs/enroll-error.log";
    public string CgiTraceLogPath { get; set; } = "logs/enroll-cgi.log";
    public bool UseHttps { get; set; }
    public int HttpPort { get; set; } = 80;
    public int TimeoutSeconds { get; set; } = 30;
}

