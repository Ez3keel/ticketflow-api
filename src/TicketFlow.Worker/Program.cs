using OpenTelemetry.Metrics;
using TicketFlow.Application.Reservations;
using TicketFlow.Infrastructure;
using TicketFlow.Infrastructure.Observability;
using TicketFlow.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// No Kestrel/ASP.NET Core here, so the AspNetCore Prometheus exporter (Api's
// choice) isn't an option -- this exporter runs its own minimal HttpListener on
// :9464 (the OTel convention port) instead, serving the same Prometheus text
// format from a plain background process.
builder.Services.AddTicketFlowObservability(builder.Configuration, "TicketFlow.Worker")
    .WithMetrics(metrics => metrics.AddPrometheusHttpListener(options =>
        options.ConfigureHttpListener = (_, listener) => listener.Prefixes.Add("http://localhost:9464/")));

builder.Services.AddScoped<OrderProcessingService>();
builder.Services.AddHostedService<OrderConfirmationConsumer>();

var host = builder.Build();
host.Run();
