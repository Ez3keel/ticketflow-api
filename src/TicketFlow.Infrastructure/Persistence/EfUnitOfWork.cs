using TicketFlow.Application.Common.Interfaces;

namespace TicketFlow.Infrastructure.Persistence;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly TicketFlowDbContext _context;

    public EfUnitOfWork(TicketFlowDbContext context)
    {
        _context = context;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
