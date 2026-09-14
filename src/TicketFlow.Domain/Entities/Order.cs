using TicketFlow.Domain.Common;
using TicketFlow.Domain.Enums;
using TicketFlow.Domain.Exceptions;

namespace TicketFlow.Domain.Entities;

public class Order : Entity
{
    private readonly List<OrderItem> _items = new();

    public Guid UserId { get; private set; }
    public Guid EventSessionId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public decimal TotalAmount => _items.Sum(i => i.Price);

    private Order()
    {
    }

    public Order(Guid userId, Guid eventSessionId, DateTime createdAtUtc)
    {
        UserId = userId;
        EventSessionId = eventSessionId;
        CreatedAtUtc = createdAtUtc;
    }

    public void AddItem(Guid seatId, decimal price)
    {
        if (Status != OrderStatus.PendingPayment)
            throw new DomainException("Cannot add items to an order that is no longer pending payment.");

        if (_items.Any(i => i.SeatId == seatId))
            throw new DomainException("This seat is already in the order.");

        _items.Add(new OrderItem(Id, seatId, price));
    }

    public void Confirm()
    {
        if (Status != OrderStatus.PendingPayment)
            throw new DomainException($"Order cannot be confirmed from status {Status}.");
        if (_items.Count == 0)
            throw new DomainException("Cannot confirm an order with no items.");

        Status = OrderStatus.Confirmed;
    }

    public void Cancel()
    {
        if (Status != OrderStatus.PendingPayment)
            throw new DomainException($"Order cannot be cancelled from status {Status}.");

        Status = OrderStatus.Cancelled;
    }

    public void Expire()
    {
        if (Status != OrderStatus.PendingPayment)
            throw new DomainException($"Order cannot expire from status {Status}.");

        Status = OrderStatus.Expired;
    }
}
