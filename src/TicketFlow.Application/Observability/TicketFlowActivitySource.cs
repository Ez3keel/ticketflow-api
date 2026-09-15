using System.Diagnostics;

namespace TicketFlow.Application.Observability;

// A plain System.Diagnostics.ActivitySource, not an OpenTelemetry type -- Application
// stays free of any tracing vendor. Infrastructure's OpenTelemetry setup "discovers"
// this source by name (AddSource(Name)); if it isn't listening, StartActivity below
// is a cheap no-op.
public static class TicketFlowActivitySource
{
    public const string Name = "TicketFlow";

    public static readonly ActivitySource Instance = new(Name);
}
