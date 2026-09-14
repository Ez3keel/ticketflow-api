namespace TicketFlow.Application.Reservations.Dtos;

public record ReserveSeatsRequest(Guid SessionId, List<Guid> SeatIds);
