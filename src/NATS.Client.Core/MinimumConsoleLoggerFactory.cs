using Microsoft.Extensions.Logging;

namespace NATS.Client.Core;

public class MinimumConsoleLoggerFactory : ILoggerFactory
{
    readonly LogLevel logLevel;

    public MinimumConsoleLoggerFactory(LogLevel logLevel)
    {
        this.logLevel = logLevel;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(logLevel);
    }

    public void Dispose()
    {
    }

    class Logger : ILogger
    {
        readonly LogLevel logLevel;

        public Logger(LogLevel logLevel)
        {
            this.logLevel = logLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return this.logLevel <= logLevel;
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

    class NullDisposable : IDisposable
    {
        public static readonly IDisposable Instance = new NullDisposable();

        NullDisposable()
        {

        }

        public void Dispose()
        {
        }
    }
}
