using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbitMq.StartAsync());

        // Environment variables, not ConfigureAppConfiguration: Program.cs's
        // top-level code calls AddInfrastructure(builder.Configuration) -- which
        // reads and captures the connection strings into local variables -- as part
        // of its own linear execution, before WebApplicationFactory gets a chance to
        // layer ConfigureAppConfiguration sources onto the builder. That override
        // would only affect values read *after* it runs, which the connection
        // strings here aren't. Env vars, read by WebApplication.CreateBuilder itself
        // at the very start of Main, are visible in time -- the same double-
        // underscore convention docker-compose.yml already uses for the same reason.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("RabbitMq__HostName", _rabbitMq.Hostname);
        Environment.SetEnvironmentVariable("RabbitMq__Port", _rabbitMq.GetMappedPublicPort(5672).ToString());
        Environment.SetEnvironmentVariable("RabbitMq__UserName", "guest");
        Environment.SetEnvironmentVariable("RabbitMq__Password", "guest");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "TicketFlow");
        Environment.SetEnvironmentVariable("Jwt__Audience", "TicketFlow.Client");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "3glvz3kthqfCpxT9zP279NeAMI8ZYx2jQQrsK6HwI90=");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        // No real OTLP collector in this test run; point it somewhere that will
        // just fail silently on export rather than at whatever a stray
        // docker-compose Jaeger happens to be running on.
        Environment.SetEnvironmentVariable("Otel__OtlpEndpoint", "http://127.0.0.1:1");

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
