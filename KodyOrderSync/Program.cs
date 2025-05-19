using KodyOrderSync;
using KodyOrderSync.Repositories;
using KodyOrderSync.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;

// Create the host builder with proper Windows service configuration
var builder = Host.CreateDefaultBuilder(args)
    .UseContentRoot(WindowsServiceHelpers.IsWindowsService() 
        ? AppContext.BaseDirectory 
        : Directory.GetCurrentDirectory())
    .UseWindowsService(options =>
    {
        options.ServiceName = "KodyOrderSyncService";
    })
    .UseSerilog((hostContext, services, configuration) =>
    {
        // Ensure log directory exists
        var logPath = hostContext.Configuration["Serilog:WriteTo:1:Args:path"] ?? "logs/kodyordersync-.log";
        var logDirectory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        // Use configuration from appsettings.json
        configuration
            .ReadFrom.Configuration(hostContext.Configuration)
            .ReadFrom.Services(services);
    })
    .ConfigureServices((hostContext, services) =>
    {
        // Load configuration from appsettings.json
        var syncSettings = hostContext.Configuration.GetSection("OrderSyncSettings");
        services.Configure<OrderSyncSettings>(syncSettings);
        services.AddSingleton<IKodyOrderClient, KodyOrderClient>();
        services.AddSingleton<IProcessingStateRepository, LiteDbStateRepository>();
        services.AddSingleton<IOrderRepository, MySqlOrderRepository>();
        
        // Register the main worker service
        services.AddHostedService<OrderSyncWorker>();
        services.AddHostedService<OrderStatusUpdateWorker>();
    });
var host = builder.Build();

Log.Information("KodyOrderSync version {Version} starting up", VersionInfo.Version);
Log.Information("Build: {InformationalVersion}", VersionInfo.InformationalVersion);
Log.Information("Compatible with POS version {PosVersion}", VersionInfo.CompatiblePosVersion);
await host.RunAsync();