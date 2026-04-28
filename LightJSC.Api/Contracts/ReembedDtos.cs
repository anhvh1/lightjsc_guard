namespace LightJSC.Api.Contracts;

public sealed class ReembedPersonsRequest
{
    public string CameraId { get; set; } = string.Empty;
    public IReadOnlyCollection<string>? CameraIds { get; set; }
    public string? TargetFeatureVersion { get; set; }
    public IReadOnlyDictionary<string, string>? FeatureVersionByCamera { get; set; }
    public IReadOnlyCollection<Guid>? PersonIds { get; set; }
    public int MaxPersons { get; set; } = 200;
    public bool IncludeInactive { get; set; }
    public bool DryRun { get; set; }
}

public sealed class ReembedEventsRequest
{
    public string CameraId { get; set; } = string.Empty;
    public IReadOnlyCollection<string>? CameraIds { get; set; }
    public string? TargetFeatureVersion { get; set; }
    public IReadOnlyDictionary<string, string>? FeatureVersionByCamera { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public int MaxEvents { get; set; } = 500;
    public bool DryRun { get; set; }
}

public sealed class ReembedResult
{
    public int Processed { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}
