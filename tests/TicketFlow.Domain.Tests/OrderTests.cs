using TicketFlow.Domain.Entities;
using TicketFlow.Domain.Enums;
using TicketFlow.Domain.Exceptions;
using Xunit;

namespace TicketFlow.Domain.Tests;

public class OrderTests
{
    [Fact]
    public void NewOrder_IsPendingPayment_WithNoItems()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Empty(order.Items);
        Assert.Equal(0, order.TotalAmount);
    }

    [Fact]
    public void AddItem_AccumulatesTotalAmount()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        order.AddItem(Guid.NewGuid(), 150m);
        order.AddItem(Guid.NewGuid(), 200m);

        Assert.Equal(350m, order.TotalAmount);
        Assert.Equal(2, order.Items.Count);
    }

    [Fact]
    public void AddItem_SameSeatTwice_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        var seatId = Guid.NewGuid();
        order.AddItem(seatId, 150m);

        Assert.Throws<DomainException>(() => order.AddItem(seatId, 150m));
    }

    [Fact]
    public void Confirm_WithNoItems_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        Assert.Throws<DomainException>(() => order.Confirm());
    }

    [Fact]
    public void Confirm_WithItems_SetsConfirmed()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void AddItem_AfterConfirm_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);
        order.Confirm();

        Assert.Throws<DomainException>(() => order.AddItem(Guid.NewGuid(), 150m));
    }

    [Fact]
    public void Cancel_FromPendingPayment_SetsCancelled()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_AfterConfirm_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);
        order.Confirm();

        Assert.Throws<DomainException>(() => order.Cancel());
    }

    [Fact]
    public void Expire_FromPendingPayment_SetsExpired()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        order.Expire();

        Assert.Equal(OrderStatus.Expired, order.Status);
    }
}
