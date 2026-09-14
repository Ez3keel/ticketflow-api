using System.Collections.Concurrent;
using TicketFlow.Application.Common.Interfaces;
using TicketFlow.Domain.Entities;

namespace TicketFlow.Infrastructure.Persistence.InMemory;

public class InMemoryEventRepository : IEventRepository
{
    private readonly ConcurrentDictionary<Guid, Event> _events = new();

    public Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        _events.TryGetValue(eventId, out var @event);
        return Task.FromResult(@event);
    }

    public Task<Event?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var @event = _events.Values.FirstOrDefault(e => e.Sessions.Any(s => s.Id == sessionId));
        return Task.FromResult(@event);
    }

    public Task<IReadOnlyList<Event>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Event>>(_events.Values.ToList());

    public Task AddAsync(Event @event, CancellationToken cancellationToken = default)
    {
        _events[@event.Id] = @event;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Event @event, CancellationToken cancellationToken = default)
    {
        _events[@event.Id] = @event;
        return Task.CompletedTask;
    }
}
