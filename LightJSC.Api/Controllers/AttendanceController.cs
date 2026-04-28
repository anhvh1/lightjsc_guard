using LightJSC.Api.Contracts;
using LightJSC.Infrastructure.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace LightJSC.Api.Controllers;

[ApiController]
[Route("api/v1/attendance")]
public sealed class AttendanceController : ControllerBase
{
    private readonly AttendanceRepository _repository;

    public AttendanceController(AttendanceRepository repository)
    {
        _repository = repository;
    }

    [HttpPost("summary")]
    public async Task<ActionResult<AttendanceSummaryResponse>> GetSummary(
        [FromBody] AttendanceSummaryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Year < 2000 || request.Year > 2100)
        {
            return BadRequest("Year is invalid.");
        }

        if (request.Month is < 1 or > 12)
        {
            return BadRequest("Month must be between 1 and 12.");
        }

        var filter = new AttendanceSummaryFilter
        {
            Year = request.Year,
            Month = request.Month,
            CameraIds = request.CameraIds,
            Categories = request.Categories,
            PersonIds = request.PersonIds
        };

        var data = await _repository.GetMonthlySummaryAsync(filter, cancellationToken);
        var grouped = data.Rows
            .GroupBy(row => new
            {
                row.PersonId,
                row.FirstName,
                row.LastName,
                row.PersonalId,
                row.Category
            })
            .Select(group =>
            {
                var days = Enumerable.Range(1, data.DaysInMonth)
                    .Select(day =>
                    {
                        var row = group.FirstOrDefault(item => item.DayNumber == day);
                        return new AttendanceSummaryDayCell
                        {
                            Day = day,
                            InEvent = row?.InEventId is Guid inEventId && row.InEventTimeUtc.HasValue
                                ? new AttendanceEventPoint
                                {
                                    EventId = inEventId,
                                    EventTimeUtc = row.InEventTimeUtc.Value
                                }
                                : null,
                            OutEvent = row?.OutEventId is Guid outEventId && row.OutEventTimeUtc.HasValue
                                ? new AttendanceEventPoint
                                {
                                    EventId = outEventId,
                                    EventTimeUtc = row.OutEventTimeUtc.Value
                                }
                                : null
                        };
                    })
                    .ToList();

                return new AttendanceSummaryPersonRow
                {
                    PersonId = group.Key.PersonId,
                    FullName = BuildFullName(group.Key.FirstName, group.Key.LastName),
                    PersonalId = group.Key.PersonalId,
                    Category = group.Key.Category,
                    Days = days
                };
            })
            .OrderBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Ok(new AttendanceSummaryResponse
        {
            Year = data.Year,
            Month = data.Month,
            DaysInMonth = data.DaysInMonth,
            GeneratedAtUtc = DateTime.UtcNow,
            Items = grouped
        });
    }

    private static string BuildFullName(string? firstName, string? lastName)
    {
        var parts = new[] { firstName?.Trim(), lastName?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var fullName = string.Join(' ', parts);
        return string.IsNullOrWhiteSpace(fullName) ? "-" : fullName;
    }
}
