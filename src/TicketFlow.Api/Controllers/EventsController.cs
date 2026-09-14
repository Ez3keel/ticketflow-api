using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketFlow.Api.Common;
using TicketFlow.Application.Events;
using TicketFlow.Application.Events.Dtos;

namespace TicketFlow.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly EventCatalogService _catalogService;
    private readonly IValidator<CreateEventRequest> _createEventValidator;
    private readonly IValidator<AddSessionRequest> _addSessionValidator;
    private readonly IValidator<AddSeatsRequest> _addSeatsValidator;

    public EventsController(
        EventCatalogService catalogService,
        IValidator<CreateEventRequest> createEventValidator,
        IValidator<AddSessionRequest> addSessionValidator,
        IValidator<AddSeatsRequest> addSeatsValidator)
    {
        _catalogService = catalogService;
        _createEventValidator = createEventValidator;
        _addSessionValidator = addSessionValidator;
        _addSeatsValidator = addSeatsValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EventDto>>> List(CancellationToken cancellationToken)
        => Ok(await _catalogService.ListEventsAsync(cancellationToken));

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<EventDto>> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var validation = await _createEventValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToProblemDetails());

        var result = await _catalogService.CreateEventAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{eventId:guid}/sessions")]
    [Authorize]
    public async Task<ActionResult<EventDto>> AddSession(Guid eventId, AddSessionRequest request, CancellationToken cancellationToken)
    {
        var validation = await _addSessionValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToProblemDetails());

        var result = await _catalogService.AddSessionAsync(eventId, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<ActionResult<EventSessionDetailDto>> GetSession(Guid sessionId, CancellationToken cancellationToken)
        => Ok(await _catalogService.GetSessionAsync(sessionId, cancellationToken));

    [HttpPost("sessions/{sessionId:guid}/seats")]
    [Authorize]
    public async Task<ActionResult<EventSessionDetailDto>> AddSeats(Guid sessionId, AddSeatsRequest request, CancellationToken cancellationToken)
    {
        var validation = await _addSeatsValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToProblemDetails());

        var result = await _catalogService.AddSeatsAsync(sessionId, request, cancellationToken);
        return Ok(result);
    }
}
