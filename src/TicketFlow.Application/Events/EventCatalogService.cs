using TicketFlow.Application.Common.Exceptions;
using TicketFlow.Application.Common.Interfaces;
using TicketFlow.Application.Events.Dtos;
using TicketFlow.Domain.Entities;

namespace TicketFlow.Application.Events;

public class EventCatalogService
{
    private readonly IEventRepository _eventRepository;
    private readonly IUnitOfWork _unitOfWork;

    public EventCatalogService(IEventRepository eventRepository, IUnitOfWork unitOfWork)
    {
        _eventRepository = eventRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<EventDto> CreateEventAsync(CreateEventRequest request, CancellationToken cancellationToken = default)
    {
        var @event = new Event(request.Name, request.Description);
        await _eventRepository.AddAsync(@event, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToEventDto(@event);
    }

    public async Task<IReadOnlyList<EventDto>> ListEventsAsync(CancellationToken cancellationToken = default)
    {
        var events = await _eventRepository.ListAsync(cancellationToken);
        return events.Select(ToEventDto).ToList();
    }

    public async Task<EventDto> AddSessionAsync(Guid eventId, AddSessionRequest request, CancellationToken cancellationToken = default)
    {
        var @event = await _eventRepository.GetByIdAsync(eventId, cancellationToken)
            ?? throw new NotFoundException($"Event {eventId} was not found.");

        @event.AddSession(request.VenueName, request.StartsAtUtc, request.TicketPrice);
        await _eventRepository.UpdateAsync(@event, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToEventDto(@event);
    }

    public async Task<EventSessionDetailDto> AddSeatsAsync(Guid sessionId, AddSeatsRequest request, CancellationToken cancellationToken = default)
    {
        var @event = await _eventRepository.GetBySessionIdAsync(sessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {sessionId} was not found.");

        var session = @event.Sessions.First(s => s.Id == sessionId);
        for (var number = request.FromNumber; number <= request.ToNumber; number++)
            session.AddSeat(request.Row, number);

        await _eventRepository.UpdateAsync(@event, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToSessionDetailDto(session);
    }

    public async Task<EventSessionDetailDto> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var @event = await _eventRepository.GetBySessionIdAsync(sessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {sessionId} was not found.");

        var session = @event.Sessions.First(s => s.Id == sessionId);
        return ToSessionDetailDto(session);
    }

    private static EventDto ToEventDto(Event @event) => new(
        @event.Id,
        @event.Name,
        @event.Description,
        @event.Sessions.Select(s => new EventSessionSummaryDto(s.Id, s.VenueName, s.StartsAtUtc, s.TicketPrice, s.Seats.Count)).ToList());

    private static EventSessionDetailDto ToSessionDetailDto(Domain.Entities.EventSession session) => new(
        session.Id,
        session.EventId,
        session.VenueName,
        session.StartsAtUtc,
        session.TicketPrice,
        session.Seats.Select(s => new SeatDto(s.Id, s.Row, s.Number, s.Status.ToString())).ToList());
}
