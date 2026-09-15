using Microsoft.EntityFrameworkCore;
using TicketFlow.Application.Common.Interfaces;
using TicketFlow.Domain.Entities;

namespace TicketFlow.Infrastructure.Persistence.Repositories;

public class EfEventRepository : IEventRepository
{
    private readonly TicketFlowDbContext _context;

    public EfEventRepository(TicketFlowDbContext context)
    {
        _context = context;
    }

    public Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default)
        => Query().FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

    public Task<Event?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => Query().FirstOrDefaultAsync(e => e.Sessions.Any(s => s.Id == sessionId), cancellationToken);

    public async Task<IReadOnlyList<Event>> ListAsync(CancellationToken cancellationToken = default)
        => await Query().ToListAsync(cancellationToken);

    public async Task AddAsync(Event @event, CancellationToken cancellationToken = default)
        => await _context.Events.AddAsync(@event, cancellationToken);

    // No-op: `@event` was loaded from this same (Scoped) DbContext earlier in the
    // request, so EF's change tracker already sees every mutation made through the
    // domain methods. Persisting happens once, explicitly, via IUnitOfWork.
    public Task UpdateAsync(Event @event, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private IQueryable<Event> Query()
        => _context.Events.Include(e => e.Sessions).ThenInclude(s => s.Seats);
}
