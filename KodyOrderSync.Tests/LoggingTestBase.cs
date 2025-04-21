using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace KodyOrderSync.Tests;

public abstract class LoggingTestBase
{
    private readonly ILoggerFactory _loggerFactory;

    protected LoggingTestBase(ITestOutputHelper output, Type loggerType)
    {
        _loggerFactory = LoggerFactory
            .Create(builder => builder
                .AddProvider(new XUnitLoggerProvider(output))
                .SetMinimumLevel(LogLevel.Debug));
    }

    protected ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();

    private class XUnitLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _output;
        public XUnitLoggerProvider(ITestOutputHelper output) => _output = output;
        public ILogger CreateLogger(string categoryName) => new XUnitLogger(_output, categoryName);
        public void Dispose() { }
    }

    private class XUnitLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly string _categoryName;

        public XUnitLogger(ITestOutputHelper output, string categoryName)
        {
            _output = output;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) => NullDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            try {
                _output.WriteLine($"[{logLevel}] {_categoryName}: {formatter(state, exception)}");
                if (exception != null) _output.WriteLine(exception.ToString());
            } catch { }
        }

        private class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}