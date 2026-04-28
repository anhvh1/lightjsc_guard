namespace LightJSC.Subscriber.Models;

    public sealed class FaceWebhookPayload
    {
        public string CameraId { get; init; } = string.Empty;
        public string? CameraCode { get; init; }
        public string? CameraIp { get; init; }
        public DateTimeOffset EventTimeUtc { get; init; }
    public string? FeatureBase64 { get; init; }
    public float L2Norm { get; init; }
    public string FeatureVersion { get; init; } = string.Empty;
    public int? Age { get; init; }
    public string? Gender { get; init; }
    public string? Mask { get; init; }
    public string? FaceImageBase64 { get; init; }
    public string? BsFrame { get; init; }
    public string? ThumbFrame { get; init; }
    public float? Score { get; init; }
    public BoundingBox? BBox { get; init; }
    public bool IsKnown { get; init; }
    public float? Similarity { get; init; }
    public string? WatchlistEntryId { get; init; }
    public string? PersonId { get; init; }
    public PersonProfile? Person { get; init; }
    public string? Zone { get; init; }
    public string? CameraName { get; init; }
}

