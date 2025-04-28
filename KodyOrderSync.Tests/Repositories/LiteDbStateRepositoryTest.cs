using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KodyOrderSync.Models;
using KodyOrderSync.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace KodyOrderSync.Tests.Repositories;

public class LiteDbStateRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly Mock<ILogger<LiteDbStateRepository>> _loggerMock;
    private readonly Mock<IOptions<OrderSyncSettings>> _optionsMock;
    private LiteDbStateRepository _repository;

    public LiteDbStateRepositoryTests()
    {
        // Create a temporary file path for the test database
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid()}.db");

        // Setup mocks
        _loggerMock = new Mock<ILogger<LiteDbStateRepository>>();
        _optionsMock = new Mock<IOptions<OrderSyncSettings>>();
        _optionsMock.Setup(o => o.Value).Returns(new OrderSyncSettings { StateDbPath = _tempDbPath });

        // Create repository instance
        _repository = new LiteDbStateRepository(_optionsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task AddProcessedOrderAsync_ShouldInsertValidState()
    {
        // Arrange
        var state = new OrderProcessingState
        {
            KodyOrderId = "test-order-123",
            HashedKodyOrderId = IdHasher.HashOrderId("test-order-123"),
            PosOrderId = "pos-456",
            LastStatusSentToKody = "Received",
            OrderPulledTimestamp = DateTime.UtcNow.AddMinutes(-10)
        };

        // Act
        await _repository.AddProcessedOrderAsync(state, CancellationToken.None);

        // Assert
        var result = await _repository.GetOrderStateByKodyIdAsync("test-order-123", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("test-order-123", result.KodyOrderId);
        Assert.Equal("pos-456", result.PosOrderId);
        Assert.Equal("Received", result.LastStatusSentToKody);
    }

    [Fact]
    public async Task AddProcessedOrderAsync_ShouldIgnoreDuplicateKodyOrderId()
    {
        // Arrange
        var state1 = new OrderProcessingState
        {
            KodyOrderId = "duplicate-id",
            HashedKodyOrderId = IdHasher.HashOrderId("duplicate-id"),
            PosOrderId = "pos-1",
            LastStatusSentToKody = "Received"
        };

        var state2 = new OrderProcessingState
        {
            KodyOrderId = "duplicate-id",
            HashedKodyOrderId = IdHasher.HashOrderId("duplicate-id"),
            PosOrderId = "pos-2",
            LastStatusSentToKody = "Processing"
        };

        // Act & Assert - should not throw
        await _repository.AddProcessedOrderAsync(state1, CancellationToken.None);
        await _repository.AddProcessedOrderAsync(state2, CancellationToken.None);

        // Verify first one was kept
        var result = await _repository.GetOrderStateByKodyIdAsync("duplicate-id", CancellationToken.None);
        Assert.Equal("pos-1", result.PosOrderId);
    }
    
    [Fact]
    public async Task GetOrderStateByHashedKodyIdAsync_ShouldReturnCorrectState()
    {
        // Arrange
        var orderId = "hashed-test-order";
        var hashedId = IdHasher.HashOrderId(orderId);
        var state = new OrderProcessingState
        {
            KodyOrderId = orderId,
            HashedKodyOrderId = hashedId,
            PosOrderId = "pos-123",
            LastStatusSentToKody = "Pending"
        };
        await _repository.AddProcessedOrderAsync(state, CancellationToken.None);

        // Act
        var result = await _repository.GetOrderStateByHashedKodyIdAsync(hashedId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(orderId, result.KodyOrderId);
        Assert.Equal("pos-123", result.PosOrderId);
    }
    
    [Fact]
    public async Task GetOrderStateByKodyIdAsync_ShouldReturnNullForNonExistentId()
    {
        // Act
        var result = await _repository.GetOrderStateByKodyIdAsync("non-existent", CancellationToken.None);

        // Assert
        Assert.Null(result);
    }
    
    
    [Fact]
    public async Task GetOrderStateByHashedKodyIdAsync_ShouldReturnNullForNonExistentHash()
    {
        // Act
        var result = await _repository.GetOrderStateByHashedKodyIdAsync("non-existent-hash", CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetLastStatusSentAsync_ShouldUpdateExistingRecord()
    {
        // Arrange
        var state = new OrderProcessingState
        {
            KodyOrderId = "update-test",
            HashedKodyOrderId = IdHasher.HashOrderId("update-test"),
            PosOrderId = "pos-789",
            LastStatusSentToKody = "Received",
            OrderPulledTimestamp = DateTime.UtcNow.AddMinutes(-5)
        };
        await _repository.AddProcessedOrderAsync(state, CancellationToken.None);

        // Act
        await _repository.SetLastStatusSentAsync("update-test", "Completed", CancellationToken.None);

        // Assert
        var result = await _repository.GetOrderStateByKodyIdAsync("update-test", CancellationToken.None);
        Assert.Equal("Completed", result.LastStatusSentToKody);
    }

    [Fact]
    public async Task GetLastProcessedOrderTimestampAsync_ShouldReturnLatestTimestamp()
    {
        // Arrange
        var oldTime = DateTime.UtcNow.AddDays(-2);
        var newTime = DateTime.UtcNow.AddDays(-1);

        var state1 = new OrderProcessingState
        {
            KodyOrderId = "old-order",
            HashedKodyOrderId = IdHasher.HashOrderId("old-order"),
            OrderPulledTimestamp = oldTime
        };

        var state2 = new OrderProcessingState
        {
            KodyOrderId = "new-order",
            HashedKodyOrderId = IdHasher.HashOrderId("new-order"),
            OrderPulledTimestamp = newTime
        };

        await _repository.AddProcessedOrderAsync(state1, CancellationToken.None);
        await _repository.AddProcessedOrderAsync(state2, CancellationToken.None);

        // Act
        var result = await _repository.GetLastProcessedOrderTimestampAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var timeDifference = (result.Value.ToUniversalTime() - newTime.ToUniversalTime()).Duration();
        Assert.True(timeDifference < TimeSpan.FromMilliseconds(1), 
            $"Expected time close to {newTime}, but got {result.Value}");
    }

    [Fact]
    public async Task GetLastProcessedOrderTimestampAsync_ShouldReturnNullForEmptyDb()
    {
        // Use a fresh repository to ensure empty DB
        _repository.Dispose();
        var tempPath = Path.Combine(Path.GetTempPath(), $"empty_db_{Guid.NewGuid()}.db");
        _optionsMock.Setup(o => o.Value).Returns(new OrderSyncSettings { StateDbPath = tempPath });
        _repository = new LiteDbStateRepository(_optionsMock.Object, _loggerMock.Object);

        // Act
        var result = await _repository.GetLastProcessedOrderTimestampAsync(CancellationToken.None);

        // Assert
        Assert.Null(result);

        // Cleanup
        _repository.Dispose();
        if (File.Exists(tempPath))
            File.Delete(tempPath);
    }

    public void Dispose()
    {
        _repository?.Dispose();
        if (File.Exists(_tempDbPath))
            File.Delete(_tempDbPath);
    }
}