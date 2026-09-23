using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IfredrixDownloadManager;

/// <summary>
/// Shared token-bucket used to cap the total bandwidth of every HTTP connection.
/// Limit is expressed in bytes per second; 0 means "unlimited".
/// </summary>
public sealed class RateLimiter
{
    private long _limitBps;
    private double _tokens;
    private long _lastTicks = Stopwatch.GetTimestamp();
    private readonly object _sync = new();

    public long LimitBps
    {
        get => Interlocked.Read(ref _limitBps);
        set
        {
            lock (_sync)
            {
                Interlocked.Exchange(ref _limitBps, value < 0 ? 0 : value);
            }
        }
    }

    /// <summary>Blocks until the bucket can absorb <paramref name="bytes"/>.</summary>
    public async Task ThrottleAsync(long bytes, CancellationToken ct)
    {
        if (bytes <= 0) return;
        var limit = Interlocked.Read(ref _limitBps);
        if (limit <= 0) return;

        while (!ct.IsCancellationRequested)
        {
            int waitMs;
            lock (_sync)
            {
                Refill(limit);
                if (_tokens >= bytes)
                {
                    _tokens -= bytes;
                    return;
                }

                var deficit = bytes - _tokens;
                waitMs = (int)Math.Ceiling(deficit * 1000.0 / limit);
                if (waitMs < 4) waitMs = 4;
                if (waitMs > 400) waitMs = 400;
            }

            await Task.Delay(waitMs, ct).ConfigureAwait(false);
        }
    }

    private void Refill(long limit)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;
        // Never bank more than one second worth of tokens.
        _tokens = Math.Min(limit, _tokens + elapsed * limit);
    }
}

/// <summary>
/// Per-download throttles stacked on top of the global limiter.
/// 0 means "no per-download cap". Entries are dropped on completion.
/// </summary>
internal static class TaskLimits
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RateLimiter> _limiters = new();

    public static async Task ThrottleAsync(string taskId, long limitBps, long bytes, CancellationToken ct)
    {
        if (limitBps <= 0 || bytes <= 0) return;
        var limiter = _limiters.GetOrAdd(taskId, _ => new RateLimiter());
        limiter.LimitBps = limitBps;
        await limiter.ThrottleAsync(bytes, ct).ConfigureAwait(false);
    }

    public static void Drop(string taskId) => _limiters.TryRemove(taskId, out _);
}
