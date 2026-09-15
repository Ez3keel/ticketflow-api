using TicketFlow.Domain.Entities;

namespace TicketFlow.Application.Common.Interfaces;

public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<Event?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Event>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Event @event, CancellationToken cancellationToken = default);

    Task UpdateAsync(Event @event, CancellationToken cancellationToken = default);

    // Forces the given already-tracked seats to be re-read from the database,
    // bypassing the DbContext's identity map. Needed after acquiring a distributed
    // lock: the seats were loaded before the lock was held, so the in-memory copy
    // may already be stale by the time the lock is granted.
    Task ReloadSeatsAsync(IEnumerable<Guid> seatIds, CancellationToken cancellationToken = default);
}
