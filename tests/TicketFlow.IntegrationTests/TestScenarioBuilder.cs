using System.Net.Http.Json;
using TicketFlow.Application.Auth.Dtos;
using TicketFlow.Application.Events.Dtos;

namespace TicketFlow.IntegrationTests;

public static class TestScenarioBuilder
{
    public static async Task<string> RegisterUserAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid():N}@ticketflow.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "SenhaForte123"));
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return auth!.AccessToken;
    }

    public static async Task<EventSessionDetailDto> CreateSessionWithSeatsAsync(HttpClient client, string accessToken, int seatCount)
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var eventResponse = await client.PostAsJsonAsync("/api/events", new CreateEventRequest("Concurrency Test Event", "x"));
        eventResponse.EnsureSuccessStatusCode();
        var createdEvent = await eventResponse.Content.ReadFromJsonAsync<EventDto>();

        var sessionResponse = await client.PostAsJsonAsync(
            $"/api/events/{createdEvent!.Id}/sessions",
            new AddSessionRequest("Test Venue", DateTime.UtcNow.AddDays(30), 100m));
        sessionResponse.EnsureSuccessStatusCode();
        var eventWithSession = await sessionResponse.Content.ReadFromJsonAsync<EventDto>();
        var sessionId = eventWithSession!.Sessions.Single().Id;

        var seatsResponse = await client.PostAsJsonAsync(
            $"/api/events/sessions/{sessionId}/seats",
            new AddSeatsRequest("A", 1, seatCount));
        seatsResponse.EnsureSuccessStatusCode();

        return (await seatsResponse.Content.ReadFromJsonAsync<EventSessionDetailDto>())!;
    }
}
