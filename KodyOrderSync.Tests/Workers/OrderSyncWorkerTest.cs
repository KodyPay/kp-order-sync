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

public class OrderSyncWorkerTests : LoggingTestBase
{
    private readonly ILogger<OrderSyncWorker> _logger;
    private readonly Mock<IKodyOrderClient> _kodyClientMock;
    private readonly Mock<IOrderRepository> _posOrderRepoMock;
    private readonly Mock<IProcessingStateRepository> _stateRepoMock;
    private readonly Mock<IOptions<OrderSyncSettings>> _optionsMock;
    private readonly OrderSyncSettings _settings;

    public OrderSyncWorkerTests(ITestOutputHelper output) 
        : base(output, typeof(OrderSyncWorker))
    {
        _logger = CreateLogger<OrderSyncWorker>(); // Create typed logger
        _kodyClientMock = new Mock<IKodyOrderClient>();
        _posOrderRepoMock = new Mock<IOrderRepository>();
        _stateRepoMock = new Mock<IProcessingStateRepository>();
        _optionsMock = new Mock<IOptions<OrderSyncSettings>>();

        _settings = new OrderSyncSettings
        {
            KodyStoreId = "store123",
            OrderPollingIntervalSeconds = 1 // Fast polling for tests
        };
        _optionsMock.Setup(o => o.Value).Returns(_settings);
    }

    [Fact]
    public async Task ExecuteAsync_FetchesOrders_WithCorrectParameters()
    {
        // Arrange
        var lastProcessed = DateTime.UtcNow.AddHours(-1);
        _stateRepoMock.Setup(x => x.GetLastProcessedOrderTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastProcessed);

        _kodyClientMock.Setup(x => x.GetOrdersAsync(It.IsAny<GetOrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetOrdersResponse { Orders = { } });

        var worker = new OrderSyncWorker(
            _logger,
            _kodyClientMock.Object,
            _posOrderRepoMock.Object,
            _stateRepoMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1500); // Allow time for one execution
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _kodyClientMock.Verify(x => x.GetOrdersAsync(
            It.Is<GetOrdersRequest>(req =>
                req.StoreId == _settings.KodyStoreId &&
                req.AfterDate != null &&
                req.StatusIn.Count == 2 && 
                req.StatusIn.Contains(OrderStatus.Pending) && 
                req.StatusIn.Contains(OrderStatus.NewOrder) &&
                req.PageSize == 100),
            It.IsAny<CancellationToken>()
        ), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesNewOrders_SavesThemCorrectly()
    {
        // Arrange
        var orders = new List<Order>
        {
            new Order { OrderId = "order1" },
            new Order { OrderId = "order2" }
        };

        _stateRepoMock.Setup(x => x.GetLastProcessedOrderTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _kodyClientMock.Setup(x => x.GetOrdersAsync(It.IsAny<GetOrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetOrdersResponse { Orders = { orders } });

        _stateRepoMock.Setup(x => x.GetOrderStateByKodyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderProcessingState?)null);

        _posOrderRepoMock.Setup(x => x.SaveOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("pos123");

        var worker = new OrderSyncWorker(
            _logger,
            _kodyClientMock.Object,
            _posOrderRepoMock.Object,
            _stateRepoMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StartAsync(cts.Token);
        await Task.Delay(500); // Allow time for one execution
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _posOrderRepoMock.Verify(x => x.SaveOrderAsync(
            It.Is<Order>(o => o.OrderId == "order1"),
            It.IsAny<CancellationToken>()
        ), Times.Once);

        _posOrderRepoMock.Verify(x => x.SaveOrderAsync(
            It.Is<Order>(o => o.OrderId == "order2"),
            It.IsAny<CancellationToken>()
        ), Times.Once);

        _stateRepoMock.Verify(x => x.AddProcessedOrderAsync(
            It.Is<OrderProcessingState>(s =>
                s.KodyOrderId == "order1" &&
                s.PosOrderId == "pos123"),
            It.IsAny<CancellationToken>()
        ), Times.Once);

        _stateRepoMock.Verify(x => x.AddProcessedOrderAsync(
            It.Is<OrderProcessingState>(s =>
                s.KodyOrderId == "order2" &&
                s.PosOrderId == "pos123"),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsAlreadyProcessedOrders()
    {
        // Arrange
        var orders = new List<Order>
        {
            new Order { OrderId = "order1" }
        };

        _stateRepoMock.Setup(x => x.GetLastProcessedOrderTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _kodyClientMock.Setup(x => x.GetOrdersAsync(It.IsAny<GetOrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetOrdersResponse { Orders = { orders } });

        _stateRepoMock.Setup(x => x.GetOrderStateByKodyIdAsync("order1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderProcessingState { KodyOrderId = "order1", HashedKodyOrderId =  IdHasher.HashOrderId("order1") });

        var worker = new OrderSyncWorker(
            _logger,
            _kodyClientMock.Object,
            _posOrderRepoMock.Object,
            _stateRepoMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StartAsync(cts.Token);
        await Task.Delay(1500);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _posOrderRepoMock.Verify(x => x.SaveOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        _stateRepoMock.Verify(
            x => x.AddProcessedOrderAsync(It.IsAny<OrderProcessingState>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesExceptions_ContinuesExecution()
    {
        // Arrange
        var orders = new List<Order>
        {
            new Order { OrderId = "order1" },
            new Order { OrderId = "order2" }
        };

        _stateRepoMock.Setup(x => x.GetLastProcessedOrderTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        _kodyClientMock.Setup(x => x.GetOrdersAsync(It.IsAny<GetOrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetOrdersResponse { Orders = { orders } });

        _stateRepoMock.Setup(x => x.GetOrderStateByKodyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderProcessingState?)null);

        // Make the first order fail, but the second succeed
        _posOrderRepoMock.Setup(x =>
                x.SaveOrderAsync(It.Is<Order>(o => o.OrderId == "order1"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test exception"));

        _posOrderRepoMock.Setup(x =>
                x.SaveOrderAsync(It.Is<Order>(o => o.OrderId == "order2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync("pos123");

        var worker = new OrderSyncWorker(
            _logger,
            _kodyClientMock.Object,
            _posOrderRepoMock.Object,
            _stateRepoMock.Object,
            _optionsMock.Object);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(CancellationToken.None);

        // Assert - first order fails but second succeeds
        _stateRepoMock.Verify(x => x.AddProcessedOrderAsync(
            It.Is<OrderProcessingState>(s => s.KodyOrderId == "order2"),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }
}