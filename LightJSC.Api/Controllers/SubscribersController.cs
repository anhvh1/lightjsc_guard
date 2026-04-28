using LightJSC.Api.Contracts;
using LightJSC.Core.Interfaces;
using SubscriberEntity = LightJSC.Core.Models.Subscriber;
using Microsoft.AspNetCore.Mvc;

namespace LightJSC.Api.Controllers;

/// <summary>
/// Manage webhook subscribers for unknown face events.
/// </summary>
[ApiController]
[Route("api/v1/subscribers")]
public sealed class SubscribersController : ControllerBase
{
    private readonly ISubscriberRepository _subscriberRepository;

    public SubscribersController(ISubscriberRepository subscriberRepository)
    {
        _subscriberRepository = subscriberRepository;
    }

    /// <summary>
    /// Create a webhook subscriber.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SubscriberResponse>> Create([FromBody] SubscriberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EndpointUrl))
        {
            return BadRequest("EndpointUrl is required.");
        }

        var subscriber = new SubscriberEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            EndpointUrl = request.EndpointUrl,
            Enabled = request.Enabled,
            CreatedAt = DateTime.UtcNow
        };

        await _subscriberRepository.AddAsync(subscriber, cancellationToken);
        return Ok(ToResponse(subscriber));
    }

    /// <summary>
    /// Update a webhook subscriber.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SubscriberResponse>> Update(Guid id, [FromBody] SubscriberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EndpointUrl))
        {
            return BadRequest("EndpointUrl is required.");
        }

        var subscriber = new SubscriberEntity
        {
            Id = id,
            Name = request.Name,
            EndpointUrl = request.EndpointUrl,
            Enabled = request.Enabled
        };

        var updated = await _subscriberRepository.UpdateAsync(subscriber, cancellationToken);
        if (updated is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(updated));
    }

    /// <summary>
    /// List all webhook subscribers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SubscriberResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var subscribers = await _subscriberRepository.ListAsync(cancellationToken);
        return Ok(subscribers.Select(ToResponse).ToList());
    }

    /// <summary>
    /// Delete a subscriber.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _subscriberRepository.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static SubscriberResponse ToResponse(SubscriberEntity subscriber)
    {
        return new SubscriberResponse
        {
            Id = subscriber.Id,
            Name = subscriber.Name,
            EndpointUrl = subscriber.EndpointUrl,
            Enabled = subscriber.Enabled,
            CreatedAt = subscriber.CreatedAt
        };
    }
}

