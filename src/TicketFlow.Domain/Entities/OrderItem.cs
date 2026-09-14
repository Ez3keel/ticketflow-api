using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Entities;

public class OrderItem : Entity
{
    public Guid OrderId { get; private set; }
    public Guid SeatId { get; private set; }
    public decimal Price { get; private set; }

    private OrderItem()
    {
    }

    internal OrderItem(Guid orderId, Guid seatId, decimal price)
    {
        OrderId = orderId;
        SeatId = seatId;
        Price = price;
    }
}
