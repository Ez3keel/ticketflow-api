namespace TicketFlow.Application.Common.Interfaces;

public interface IDistributedLockProvider
{
    // Returns null when the resource is already locked by someone else instead of
    // blocking/retrying -- callers decide whether "someone else has it" is a 409 or
    // worth a retry, the lock provider doesn't guess.
    Task<IDistributedLock?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default);
}
