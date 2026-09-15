namespace TicketFlow.Infrastructure.Messaging;

public record OrderConfirmationMessage(Guid OrderId);
