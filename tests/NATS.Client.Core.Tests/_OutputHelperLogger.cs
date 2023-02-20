using Microsoft.Extensions.Logging;
using System;
using Xunit.Abstractions;

namespace NATS.Client.Core.Tests;

public class OutputHelperLoggerFactory : ILoggerFactory
{
    readonly ITestOutputHelper testOutputHelper;

    public OutputHelperLoggerFactory(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(testOutputHelper);
    }

    public void Dispose()
    {
    }

    class Logger : ILogger
    {
        readonly ITestOutputHelper testOutputHelper;

        public Logger(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                testOutputHelper.WriteLine(formatter(state, exception));
                if (exception != null)
                {
                    testOutputHelper.WriteLine(exception.ToString());
                }
            }
            catch { }
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
