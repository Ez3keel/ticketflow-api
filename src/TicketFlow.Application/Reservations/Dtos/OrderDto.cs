namespace TicketFlow.Application.Reservations.Dtos;

public record OrderDto(
    Guid Id,
    Guid UserId,
    Guid EventSessionId,
    string Status,
    DateTime CreatedAtUtc,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDto> Items);
