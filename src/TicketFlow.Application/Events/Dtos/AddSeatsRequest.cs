namespace TicketFlow.Application.Events.Dtos;

public record AddSeatsRequest(string Row, int FromNumber, int ToNumber);
