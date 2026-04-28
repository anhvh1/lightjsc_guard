using LightJSC.Core.Models;

namespace LightJSC.Api.Contracts;

public sealed class FaceEventSearchRequest
{
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public IReadOnlyCollection<string>? CameraIds { get; set; }
    public bool? IsKnown { get; set; }
    public string? ListType { get; set; }
    public string? Gender { get; set; }
    public int? AgeMin { get; set; }
    public int? AgeMax { get; set; }
    public string? Mask { get; set; }
    public float? ScoreMin { get; set; }
    public float? SimilarityMin { get; set; }
    public string? PersonQuery { get; set; }
    public IReadOnlyCollection<string>? PersonIds { get; set; }
    public string? Category { get; set; }
    public bool? HasFeature { get; set; }
    public IReadOnlyCollection<string>? WatchlistEntryIds { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool IncludeBestshot { get; set; }
}

public sealed class FaceEventSearchFilterRequest
{
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public IReadOnlyCollection<string>? CameraIds { get; set; }
    public bool? IsKnown { get; set; }
    public string? ListType { get; set; }
    public string? Gender { get; set; }
    public int? AgeMin { get; set; }
    public int? AgeMax { get; set; }
    public string? Mask { get; set; }
    public float? ScoreMin { get; set; }
    public float? SimilarityMin { get; set; }
    public string? PersonQuery { get; set; }
    public IReadOnlyCollection<string>? PersonIds { get; set; }
    public string? Category { get; set; }
    public bool? HasFeature { get; set; }
    public IReadOnlyCollection<string>? WatchlistEntryIds { get; set; }
}

public sealed class FaceEventSearchResponse
{
    public IReadOnlyList<FaceEventResponse> Items { get; set; } = Array.Empty<FaceEventResponse>();
    public int Total { get; set; }
}

public sealed class FaceEventResponse
{
    public Guid Id { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public string? CameraIp { get; set; }
    public string? CameraZone { get; set; }
    public bool IsKnown { get; set; }
    public string? WatchlistEntryId { get; set; }
    public string? PersonId { get; set; }
    public PersonProfile? Person { get; set; }
    public float? Similarity { get; set; }
    public float? Score { get; set; }
    public string? BestshotBase64 { get; set; }
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public string? Mask { get; set; }
    public bool HasFeature { get; set; }
    public float? TraceSimilarity { get; set; }
}

public sealed class FaceTracePersonRequest
{
    public Guid PersonId { get; set; }
    public int TopK { get; set; } = 50;
    public float? SimilarityMin { get; set; }
    public bool IncludeBestshot { get; set; }
    public FaceEventSearchFilterRequest? Filter { get; set; }
}

public sealed class FaceTraceImageRequest
{
    public string? CameraId { get; set; }
    public string ImageBase64 { get; set; } = string.Empty;
    public int TopK { get; set; } = 50;
    public float? SimilarityMin { get; set; }
    public bool IncludeBestshot { get; set; }
    public FaceEventSearchFilterRequest? Filter { get; set; }
}

public sealed class FaceTraceEventRequest
{
    public int TopK { get; set; } = 50;
    public float? SimilarityMin { get; set; }
    public bool IncludeBestshot { get; set; }
    public FaceEventSearchFilterRequest? Filter { get; set; }
}

public sealed class BestshotResponse
{
    public string? Base64 { get; set; }
}
