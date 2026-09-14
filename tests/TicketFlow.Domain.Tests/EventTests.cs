using TicketFlow.Domain.Entities;
using TicketFlow.Domain.Exceptions;
using Xunit;

namespace TicketFlow.Domain.Tests;

public class EventTests
{
    [Fact]
    public void AddSession_AddsToCollection()
    {
        var @event = new Event("Rock in Rio", "Annual rock festival");

        var session = @event.AddSession("Estadio Nilton Santos", DateTime.UtcNow.AddDays(30));

        Assert.Single(@event.Sessions);
        Assert.Same(session, @event.Sessions.First());
    }

    [Fact]
    public void AddSeat_DuplicateRowAndNumber_Throws()
    {
        var @event = new Event("Rock in Rio", "Annual rock festival");
        var session = @event.AddSession("Estadio Nilton Santos", DateTime.UtcNow.AddDays(30));
        session.AddSeat("A", 1);

        Assert.Throws<DomainException>(() => session.AddSeat("A", 1));
    }

    [Fact]
    public void AddSeat_DifferentRowOrNumber_Succeeds()
    {
        var @event = new Event("Rock in Rio", "Annual rock festival");
        var session = @event.AddSession("Estadio Nilton Santos", DateTime.UtcNow.AddDays(30));

        session.AddSeat("A", 1);
        session.AddSeat("A", 2);
        session.AddSeat("B", 1);

        Assert.Equal(3, session.Seats.Count);
    }
}
