namespace LightJSC.Core.Models;

public sealed class Person
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PersonalId { get; set; }
    public string? DocumentNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfIssue { get; set; }
    public string? Address { get; set; }
    public string? RawQrPayload { get; set; }
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public string? Remarks { get; set; }
    public string? Category { get; set; }
    public string? ListType { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

