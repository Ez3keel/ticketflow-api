using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using TicketFlow.Api.RateLimiting;
using TicketFlow.Application.Auth;
using TicketFlow.Application.Events;
using TicketFlow.Application.Reservations;
using TicketFlow.Infrastructure;
using TicketFlow.Infrastructure.Persistence;
using TicketFlow.Infrastructure.Realtime;
using TicketFlow.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter a valid JWT access token."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<EventCatalogService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddValidatorsFromAssemblyContaining<TicketFlow.Application.Auth.Validators.RegisterRequestValidator>();

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Browsers can't set an Authorization header on the WebSocket handshake
        // SignalR uses, so the client sends the token as a query string parameter
        // instead; this reads it back out for exactly that one path, leaving every
        // other endpoint on the normal Authorization header.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Baseline abuse protection for every endpoint, partitioned by client IP since
    // most of the catalog is publicly readable without a token. Deliberately
    // generous (100 req/min) -- this is a safety net, not the real defense.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 100,
                QueueLimit = 0
            }));

    // The real defense: seat reservation is the one endpoint a bot actually wants
    // to hammer (grab every seat before a human can). Partitioned per user (not
    // IP) since it's already [Authorize]-only, so one account can't just rotate
    // IPs to get more attempts. A token bucket allows a small legitimate burst
    // (reserving a few seats for friends) while capping the sustained rate.
    // AddPolicy (not AddTokenBucketLimiter) is what makes this per-partition-key --
    // the simpler AddXxxLimiter overloads create a single limiter shared by every
    // caller, which would rate-limit the whole endpoint globally instead of per user.
    options.AddPolicy<string>(RateLimitingPolicies.Reserve, httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: UserId(httpContext),
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 5,
                TokensPerPeriod = 5,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // Brute-force / credential-stuffing protection on login and registration,
    // partitioned by IP since there's no authenticated user yet at this point.
    options.AddPolicy<string>(RateLimitingPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 5,
                QueueLimit = 0
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            status = StatusCodes.Status429TooManyRequests,
            title = "TooManyRequests",
            detail = "Rate limit exceeded. Please slow down and try again shortly."
        }, cancellationToken);
    };
});

static string ClientIp(HttpContext httpContext) => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static string UserId(HttpContext httpContext)
    => httpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value ?? ClientIp(httpContext);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Convenient for local development so `docker compose up` + `dotnet run` is
    // enough to get a working database; a real deployment would apply migrations
    // as an explicit release step instead of on every app startup.
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TicketFlowDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseMiddleware<TicketFlow.Api.Middleware.ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
    app.MapScalarApiReference(options =>
    {
        options.Title = "TicketFlow API";
        options.OpenApiRoutePattern = "/openapi/{documentName}.json";
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<TicketFlowHub>("/hubs/ticketflow");

app.Run();

// Exposes the otherwise-internal top-level Program class so
// WebApplicationFactory<Program> can boot this app in-process for integration
// tests (TicketFlow.IntegrationTests).
public partial class Program;
