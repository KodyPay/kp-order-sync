using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KodyOrderSync.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using MySql.Data.MySqlClient;
using Xunit;
using Xunit.Abstractions;

namespace KodyOrderSync.Tests.Integration;

public class MySqlOrderRepositoryTests(ITestOutputHelper output) : DatabaseIntegrationTestBase(output)
{
    private MySqlOrderRepository _repository;
    private Mock<IOptions<OrderSyncSettings>> _optionsMock;

    // Override and extend the initialization method
    public override async Task InitializeAsync()
    {
        // First start the container by calling the base implementation
        await base.InitializeAsync();

        // Now ConnectionString should be available
        Assert.NotNull(ConnectionString);

        // Create repository with the now-available connection string
        var settings = new OrderSyncSettings { PosDbConnectionString = ConnectionString };
        _optionsMock = new Mock<IOptions<OrderSyncSettings>>();
        _optionsMock.Setup(o => o.Value).Returns(settings);

        _repository = new MySqlOrderRepository(_optionsMock.Object, CreateLogger<MySqlOrderRepository>());
    }

    [Fact]
    public async Task SaveOrderAsync_ShouldPersistOrder()
    {
        // Arrange
        var order = DatabaseTestHelpers.CreateTestOrder();

        // Act
        var posOrderId = await _repository.SaveOrderAsync(order, CancellationToken.None);

        // Assert
        Assert.False(string.IsNullOrEmpty(posOrderId));

        // Verify the order exists in database
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM order_head WHERE check_name = @orderId AND pos_name = 'KODYORDER'";
        cmd.Parameters.AddWithValue("@orderId", order.OrderId);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetOrderStatusUpdatesAsync_ShouldReturnCompletedOrders()
    {
        // Arrange - Create test data
        var lookbackTime = DateTime.UtcNow.AddHours(-2);
        await DatabaseTestHelpers.SeedCompletedOrdersAsync(ConnectionString, 3);

        // Act
        var results = await _repository.GetOrderStatusUpdatesAsync(
            lookbackTime,
            CancellationToken.None);

        // Assert
        Assert.NotNull(results);
        var resultList = results.ToList();
        Assert.Equal(3, resultList.Count);
        Assert.All(resultList, r => Assert.NotNull(r.KodyOrderId));
        Assert.All(resultList, r => Assert.True(r.IsMake == 1));
    }

    [Fact]
    public async Task GetOrderStatusUpdatesAsync_ShouldRespectLookbackTime()
    {
        // Arrange
        await DatabaseTestHelpers.SeedCompletedOrdersAsync(ConnectionString, 2, hoursAgo: 5,
            startId: 1000); // Older orders
        await DatabaseTestHelpers.SeedCompletedOrdersAsync(ConnectionString, 3, hoursAgo: 2,
            startId: 2000); // Newer orders

        // Act - Only get orders from last 3 hours
        var results = await _repository.GetOrderStatusUpdatesAsync(
            DateTime.UtcNow.AddHours(-3),
            CancellationToken.None);

        // Assert
        Assert.Equal(3, results.Count());
    }
}

