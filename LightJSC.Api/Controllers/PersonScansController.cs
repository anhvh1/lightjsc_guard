using LightJSC.Api.Contracts;
using LightJSC.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LightJSC.Api.Controllers;

[ApiController]
[Route("api/v1/person-scans")]
public sealed class PersonScansController : ControllerBase
{
    private readonly PersonScanSessionService _sessionService;

    public PersonScansController(PersonScanSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<PersonScanResultResponse>> CreateSession(
        [FromBody] CreatePersonScanSessionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _sessionService.CreateSessionAsync(request ?? new CreatePersonScanSessionRequest(), cancellationToken);
        return Ok(response);
    }

    [HttpPost("sessions/{sessionId:guid}/scan")]
    public async Task<ActionResult<PersonScanResultResponse>> Scan(
        Guid sessionId,
        [FromBody] PersonScanRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _sessionService.ScanAsync(
            sessionId,
            request ?? new PersonScanRequest(),
            cancellationToken);
        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }
}
