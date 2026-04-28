namespace LightJSC.Api.Contracts;

public sealed class AttendanceSummaryRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public IReadOnlyCollection<string>? CameraIds { get; set; }
    public IReadOnlyCollection<string>? Categories { get; set; }
    public IReadOnlyCollection<Guid>? PersonIds { get; set; }
}

public sealed class AttendanceSummaryResponse
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int DaysInMonth { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public IReadOnlyList<AttendanceSummaryPersonRow> Items { get; set; } = Array.Empty<AttendanceSummaryPersonRow>();
}

public sealed class AttendanceSummaryPersonRow
{
    public Guid PersonId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? PersonalId { get; set; }
    public string? Category { get; set; }
    public IReadOnlyList<AttendanceSummaryDayCell> Days { get; set; } = Array.Empty<AttendanceSummaryDayCell>();
}

public sealed class AttendanceSummaryDayCell
{
    public int Day { get; set; }
    public AttendanceEventPoint? InEvent { get; set; }
    public AttendanceEventPoint? OutEvent { get; set; }
}

public sealed class AttendanceEventPoint
{
    public Guid EventId { get; set; }
    public DateTime EventTimeUtc { get; set; }
}
