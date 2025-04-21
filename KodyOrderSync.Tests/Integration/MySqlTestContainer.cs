using System;
using System.IO;
using System.Linq; // Add this for OrderBy
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;
using Xunit.Abstractions;

namespace KodyOrderSync.Tests.Integration;

public class MySqlTestContainer : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<MySqlTestContainer> _logger;
    private readonly MySqlContainer _container;
    private bool _isInitialized = false;

    // Use these consistent values across the container
    private const string DbName = "testdb";
    private const string DbUsername = "testuser";
    private const string DbPassword = "testpassword";

    public string ConnectionString =>
        $"Server={_container.Hostname};Port={_container.GetMappedPublicPort(3306)};" +
        $"Database={DbName};Uid={DbUsername};Pwd={DbPassword};";

    public MySqlTestContainer(ITestOutputHelper output, ILogger<MySqlTestContainer> logger)
    {
        _output = output;
        _logger = logger;

        // Create MySQL container with specific version
        _container = new MySqlBuilder()
            .WithImage("mysql:8.0")
            .WithName($"integration-test-mysql-{Guid.NewGuid()}")
            .WithPortBinding(3306, true)
            .WithEnvironment("MYSQL_ROOT_PASSWORD", "rootpassword")
            .WithEnvironment("MYSQL_DATABASE", DbName)
            .WithEnvironment("MYSQL_USER", DbUsername)
            .WithEnvironment("MYSQL_PASSWORD", DbPassword)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilPortIsAvailable(3306))
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        _logger.LogInformation("Starting MySQL test container...");
        await _container.StartAsync();
        _logger.LogInformation("MySQL test container started. Connection string: {ConnectionString}",
            ConnectionString.Replace(DbPassword, "***"));

        _isInitialized = true;
    }

    // Rest of the implementation remains the same, but use ConnectionString property for connections
    public async Task ApplyMigrationsAsync(string migrationsDirectory)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Container not initialized. Call InitializeAsync first.");

        _logger.LogInformation("Applying Flyway migrations from {Directory}", migrationsDirectory);

        if (!Directory.Exists(migrationsDirectory))
        {
            _logger.LogError("Migrations directory does not exist: {Directory}", migrationsDirectory);
            throw new DirectoryNotFoundException($"Migrations directory not found: {migrationsDirectory}");
        }

        var files = Directory.GetFiles(migrationsDirectory, "V*__*.sql")
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogWarning("No migration files found in {Directory}", migrationsDirectory);
        }

        _logger.LogInformation("Found {Count} migration files", files.Count);

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            _logger.LogInformation("Applying migration: {File}", fileName);
            string sql = await File.ReadAllTextAsync(file);

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Successfully applied {File}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply migration {File}: {Message}", fileName, ex.Message);
                throw;
            }
        }

        _logger.LogInformation("Migrations applied successfully");
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
        {
            _logger.LogInformation("Stopping and removing MySQL test container...");
            await _container.DisposeAsync();
        }
    }
}