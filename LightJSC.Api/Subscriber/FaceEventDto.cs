using LightJSC.Core.Models;

namespace LightJSC.Api.Subscriber;

public sealed class FaceEventDto
{
    public string Id { get; init; } = string.Empty;
    public string CameraId { get; init; } = string.Empty;
    public string? CameraCode { get; init; }
    public string? CameraIp { get; init; }
    public string? CameraName { get; init; }
    public string? Zone { get; init; }
    public DateTimeOffset EventTimeUtc { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public string? FeatureBase64 { get; init; }
    public float L2Norm { get; init; }
    public string FeatureVersion { get; init; } = string.Empty;
    public int? Age { get; init; }
    public string? Gender { get; init; }
    public string? Mask { get; init; }
    public string? ScoreText { get; init; }
    public string? SimilarityText { get; init; }
    public string? WatchlistEntryId { get; init; }
    public string? PersonId { get; init; }
    public PersonProfile? Person { get; init; }
    public BoundingBox? BBox { get; init; }
    public bool IsKnown { get; init; }
    public string? FaceImageBase64 { get; init; }

    public static FaceEventDto FromPayload(FaceWebhookPayload payload, string idempotencyKey)
    {
        return new FaceEventDto
        {
            Id = idempotencyKey,
            CameraId = payload.CameraId,
            CameraCode = payload.CameraCode,
            CameraIp = payload.CameraIp,
            CameraName = payload.CameraName,
            Zone = payload.Zone,
            EventTimeUtc = payload.EventTimeUtc == default ? DateTimeOffset.UtcNow : payload.EventTimeUtc,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            FeatureBase64 = payload.FeatureBase64,
            L2Norm = payload.L2Norm,
            FeatureVersion = payload.FeatureVersion,
            Age = payload.Age,
            Gender = payload.Gender,
            Mask = payload.Mask,
            ScoreText = payload.Score.HasValue ? payload.Score.Value.ToString("0.000") : null,
            SimilarityText = payload.Similarity.HasValue ? payload.Similarity.Value.ToString("0.000") : null,
            WatchlistEntryId = payload.WatchlistEntryId,
            PersonId = payload.PersonId,
            Person = payload.Person,
            BBox = payload.BBox,
            IsKnown = payload.IsKnown,
            FaceImageBase64 = payload.FaceImageBase64
        };
    }
}
