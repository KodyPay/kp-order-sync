using Microsoft.Extensions.Logging;
using Serilog;
using Xunit.Abstractions;

namespace KodyOrderSync.Tests;

public abstract class LoggingTestBase
{
    protected LoggingTestBase(ITestOutputHelper output)
    {
        // Configure Serilog to write to XUnit output
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.TestOutput(output)
            .CreateLogger();
    }

    protected ILogger<T> CreateLogger<T>() => new LoggerFactory()
        .AddSerilog(Log.Logger)
        .CreateLogger<T>();

    // For direct Serilog logger if needed
    protected Serilog.ILogger CreateSerilogLogger<T>() => 
        Log.Logger.ForContext<T>();
}