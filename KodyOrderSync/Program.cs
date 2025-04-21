using KodyOrderSync;
using KodyOrderSync.Repositories;
using KodyOrderSync.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

// Create the host builder with proper Windows service configuration
var builder = Host.CreateDefaultBuilder(args)
    .UseContentRoot(WindowsServiceHelpers.IsWindowsService() 
        ? AppContext.BaseDirectory 
        : Directory.GetCurrentDirectory())
    .UseWindowsService(options =>
    {
        options.ServiceName = "KodyOrderSyncService";
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


var type = typeof(LiteDB.LiteDatabase);
Console.WriteLine(type.Assembly.GetName().Version);

// Build and run the host
var host = builder.Build();
await host.RunAsync();