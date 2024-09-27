using Microsoft.Extensions.Logging;

namespace NATS.Client.TestUtilities;

public class InMemoryTestLoggerFactory(LogLevel level, Action<InMemoryTestLoggerFactory.LogMessage>? logger = null) : ILoggerFactory
{
    private readonly List<LogMessage> _messages = new();

    public IReadOnlyList<LogMessage> Logs
    {
        get
        {
            lock (_messages)
                return _messages.ToList();
        }
    }

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, level, this);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private void Log(string categoryName, LogLevel logLevel, EventId eventId, Exception? exception, string message)
    {
        lock (_messages)
        {
            var logMessage = new LogMessage(categoryName, logLevel, eventId, exception, message);
            _messages.Add(logMessage);
            logger?.Invoke(logMessage);
        }
    }

    public record LogMessage(string Category, LogLevel LogLevel, EventId EventId, Exception? Exception, string Message);

    private class TestLogger(string categoryName, LogLevel level, InMemoryTestLoggerFactory logger) : ILogger
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= level)
                logger.Log(categoryName, logLevel, eventId, exception, formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= level;

#if NET8_0_OR_GREATER
#pragma warning disable CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
        public IDisposable BeginScope<TState>(TState state)
#pragma warning restore CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
            where TState : notnull => new NullDisposable();
#else
        public IDisposable BeginScope<TState>(TState state) => new NullDisposable();
#endif

        private class NullDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
