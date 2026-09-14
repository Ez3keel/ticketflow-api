namespace TicketFlow.Application.Events.Dtos;

public record EventDto(Guid Id, string Name, string Description, IReadOnlyList<EventSessionSummaryDto> Sessions);
