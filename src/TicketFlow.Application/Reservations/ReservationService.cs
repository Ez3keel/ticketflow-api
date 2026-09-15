using TicketFlow.Application.Common.Exceptions;
using TicketFlow.Application.Common.Interfaces;
using TicketFlow.Application.Reservations.Dtos;
using TicketFlow.Domain.Entities;
using TicketFlow.Domain.Exceptions;

namespace TicketFlow.Application.Reservations;

public class ReservationService
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(10);

    private readonly IEventRepository _eventRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public ReservationService(
        IEventRepository eventRepository,
        IOrderRepository orderRepository,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork)
    {
        _eventRepository = eventRepository;
        _orderRepository = orderRepository;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderDto> ReserveSeatsAsync(Guid userId, ReserveSeatsRequest request, CancellationToken cancellationToken = default)
    {
        var @event = await _eventRepository.GetBySessionIdAsync(request.SessionId, cancellationToken)
            ?? throw new NotFoundException($"Session {request.SessionId} was not found.");

        var session = @event.Sessions.First(s => s.Id == request.SessionId);

        var seats = request.SeatIds
            .Select(seatId => session.Seats.FirstOrDefault(s => s.Id == seatId)
                ?? throw new NotFoundException($"Seat {seatId} was not found in this session."))
            .ToList();

        // Two passes: fail the whole request before reserving anything if any seat is
        // unavailable, so a request never leaves a partial set of seats reserved.
        // This check-then-act is only race-free within a single process; coordinating
        // it across multiple API instances is exactly the job Redis takes over later.
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

        return ToOrderDto(order);
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
