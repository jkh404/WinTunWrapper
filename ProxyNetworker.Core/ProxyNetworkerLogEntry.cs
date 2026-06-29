namespace ProxyNetworker.Core;

public enum ProxyNetworkerLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error
}

public sealed class ProxyNetworkerLogEntry
{
    public ProxyNetworkerLogEntry(
        ProxyNetworkerLogLevel level,
        string message,
        Exception? exception = null,
        DateTimeOffset? timestamp = null)
    {
        Level = level;
        Message = message;
        Exception = exception;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }

    public ProxyNetworkerLogLevel Level { get; }

    public string Message { get; }

    public Exception? Exception { get; }

    public DateTimeOffset Timestamp { get; }
}
