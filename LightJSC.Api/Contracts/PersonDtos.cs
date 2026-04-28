namespace LightJSC.Api.Contracts;

/// <summary>
/// Person create/update payload.
/// </summary>
public sealed class PersonRequest
{
    /// <summary>Unique person code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>First name.</summary>
    public string FirstName { get; set; } = string.Empty;
    /// <summary>Last name.</summary>
    public string LastName { get; set; } = string.Empty;
    /// <summary>Personal identifier from CCCD/VNeID.</summary>
    public string? PersonalId { get; set; }
    /// <summary>Document/card number from CCCD/VNeID.</summary>
    public string? DocumentNumber { get; set; }
    /// <summary>Date of birth.</summary>
    public DateOnly? DateOfBirth { get; set; }
    /// <summary>Date of issue.</summary>
    public DateOnly? DateOfIssue { get; set; }
    /// <summary>Address from CCCD/VNeID.</summary>
    public string? Address { get; set; }
    /// <summary>Raw QR payload, if available.</summary>
    public string? RawQrPayload { get; set; }
    /// <summary>Gender label.</summary>
    public string? Gender { get; set; }
    /// <summary>Age.</summary>
    public int? Age { get; set; }
    /// <summary>Remarks.</summary>
    public string? Remarks { get; set; }
    /// <summary>Category/group.</summary>
    public string? Category { get; set; }
    /// <summary>List classification (WhiteList/BlackList).</summary>
    public string? ListType { get; set; }
    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Person response payload.
/// </summary>
public sealed class PersonResponse
{
    /// <summary>Person id.</summary>
    public Guid Id { get; set; }
    /// <summary>Unique person code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>First name.</summary>
    public string FirstName { get; set; } = string.Empty;
    /// <summary>Last name.</summary>
    public string LastName { get; set; } = string.Empty;
    /// <summary>Personal identifier from CCCD/VNeID.</summary>
    public string? PersonalId { get; set; }
    /// <summary>Document/card number from CCCD/VNeID.</summary>
    public string? DocumentNumber { get; set; }
    /// <summary>Date of birth.</summary>
    public DateOnly? DateOfBirth { get; set; }
    /// <summary>Date of issue.</summary>
    public DateOnly? DateOfIssue { get; set; }
    /// <summary>Address from CCCD/VNeID.</summary>
    public string? Address { get; set; }
    /// <summary>Raw QR payload, if available.</summary>
    public string? RawQrPayload { get; set; }
    /// <summary>Gender label.</summary>
    public string? Gender { get; set; }
    /// <summary>Age.</summary>
    public int? Age { get; set; }
    /// <summary>Remarks.</summary>
    public string? Remarks { get; set; }
    /// <summary>Category/group.</summary>
    public string? Category { get; set; }
    /// <summary>List classification (WhiteList/BlackList).</summary>
    public string? ListType { get; set; }
    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }
    /// <summary>True when at least one active template exists.</summary>
    public bool IsEnrolled { get; set; }
    /// <summary>Latest enrolled face image (data URI), if stored.</summary>
    public string? EnrolledFaceImageBase64 { get; set; }
    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Updated timestamp (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Enroll a face template using i-PRO CGI.
/// </summary>
public sealed class EnrollFaceRequest
{
    /// <summary>Camera id stored in ingestor.</summary>
    public string CameraId { get; set; } = string.Empty;
    /// <summary>JPEG image in base64 (data URI allowed).</summary>
    public string ImageBase64 { get; set; } = string.Empty;
    /// <summary>Store the JPEG in the template record.</summary>
    public bool StoreFaceImage { get; set; } = true;
    /// <summary>Optional source camera id override.</summary>
    public string? SourceCameraId { get; set; }
}

/// <summary>
/// Face template response.
/// </summary>
public sealed class FaceTemplateResponse
{
    /// <summary>Template id.</summary>
    public Guid Id { get; set; }
    /// <summary>Person id.</summary>
    public Guid PersonId { get; set; }
    /// <summary>Feature version label.</summary>
    public string FeatureVersion { get; set; } = string.Empty;
    /// <summary>L2 norm of the feature.</summary>
    public float L2Norm { get; set; }
    /// <summary>Source camera id.</summary>
    public string? SourceCameraId { get; set; }
    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }
    /// <summary>Stored face image (data URI), if available.</summary>
    public string? FaceImageBase64 { get; set; }
    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Updated timestamp (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Update face template active status.
/// </summary>
public sealed class TemplateStatusRequest
{
    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; }
}

