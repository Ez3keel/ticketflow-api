using TicketFlow.Application.Common.Interfaces;

namespace TicketFlow.Infrastructure.Common;

public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
