using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace KodyOrderSync.Tests.Integration;

public abstract class DatabaseIntegrationTestBase : LoggingTestBase, IAsyncLifetime
{
    private readonly MySqlTestContainer DbContainer;
    protected string ConnectionString;
    protected readonly IServiceProvider ServiceProvider;

    protected DatabaseIntegrationTestBase(ITestOutputHelper output)
        : base(output, typeof(DatabaseIntegrationTestBase))
    {
        // Create the MySQL test container
        DbContainer = new MySqlTestContainer(output, CreateLogger<MySqlTestContainer>());

        // ConnectionString = DbContainer.ConnectionString;

        // Build service provider with required services
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Override this method to configure your test services
    }

    // This runs before each test
    public virtual async Task InitializeAsync()
    {
        // Start the container
        await DbContainer.InitializeAsync();
        ConnectionString = DbContainer.ConnectionString;

        // Apply migrations from project
        string migrationsPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", // Navigate up to solution directory
            "KodyOrderSync.Tests",
            "Migrations");

        await DbContainer.ApplyMigrationsAsync(migrationsPath);
    }

    // This runs after each test
    public virtual async Task DisposeAsync()
    {
        await DbContainer.DisposeAsync();
    }
}