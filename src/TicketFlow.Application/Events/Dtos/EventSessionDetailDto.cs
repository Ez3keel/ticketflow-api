namespace TicketFlow.Application.Events.Dtos;

public record EventSessionDetailDto(
    Guid Id,
    Guid EventId,
    string VenueName,
    DateTime StartsAtUtc,
    decimal TicketPrice,
    IReadOnlyList<SeatDto> Seats);
