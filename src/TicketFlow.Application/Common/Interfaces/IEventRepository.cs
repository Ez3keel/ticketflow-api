using TicketFlow.Domain.Entities;

namespace TicketFlow.Application.Common.Interfaces;

public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<Event?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Event>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Event @event, CancellationToken cancellationToken = default);

    Task UpdateAsync(Event @event, CancellationToken cancellationToken = default);
}
