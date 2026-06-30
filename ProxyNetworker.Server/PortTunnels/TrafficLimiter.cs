namespace ProxyNetworker.Server.PortTunnels;

internal sealed class TrafficLimiter
{
    private readonly long _bandwidthLimitBytes;
    private readonly long _maxBytesPerSecond;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _totalBytes;
    private long _windowBytes;
    private DateTimeOffset _windowStartedAt = DateTimeOffset.UtcNow;

    public TrafficLimiter(long bandwidthLimitBytes, long maxBytesPerSecond)
    {
        _bandwidthLimitBytes = Math.Max(0, bandwidthLimitBytes);
        _maxBytesPerSecond = Math.Max(0, maxBytesPerSecond);
    }

    public async Task<bool> ConsumeAsync(int byteCount, CancellationToken cancellationToken)
    {
        if (byteCount <= 0)
        {
            return true;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_bandwidthLimitBytes > 0 && _totalBytes + byteCount > _bandwidthLimitBytes)
            {
                return false;
            }

            if (_maxBytesPerSecond > 0)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _windowStartedAt >= TimeSpan.FromSeconds(1))
                {
                    _windowStartedAt = now;
                    _windowBytes = 0;
                }

                var projectedBytes = _windowBytes + byteCount;
                if (projectedBytes > _maxBytesPerSecond)
                {
                    var overage = projectedBytes - _maxBytesPerSecond;
                    var delayMs = Math.Max(1, (int)Math.Ceiling(overage * 1000d / _maxBytesPerSecond));
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    _windowStartedAt = DateTimeOffset.UtcNow;
                    _windowBytes = 0;
                }

                _windowBytes = Math.Min(_maxBytesPerSecond, _windowBytes + byteCount);
            }

            _totalBytes += byteCount;
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }
}
