using TicketFlow.Application.Common.Exceptions;
using TicketFlow.Application.Common.Interfaces;

namespace TicketFlow.Application.Reservations;

// Invoked only by the queue consumer (TicketFlow.Worker), never by a controller --
// that separation is deliberate: ReservationService is the API-facing surface
// (things an HTTP caller asks for and waits on), this is the message-handler-facing
// surface (things that happen after a payment has actually been processed).
public class OrderProcessingService
{
    private readonly IEventRepository _eventRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public OrderProcessingService(
        IEventRepository eventRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICacheService cache)
    {
        _eventRepository = eventRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task ConfirmOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        var @event = await _eventRepository.GetBySessionIdAsync(order.EventSessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {order.EventSessionId} was not found.");

        var session = @event.Sessions.First(s => s.Id == order.EventSessionId);
        foreach (var item in order.Items)
            session.Seats.First(s => s.Id == item.SeatId).Confirm();

        order.Confirm();

        await _eventRepository.UpdateAsync(@event, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync($"session:{session.Id}", cancellationToken);
    }
}
