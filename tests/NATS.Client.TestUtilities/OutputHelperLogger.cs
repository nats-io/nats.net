using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace NATS.Client.Core.Tests;

public class OutputHelperLoggerFactory : ILoggerFactory
{
    private readonly ITestOutputHelper _testOutputHelper;

    public OutputHelperLoggerFactory(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(_testOutputHelper);
    }

    public void Dispose()
    {
    }

    private class Logger : ILogger
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public Logger(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
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
                _testOutputHelper.WriteLine($"[NCLOG] {DateTime.Now:HH:mm:ss.fff} {logLevel}: {formatter(state, exception)}");
                if (exception != null)
                {
                    _testOutputHelper.WriteLine($"[NCLOG] {DateTime.Now:HH:mm:ss.fff} Exception: {exception}");
                }
            }
            catch
            {
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
