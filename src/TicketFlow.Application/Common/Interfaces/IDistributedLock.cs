namespace TicketFlow.Application.Common.Interfaces;

/// <summary>
/// A held lock on some shared resource. Releasing it (via DisposeAsync) is the only
/// way to give it up before it naturally expires.
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
}
