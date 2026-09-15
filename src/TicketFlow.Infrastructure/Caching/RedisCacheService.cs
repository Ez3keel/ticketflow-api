using System.Text.Json;
using StackExchange.Redis;
using TicketFlow.Application.Common.Interfaces;

namespace TicketFlow.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;

    public RedisCacheService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var value = await _redis.GetDatabase().StringGetAsync(key);
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>(value!);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class
        => await _redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value), ttl);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => await _redis.GetDatabase().KeyDeleteAsync(key);
}
