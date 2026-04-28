namespace LightJSC.Api.Contracts;

public sealed class CreatePersonScanSessionRequest
{
    public string? CameraId { get; set; }
}

public sealed class PersonScanRequest
{
    public string? Mode { get; set; }
    public bool ResetQr { get; set; }
    public bool ResetFace { get; set; }
}

public sealed class PersonScanPersonResponse
{
    public string? Code { get; set; }
    public string? PersonalId { get; set; }
    public string? DocumentNumber { get; set; }
    public string? FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfIssue { get; set; }
    public int? Age { get; set; }
    public string? Address { get; set; }
    public string? RawQrPayload { get; set; }
}

public sealed class PersonScanResultResponse
{
    public Guid SessionId { get; set; }
    public string? CameraId { get; set; }
    public string Status { get; set; } = "Previewing";
    public bool QrDetected { get; set; }
    public bool FaceDetected { get; set; }
    public string? SnapshotImageBase64 { get; set; }
    public string? FaceImageBase64 { get; set; }
    public string? RawQrPayload { get; set; }
    public PersonScanPersonResponse? Person { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ScannedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
