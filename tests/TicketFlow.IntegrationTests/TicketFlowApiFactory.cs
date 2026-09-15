using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace TicketFlow.IntegrationTests;

// Boots the real Api in-process (WebApplicationFactory) against real, disposable
// Postgres/Redis/RabbitMq containers instead of mocks -- the point of Fase 7's
// concurrency test is to prove the actual distributed lock and database behavior,
// which a fake/in-memory repository could never disprove even if it were broken.
public class TicketFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("ticketflow")
        .WithUsername("ticketflow")
        .WithPassword("ticketflow_test")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    // RabbitMqBuilder generates a random username/password by default -- pin them
    // to match what we pass into RabbitMq:UserName/Password below, since ASP.NET
    // Core resolves a controller's whole constructor dependency graph eagerly (even
    // for OrdersController.Reserve, which never touches IOrderQueue itself, just
    // constructing ReservationService pulls in IOrderQueue -> IConnection).
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Layered on top of appsettings.Development.json (higher precedence),
        // pointing every connection string at this test run's own throwaway
        // containers instead of whatever docker-compose happens to have running
        // locally -- the two must never collide.
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                ["RabbitMq:HostName"] = _rabbitMq.Hostname,
                ["RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(),
                ["RabbitMq:UserName"] = "guest",
                ["RabbitMq:Password"] = "guest",
                ["Jwt:Issuer"] = "TicketFlow",
                ["Jwt:Audience"] = "TicketFlow.Client",
                ["Jwt:SigningKey"] = "3glvz3kthqfCpxT9zP279NeAMI8ZYx2jQQrsK6HwI90=",
                ["Jwt:AccessTokenMinutes"] = "15"
            });
        });
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbitMq.StartAsync());

        // Forces the host to actually build now (WebApplicationFactory otherwise
        // builds it lazily on the first client request). Program.cs already runs
        // EF migrations on startup in Development, so this is also the point where
        // that happens -- a failure here means the app itself couldn't start, not
        // that a particular test's assertions were wrong.
        _ = Services;
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }
}
