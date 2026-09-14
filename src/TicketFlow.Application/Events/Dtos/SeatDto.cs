namespace TicketFlow.Application.Events.Dtos;

public record SeatDto(Guid Id, string Row, int Number, string Status);
