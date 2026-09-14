using TicketFlow.Domain.Common;
using TicketFlow.Domain.Exceptions;

namespace TicketFlow.Domain.Entities;

public class Event : Entity
{
    private readonly List<EventSession> _sessions = new();

    public string Name { get; private set; }
    public string Description { get; private set; }
    public IReadOnlyCollection<EventSession> Sessions => _sessions.AsReadOnly();

    private Event()
    {
        Name = string.Empty;
        Description = string.Empty;
    }

    public Event(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Event name is required.");

        Name = name;
        Description = description ?? string.Empty;
    }

    public EventSession AddSession(string venueName, DateTime startsAtUtc)
    {
        var session = new EventSession(Id, venueName, startsAtUtc);
        _sessions.Add(session);
        return session;
    }
}
