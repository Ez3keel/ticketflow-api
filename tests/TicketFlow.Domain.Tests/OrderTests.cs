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
    public void MarkAsProcessing_WithNoItems_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        Assert.Throws<DomainException>(() => order.MarkAsProcessing());
    }

    [Fact]
    public void MarkAsProcessing_WithItems_SetsProcessing()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);

        order.MarkAsProcessing();

        Assert.Equal(OrderStatus.Processing, order.Status);
    }

    [Fact]
    public void MarkAsProcessing_WhenAlreadyProcessing_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);
        order.MarkAsProcessing();

        Assert.Throws<DomainException>(() => order.MarkAsProcessing());
    }

    [Fact]
    public void Confirm_BeforeProcessing_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);

        Assert.Throws<DomainException>(() => order.Confirm());
    }

    [Fact]
    public void Confirm_AfterProcessing_SetsConfirmed()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);
        order.MarkAsProcessing();

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void AddItem_AfterMarkAsProcessing_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);
        order.MarkAsProcessing();

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
    public void Cancel_WhileProcessing_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);
        order.MarkAsProcessing();

        Assert.Throws<DomainException>(() => order.Cancel());
    }

    [Fact]
    public void Cancel_AfterConfirm_Throws()
    {
        var order = new Order(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        order.AddItem(Guid.NewGuid(), 150m);
        order.MarkAsProcessing();
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
