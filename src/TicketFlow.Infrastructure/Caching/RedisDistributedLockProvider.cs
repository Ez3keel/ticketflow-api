using StackExchange.Redis;
using TicketFlow.Application.Common.Interfaces;

namespace TicketFlow.Infrastructure.Caching;

public class RedisDistributedLockProvider : IDistributedLockProvider
{
    // Checks the token before deleting so a lock never deletes a key it no longer
    // owns -- e.g. this lock expired on its own and a different request already
    // acquired a new one under the same key by the time DisposeAsync runs.
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;

    private readonly IConnectionMultiplexer _redis;

    public RedisDistributedLockProvider(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<IDistributedLock?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");

        var acquired = await db.StringSetAsync(resource, token, expiry, When.NotExists);
        return acquired ? new RedisDistributedLock(db, resource, token) : null;
    }

    private class RedisDistributedLock : IDistributedLock
    {
        private readonly IDatabase _db;
        private readonly string _resource;
        private readonly string _token;

        public RedisDistributedLock(IDatabase db, string resource, string token)
        {
            _db = db;
            _resource = resource;
            _token = token;
        }

        public async ValueTask DisposeAsync()
            => await _db.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { _resource }, new RedisValue[] { _token });
    }
}
