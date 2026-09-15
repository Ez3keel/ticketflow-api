using TicketFlow.Application.Reservations;
using TicketFlow.Infrastructure;
using TicketFlow.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<OrderProcessingService>();
builder.Services.AddHostedService<OrderConfirmationConsumer>();

var host = builder.Build();
host.Run();
