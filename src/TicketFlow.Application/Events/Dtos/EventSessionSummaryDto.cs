namespace TicketFlow.Application.Events.Dtos;

public record EventSessionSummaryDto(Guid Id, string VenueName, DateTime StartsAtUtc, decimal TicketPrice, int SeatCount);
