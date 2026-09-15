namespace TicketFlow.Domain.Enums;

public enum OrderStatus
{
    PendingPayment,
    Processing,
    Confirmed,
    Cancelled,
    Expired
}
