namespace LightJSC.Core.Models;

public sealed class FaceEventRecord
{
    public Guid Id { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public bool IsKnown { get; set; }
    public string? WatchlistEntryId { get; set; }
    public string? PersonId { get; set; }
    public string? PersonJson { get; set; }
    public float? Similarity { get; set; }
    public float? Score { get; set; }
    public string? BestshotPath { get; set; }
    public string? ThumbPath { get; set; }
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public string? Mask { get; set; }
    public string? BBoxJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
