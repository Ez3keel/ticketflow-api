using TicketFlow.Application.Common.Exceptions;
using TicketFlow.Application.Common.Interfaces;
using TicketFlow.Application.Reservations.Dtos;
using TicketFlow.Domain.Entities;
using TicketFlow.Domain.Exceptions;

namespace TicketFlow.Application.Reservations;

public class ReservationService
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(10);

    // Only needs to outlive the check-then-reserve round trip to the database (a few
    // hundred ms at most), not the 10-minute hold itself -- that's durably recorded
    // in Postgres via Seat.ReservedUntil once this lock is released. If this
    // instance crashed mid-operation, the lock self-expires instead of jamming the
    // seat for other requests indefinitely.
    private static readonly TimeSpan SeatLockDuration = TimeSpan.FromSeconds(5);

    private readonly IEventRepository _eventRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ICacheService _cache;

    public ReservationService(
        IEventRepository eventRepository,
        IOrderRepository orderRepository,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork,
        IDistributedLockProvider lockProvider,
        ICacheService cache)
    {
        _eventRepository = eventRepository;
        _orderRepository = orderRepository;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
        _lockProvider = lockProvider;
        _cache = cache;
    }

    public async Task<OrderDto> ReserveSeatsAsync(Guid userId, ReserveSeatsRequest request, CancellationToken cancellationToken = default)
    {
        var @event = await _eventRepository.GetBySessionIdAsync(request.SessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {request.SessionId} was not found.");

        var session = @event.Sessions.First(s => s.Id == request.SessionId);

        var seats = request.SeatIds
            .Select(seatId => session.Seats.FirstOrDefault(s => s.Id == seatId)
                ?? throw new NotFoundException($"Seat {seatId} was not found in this session."))
            .OrderBy(s => s.Id) // consistent lock order across concurrent multi-seat requests avoids deadlock
            .ToList();

        var locks = new List<IDistributedLock>();
        try
        {
            foreach (var seat in seats)
            {
                var @lock = await _lockProvider.TryAcquireAsync($"seat-lock:{seat.Id}", SeatLockDuration, cancellationToken);
                if (@lock is null)
                    throw new DomainException($"Seat {seat.Row}{seat.Number} is currently being reserved by someone else. Please try again.");

                locks.Add(@lock);
            }

            // The seats above were loaded before we held any lock, so another
            // request could have reserved one of them in the meantime. Now that
            // every lock is held, force a fresh read so the availability check
            // below reflects the true current state instead of a stale snapshot.
            await _eventRepository.ReloadSeatsAsync(seats.Select(s => s.Id), cancellationToken);

            var unavailable = seats.Where(s => s.Status != Domain.Enums.SeatStatus.Available).ToList();
            if (unavailable.Count > 0)
                throw new DomainException($"Seat(s) {string.Join(", ", unavailable.Select(s => $"{s.Row}{s.Number}"))} are no longer available.");

            var now = _dateTimeProvider.UtcNow;
            var order = new Order(userId, session.Id, now);

            foreach (var seat in seats)
            {
                seat.Reserve(now, HoldDuration);
                order.AddItem(seat.Id, session.TicketPrice);
            }

            await _eventRepository.UpdateAsync(@event, cancellationToken);
            await _orderRepository.AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _cache.RemoveAsync($"session:{session.Id}", cancellationToken);

            return ToOrderDto(order);
        }
        finally
        {
            foreach (var @lock in locks)
                await @lock.DisposeAsync();
        }
    }

    public async Task<OrderDto> ConfirmOrderAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(userId, orderId, cancellationToken);
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

        return ToOrderDto(order);
    }

    public async Task<OrderDto> CancelOrderAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(userId, orderId, cancellationToken);
        var @event = await _eventRepository.GetBySessionIdAsync(order.EventSessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {order.EventSessionId} was not found.");

        var session = @event.Sessions.First(s => s.Id == order.EventSessionId);
        foreach (var item in order.Items)
            session.Seats.First(s => s.Id == item.SeatId).Release();

        order.Cancel();

        await _eventRepository.UpdateAsync(@event, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync($"session:{session.Id}", cancellationToken);

        return ToOrderDto(order);
    }

    private async Task<Order> GetOwnedOrderAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.UserId != userId)
            throw new NotFoundException($"Order {orderId} was not found.");

        return order;
    }

    private static OrderDto ToOrderDto(Order order) => new(
        order.Id,
        order.UserId,
        order.EventSessionId,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.TotalAmount,
        order.Items.Select(i => new OrderItemDto(i.SeatId, i.Price)).ToList());
}
