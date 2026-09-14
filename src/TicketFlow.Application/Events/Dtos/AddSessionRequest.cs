namespace TicketFlow.Application.Events.Dtos;

public record AddSessionRequest(string VenueName, DateTime StartsAtUtc, decimal TicketPrice);
