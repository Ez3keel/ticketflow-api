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
    private readonly IRealtimeNotifier _realtimeNotifier;

    public OrderProcessingService(
        IEventRepository eventRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IRealtimeNotifier realtimeNotifier)
    {
        _eventRepository = eventRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task ConfirmOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        var @event = await _eventRepository.GetBySessionIdAsync(order.EventSessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {order.EventSessionId} was not found.");

        var session = @event.Sessions.First(s => s.Id == order.EventSessionId);
        var confirmedSeats = order.Items
            .Select(item => session.Seats.First(s => s.Id == item.SeatId))
            .ToList();
        foreach (var seat in confirmedSeats)
            seat.Confirm();

        order.Confirm();

        await _eventRepository.UpdateAsync(@event, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync($"session:{session.Id}", cancellationToken);

        // This is the notification path that only works because of the Redis
        // SignalR backplane: this code runs in the Worker process, which has no
        // WebSocket connections of its own, yet both the group and the per-user
        // message reach clients connected to the Api process.
        foreach (var seat in confirmedSeats)
            await _realtimeNotifier.NotifySeatStatusChangedAsync(session.Id, seat.Id, seat.Status.ToString(), cancellationToken);

        await _realtimeNotifier.NotifyOrderConfirmedAsync(order.UserId, order.Id, cancellationToken);
    }
}
