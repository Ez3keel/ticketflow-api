using Microsoft.EntityFrameworkCore;
using TicketFlow.Application.Common.Interfaces;
using TicketFlow.Domain.Entities;

namespace TicketFlow.Infrastructure.Persistence.Repositories;

public class EfOrderRepository : IOrderRepository
{
    private readonly TicketFlowDbContext _context;

    public EfOrderRepository(TicketFlowDbContext context)
    {
        _context = context;
    }

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        => _context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
        => await _context.Orders.AddAsync(order, cancellationToken);

    // See EfEventRepository.UpdateAsync: same-context tracking makes this a no-op.
    public Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
