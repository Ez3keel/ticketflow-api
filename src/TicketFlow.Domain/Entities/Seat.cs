using TicketFlow.Domain.Common;
using TicketFlow.Domain.Enums;
using TicketFlow.Domain.Exceptions;

namespace TicketFlow.Domain.Entities;

public class Seat : Entity
{
    public Guid EventSessionId { get; private set; }
    public string Row { get; private set; }
    public int Number { get; private set; }
    public SeatStatus Status { get; private set; } = SeatStatus.Available;
    public DateTime? ReservedUntil { get; private set; }

    private Seat()
    {
        Row = string.Empty;
    }

    public Seat(Guid eventSessionId, string row, int number)
    {
        if (string.IsNullOrWhiteSpace(row))
            throw new DomainException("Seat row is required.");
        if (number <= 0)
            throw new DomainException("Seat number must be positive.");

        EventSessionId = eventSessionId;
        Row = row;
        Number = number;
    }

    public void Reserve(DateTime utcNow, TimeSpan holdDuration)
    {
        if (Status != SeatStatus.Available)
            throw new DomainException($"Seat {Row}{Number} is not available (current status: {Status}).");

        Status = SeatStatus.Reserved;
        ReservedUntil = utcNow.Add(holdDuration);
    }

    public void Confirm()
    {
        if (Status != SeatStatus.Reserved)
            throw new DomainException($"Seat {Row}{Number} cannot be confirmed from status {Status}.");

        Status = SeatStatus.Sold;
        ReservedUntil = null;
    }

    public void Release()
    {
        if (Status == SeatStatus.Sold)
            throw new DomainException($"Seat {Row}{Number} is already sold and cannot be released.");

        Status = SeatStatus.Available;
        ReservedUntil = null;
    }

    public bool IsReservationExpired(DateTime utcNow)
        => Status == SeatStatus.Reserved && ReservedUntil.HasValue && ReservedUntil.Value <= utcNow;
}
