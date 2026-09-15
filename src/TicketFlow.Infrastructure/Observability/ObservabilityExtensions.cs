using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TicketFlow.Application.Observability;

namespace TicketFlow.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    // Returns the builder (not IServiceCollection) so each host can layer its own
    // exporter choices on top -- Api needs ASP.NET Core instrumentation and the
    // Prometheus AspNetCore exporter, Worker (no Kestrel) needs the standalone
    // Prometheus HttpListener exporter instead. Everything both hosts share --
    // the business Meter/ActivitySource, OTLP trace export, resource naming --
    // lives here once.
    public static OpenTelemetryBuilder AddTicketFlowObservability(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddMetrics();
        services.AddSingleton<TicketFlowMetrics>();

        var otlpEndpoint = configuration["Otel:OtlpEndpoint"] ?? "http://localhost:4317";

        return services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddSource(TicketFlowActivitySource.Name)
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddMeter(TicketFlowMetrics.MeterName)
                .AddRuntimeInstrumentation());
    }
}
