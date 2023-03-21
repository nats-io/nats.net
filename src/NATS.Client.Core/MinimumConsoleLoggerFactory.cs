using Microsoft.Extensions.Logging;

namespace NATS.Client.Core;

public class MinimumConsoleLoggerFactory : ILoggerFactory
{
    private readonly LogLevel _logLevel;

    public MinimumConsoleLoggerFactory(LogLevel logLevel)
    {
        _logLevel = logLevel;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(_logLevel);
    }

    public void Dispose()
    {
    }

    private class Logger : ILogger
    {
        private readonly LogLevel _logLevel;

        public Logger(LogLevel logLevel)
        {
            _logLevel = logLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logLevel <= logLevel;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Console.WriteLine(formatter(state, exception));
                if (exception != null)
                {
                    Console.WriteLine(exception.ToString());
                }
            }
        }
    }

    private class NullDisposable : IDisposable
    {
        public static readonly IDisposable Instance = new NullDisposable();

        private NullDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
