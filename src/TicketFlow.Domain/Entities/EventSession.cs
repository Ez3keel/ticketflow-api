using TicketFlow.Domain.Common;
using TicketFlow.Domain.Exceptions;

namespace TicketFlow.Domain.Entities;

public class EventSession : Entity
{
    private readonly List<Seat> _seats = new();

    public Guid EventId { get; private set; }
    public string VenueName { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public decimal TicketPrice { get; private set; }
    public IReadOnlyCollection<Seat> Seats => _seats.AsReadOnly();

    private EventSession()
    {
        VenueName = string.Empty;
    }

    internal EventSession(Guid eventId, string venueName, DateTime startsAtUtc, decimal ticketPrice)
    {
        if (string.IsNullOrWhiteSpace(venueName))
            throw new DomainException("Venue name is required.");
        if (ticketPrice < 0)
            throw new DomainException("Ticket price cannot be negative.");

        EventId = eventId;
        VenueName = venueName;
        StartsAtUtc = startsAtUtc;
        TicketPrice = ticketPrice;
    }

    public Seat AddSeat(string row, int number)
    {
        if (_seats.Any(s => s.Row == row && s.Number == number))
            throw new DomainException($"Seat {row}{number} already exists in this session.");

        var seat = new Seat(Id, row, number);
        _seats.Add(seat);
        return seat;
    }
}
