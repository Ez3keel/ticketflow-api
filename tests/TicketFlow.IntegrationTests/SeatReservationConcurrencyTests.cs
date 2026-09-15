using System.Net;
using System.Net.Http.Json;
using TicketFlow.Application.Reservations.Dtos;
using Xunit;

namespace TicketFlow.IntegrationTests;

public class SeatReservationConcurrencyTests : IClassFixture<TicketFlowApiFactory>
{
    private readonly TicketFlowApiFactory _factory;

    public SeatReservationConcurrencyTests(TicketFlowApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReserveSeats_ManyConcurrentRequestsForTheSameSeat_OnlyOneSucceeds()
    {
        // Capped at 5: the Fase 6 rate limiter allows 5 reserve attempts per 10s per
        // user, and a real 6th attempt would correctly get 429 rather than 409 --
        // that's the rate limiter doing its job, not the lock, and mixing the two
        // concerns into one assertion would make a failure here ambiguous about
        // which mechanism actually broke.
        const int concurrentRequests = 5;

        using var client = _factory.CreateClient();
        var token = await TestScenarioBuilder.RegisterUserAsync(client);
        var session = await TestScenarioBuilder.CreateSessionWithSeatsAsync(client, token, seatCount: 1);
        var seatId = session.Seats.Single().Id;

        // Every task shares one HttpClient/token (a single user firing rapid
        // duplicate requests -- e.g. a double-click, a retry storm, or a bot) and
        // targets the exact same seat, so at most one of them can legitimately win.
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => client.PostAsJsonAsync("/api/orders/reserve", new ReserveSeatsRequest(session.Id, [seatId])));

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(concurrentRequests - 1, conflictCount);
    }

    [Fact]
    public async Task ReserveSeats_ConcurrentRequestsForDifferentSeats_AllSucceed()
    {
        // Same rate-limit ceiling as the test above.
        const int seatCount = 5;

        using var client = _factory.CreateClient();
        var token = await TestScenarioBuilder.RegisterUserAsync(client);
        var session = await TestScenarioBuilder.CreateSessionWithSeatsAsync(client, token, seatCount);

        // Same session, but each request wants a different seat. If the lock were
        // taken per-session instead of per-seat (see Fase 3's docs on why it isn't),
        // these would serialize and still all succeed -- so this test alone doesn't
        // prove per-seat granularity, only that unrelated purchases never block each
        // other incorrectly. The previous test is what proves the lock is effective.
        var tasks = session.Seats.Select(seat =>
            client.PostAsJsonAsync("/api/orders/reserve", new ReserveSeatsRequest(session.Id, [seat.Id])));

        var responses = await Task.WhenAll(tasks);

        foreach (var r in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
            Console.WriteLine($"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
