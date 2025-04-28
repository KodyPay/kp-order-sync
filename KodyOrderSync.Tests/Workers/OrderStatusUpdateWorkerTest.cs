using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Com.Kodypay.Grpc.Ordering.V1;
using KodyOrderSync.Models;
using KodyOrderSync.Repositories;
using KodyOrderSync.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace KodyOrderSync.Tests.Workers;

public class OrderStatusUpdateWorkerTests : LoggingTestBase
{
    private readonly ILogger<OrderStatusUpdateWorker> _logger;
    private readonly Mock<IKodyOrderClient> _kodyClientMock;
    private readonly Mock<IOrderRepository> _orderRepoMock;
    private readonly Mock<IProcessingStateRepository> _stateRepoMock;
    private readonly Mock<IOptions<OrderSyncSettings>> _optionsMock;
    private readonly OrderSyncSettings _settings;

    public OrderStatusUpdateWorkerTests(ITestOutputHelper output) 
        : base(output, typeof(OrderStatusUpdateWorker))
    {
        _logger = CreateLogger<OrderStatusUpdateWorker>();
        _kodyClientMock = new Mock<IKodyOrderClient>();
        _orderRepoMock = new Mock<IOrderRepository>();
        _stateRepoMock = new Mock<IProcessingStateRepository>();
        _optionsMock = new Mock<IOptions<OrderSyncSettings>>();

        _settings = new OrderSyncSettings
        {
            StatusPollingIntervalSeconds = 1, // Fast polling for tests
            HoursToLookBackForStatus = 24
        };
        _optionsMock.Setup(o => o.Value).Returns(_settings);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesStatusUpdates_FromPosDb()
    {
        // Arrange
        var lookbackTime = DateTime.UtcNow.AddHours(-_settings.HoursToLookBackForStatus);
        var posStatusUpdates = new List<PosOrderStatusInfo>
        {
            new() { 
                GicaterOrderHeadId = 1001, 
                HashedKodyOrderId = IdHasher.HashOrderId("kody456") 
            }
        };

        _orderRepoMock.Setup(x => x.GetOrderStatusUpdatesAsync(
                It.Is<DateTime>(d => d <= DateTime.UtcNow && d >= lookbackTime.AddMinutes(-10)), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(posStatusUpdates);

        var processingState = new OrderProcessingState
        {
            KodyOrderId = "kody456",
            HashedKodyOrderId = IdHasher.HashOrderId("kody456"),
            PosOrderId = "pos123",
            LastStatusSentToKody = "Received"
        };

        _stateRepoMock.Setup(x => x.GetOrderStateByHashedKodyIdAsync(
                IdHasher.HashOrderId("kody456"), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(processingState);

        _kodyClientMock.Setup(x => x.UpdateOrderStatusAsync(
                "kody456", 
                OrderStatus.Completed,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateOrderStatusResponse { Success = true });

        var worker = new OrderStatusUpdateWorker(
            _logger,
            _orderRepoMock.Object,
            _stateRepoMock.Object,
            _kodyClientMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.7));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000); // Allow time for one execution
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _orderRepoMock.Verify(x => x.GetOrderStatusUpdatesAsync(
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()
        ), Times.AtLeastOnce);

        _kodyClientMock.Verify(x => x.UpdateOrderStatusAsync(
            "kody456",
            OrderStatus.Completed,
            It.IsAny<CancellationToken>()
        ), Times.Once);

        _stateRepoMock.Verify(x => x.SetLastStatusSentAsync(
            "kody456",
            "CompletedByPOS",
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsUpdates_ForMissingStateRecords()
    {
        // Arrange
        var posStatusUpdates = new List<PosOrderStatusInfo>
        {
            new() { 
                GicaterOrderHeadId = 1001, 
                HashedKodyOrderId = IdHasher.HashOrderId("kody456") 
            }
        };

        _orderRepoMock.Setup(x => x.GetOrderStatusUpdatesAsync(
                It.IsAny<DateTime>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(posStatusUpdates);

        // Return null to simulate no matching record in the state DB
        _stateRepoMock.Setup(x => x.GetOrderStateByHashedKodyIdAsync(
                It.IsAny<string>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderProcessingState)null);

        var worker = new OrderStatusUpdateWorker(
            _logger,
            _orderRepoMock.Object,
            _stateRepoMock.Object,
            _kodyClientMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.7));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000); // Allow time for one execution
        await worker.StopAsync(CancellationToken.None);

        // Assert - should not try to update Kody
        _kodyClientMock.Verify(x => x.UpdateOrderStatusAsync(
            It.IsAny<string>(),
            It.IsAny<OrderStatus>(),
            It.IsAny<CancellationToken>()
        ), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsUpdates_ForAlreadyCompletedOrders()
    {
        // Arrange
        var posStatusUpdates = new List<PosOrderStatusInfo>
        {
            new() { 
                GicaterOrderHeadId = 1001, 
                HashedKodyOrderId = IdHasher.HashOrderId("kody456") 
            }
        };

        _orderRepoMock.Setup(x => x.GetOrderStatusUpdatesAsync(
                It.IsAny<DateTime>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(posStatusUpdates);

        // Return a state that already has been marked as completed
        var processingState = new OrderProcessingState
        {
            KodyOrderId = "kody456",
            HashedKodyOrderId = IdHasher.HashOrderId("kody456"),
            PosOrderId = "pos123",
            LastStatusSentToKody = "CompletedByPOS"  // Already completed
        };

        _stateRepoMock.Setup(x => x.GetOrderStateByHashedKodyIdAsync(
                It.IsAny<string>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(processingState);

        var worker = new OrderStatusUpdateWorker(
            _logger,
            _orderRepoMock.Object,
            _stateRepoMock.Object,
            _kodyClientMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.7));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000); // Allow time for one execution
        await worker.StopAsync(CancellationToken.None);

        // Assert - should not try to update Kody
        _kodyClientMock.Verify(x => x.UpdateOrderStatusAsync(
            It.IsAny<string>(),
            It.IsAny<OrderStatus>(),
            It.IsAny<CancellationToken>()
        ), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesFailedApiCalls()
    {
        // Arrange
        var posStatusUpdates = new List<PosOrderStatusInfo>
        {
            new() { 
                GicaterOrderHeadId = 1001, 
                HashedKodyOrderId = IdHasher.HashOrderId("kody456") 
            }
        };

        _orderRepoMock.Setup(x => x.GetOrderStatusUpdatesAsync(
                It.IsAny<DateTime>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(posStatusUpdates);

        var processingState = new OrderProcessingState
        {
            KodyOrderId = "kody456",
            HashedKodyOrderId = IdHasher.HashOrderId("kody456"),
            PosOrderId = "pos123",
            LastStatusSentToKody = "Received"
        };

        _stateRepoMock.Setup(x => x.GetOrderStateByHashedKodyIdAsync(
                IdHasher.HashOrderId("kody456"), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(processingState);

        // Set up API call to fail
        _kodyClientMock.Setup(x => x.UpdateOrderStatusAsync(
                "kody456", 
                OrderStatus.Completed,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateOrderStatusResponse { Success = false });

        var worker = new OrderStatusUpdateWorker(
            _logger,
            _orderRepoMock.Object,
            _stateRepoMock.Object,
            _kodyClientMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.7));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000); // Allow time for one execution
        await worker.StopAsync(CancellationToken.None);

        // Assert - should not update state if API call failed
        _stateRepoMock.Verify(x => x.SetLastStatusSentAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()
        ), Times.Never);
    }

    // [Fact]
    // public async Task ExecuteAsync_HandlesExceptions_ContinuesExecution()
    // {
    //     // Arrange
    //     _orderRepoMock.Setup(x => x.GetOrderStatusUpdatesAsync(
    //             It.IsAny<DateTime>(), 
    //             It.IsAny<CancellationToken>()))
    //         .ThrowsAsync(new Exception("Test exception"))
    //         .ReturnsAsync(new List<PosOrderStatusInfo>()); // Second call returns empty list
    //
    //     var worker = new OrderStatusUpdateWorker(
    //         _logger,
    //         _orderRepoMock.Object,
    //         _stateRepoMock.Object,
    //         _kodyClientMock.Object,
    //         _optionsMock.Object);
    //
    //     // Act
    //     var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    //     await worker.StartAsync(cts.Token);
    //     await Task.Delay(2500); // Allow time for two executions
    //     await worker.StopAsync(CancellationToken.None);
    //
    //     // Assert - should have called the repository at least twice (once throwing, once returning)
    //     _orderRepoMock.Verify(x => x.GetOrderStatusUpdatesAsync(
    //         It.IsAny<DateTime>(),
    //         It.IsAny<CancellationToken>()
    //     ), Times.AtLeast(2));
    // }

    [Fact]
    public async Task ExecuteAsync_SkipsStatusUpdates_ForOrdersWithoutKodyId()
    {
        // Arrange
        var posStatusUpdates = new List<PosOrderStatusInfo>
        {
            new() { 
                GicaterOrderHeadId = 1001, 
                HashedKodyOrderId = "" // Empty Kody ID
            }
        };

        _orderRepoMock.Setup(x => x.GetOrderStatusUpdatesAsync(
                It.IsAny<DateTime>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(posStatusUpdates);

        var worker = new OrderStatusUpdateWorker(
            _logger,
            _orderRepoMock.Object,
            _stateRepoMock.Object,
            _kodyClientMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.7));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000); // Allow time for one execution
        await worker.StopAsync(CancellationToken.None);

        // Assert - should not try to get state for empty ID
        _stateRepoMock.Verify(x => x.GetOrderStateByHashedKodyIdAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()
        ), Times.Never);
    }
}