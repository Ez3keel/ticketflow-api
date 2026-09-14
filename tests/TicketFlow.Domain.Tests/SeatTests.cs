using TicketFlow.Domain.Entities;
using TicketFlow.Domain.Enums;
using TicketFlow.Domain.Exceptions;
using Xunit;

namespace TicketFlow.Domain.Tests;

public class SeatTests
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(10);

    [Fact]
    public void NewSeat_IsAvailable()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);

        Assert.Equal(SeatStatus.Available, seat.Status);
    }

    [Fact]
    public void Reserve_FromAvailable_SetsStatusAndExpiration()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);
        var now = DateTime.UtcNow;

        seat.Reserve(now, HoldDuration);

        Assert.Equal(SeatStatus.Reserved, seat.Status);
        Assert.Equal(now.Add(HoldDuration), seat.ReservedUntil);
    }

    [Fact]
    public void Reserve_WhenAlreadyReserved_Throws()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);
        seat.Reserve(DateTime.UtcNow, HoldDuration);

        Assert.Throws<DomainException>(() => seat.Reserve(DateTime.UtcNow, HoldDuration));
    }

    [Fact]
    public void Reserve_WhenSold_Throws()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);
        seat.Reserve(DateTime.UtcNow, HoldDuration);
        seat.Confirm();

        Assert.Throws<DomainException>(() => seat.Reserve(DateTime.UtcNow, HoldDuration));
    }

    [Fact]
    public void Confirm_FromReserved_SetsSoldAndClearsExpiration()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);
        seat.Reserve(DateTime.UtcNow, HoldDuration);

        seat.Confirm();

        Assert.Equal(SeatStatus.Sold, seat.Status);
        Assert.Null(seat.ReservedUntil);
    }

    [Fact]
    public void Confirm_WhenNotReserved_Throws()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);

        Assert.Throws<DomainException>(() => seat.Confirm());
    }

    [Fact]
    public void Release_FromReserved_BackToAvailable()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);
        seat.Reserve(DateTime.UtcNow, HoldDuration);

        seat.Release();

        Assert.Equal(SeatStatus.Available, seat.Status);
        Assert.Null(seat.ReservedUntil);
    }

    [Fact]
    public void Release_WhenSold_Throws()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);
        seat.Reserve(DateTime.UtcNow, HoldDuration);
        seat.Confirm();

        Assert.Throws<DomainException>(() => seat.Release());
    }

    [Fact]
    public void IsReservationExpired_BeforeHoldEnds_IsFalse()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);
        var now = DateTime.UtcNow;
        seat.Reserve(now, HoldDuration);

        Assert.False(seat.IsReservationExpired(now.AddMinutes(5)));
    }

    [Fact]
    public void IsReservationExpired_AfterHoldEnds_IsTrue()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);
        var now = DateTime.UtcNow;
        seat.Reserve(now, HoldDuration);

        Assert.True(seat.IsReservationExpired(now.AddMinutes(11)));
    }

    [Fact]
    public void IsReservationExpired_WhenAvailable_IsFalse()
    {
        var seat = new Seat(Guid.NewGuid(), "A", 1);

        Assert.False(seat.IsReservationExpired(DateTime.UtcNow.AddDays(1)));
    }
}
