using Com.Kodypay.Grpc.Ordering.V1;
using KodyOrderSync.Models;

using KodyOrderSync.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KodyOrderSync.Workers;

public class OrderSyncWorker(
    ILogger<OrderSyncWorker> logger,
    IKodyOrderClient kodyClient,
    IOrderRepository posOrderRepo,
    IProcessingStateRepository stateRepo,
    IOptions<OrderSyncSettings> options)
    : BackgroundService
{
    private readonly ILogger<OrderSyncWorker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IKodyOrderClient _kodyClient = kodyClient ?? throw new ArgumentNullException(nameof(kodyClient));
    private readonly IOrderRepository _posOrderRepo = posOrderRepo ?? throw new ArgumentNullException(nameof(posOrderRepo)); // Repository for POS DB (e.g., MySQL)
    private readonly IProcessingStateRepository _stateRepo = stateRepo ?? throw new ArgumentNullException(nameof(stateRepo)); // LiteDB state repo
    private readonly OrderSyncSettings _settings = options.Value ?? throw new ArgumentNullException(nameof(options));
    
    
    private const int DefaultPollingIntervalSeconds = 60;
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order Sync Worker starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOrderSyncCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Order Sync Worker cancellation requested.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during order synchronization cycle.");
            }

            var pollingInterval = TimeSpan.FromSeconds(_settings.OrderPollingIntervalSeconds > 0
                ? _settings.OrderPollingIntervalSeconds
                : DefaultPollingIntervalSeconds);

            _logger.LogDebug("Order sync cycle complete. Waiting for {PollingInterval}", pollingInterval);
            await Task.Delay(pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Order Sync Worker stopped.");
    }
    
    private async Task ProcessOrderSyncCycleAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Checking KodyOrder API for new orders...");

        // Determine the timestamp to query from
        DateTime? lastProcessedTime = await _stateRepo.GetLastProcessedOrderTimestampAsync(stoppingToken);
        _logger.LogDebug("Querying KodyOrder for orders since: {LastProcessedTime}",
            lastProcessedTime?.ToString("o") ?? "Beginning");

        var request = new GetOrdersRequest
        {
            StoreId = _settings.KodyStoreId,
            AfterDate = lastProcessedTime.HasValue
                ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(lastProcessedTime.Value.ToUniversalTime())
                : null,
            PageSize = BatchSize
        };

        var response = await _kodyClient.GetOrdersAsync(request, stoppingToken);

        _logger.LogInformation("Found {OrderCount} new orders from KodyOrder.", response.Orders?.Count ?? 0);

        if (response.Orders != null && response.Orders.Count > 0)
        {
            await ProcessOrdersAsync(response.Orders, stoppingToken);
        }
    }

    private async Task ProcessOrdersAsync(IList<Order> orders, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing {OrderCount} orders from KodyOrder", orders.Count);

        foreach (var kodyOrder in orders)
        {
            if (stoppingToken.IsCancellationRequested) break;
            
            _logger.LogDebug("Processing KodyOrder ID: {KodyOrderId}", kodyOrder.OrderId);

            try
            {
                // Check if already processed
                var existingState = await _stateRepo.GetOrderStateByKodyIdAsync(kodyOrder.OrderId, stoppingToken);
                if (existingState != null)
                {
                    _logger.LogWarning("KodyOrder ID {KodyOrderId} already exists in state DB. Skipping insertion.",
                        kodyOrder.OrderId);
                    continue;
                }

                // Save order to POS database
                string posOrderId = await _posOrderRepo.SaveOrderAsync(kodyOrder, stoppingToken);

                _logger.LogInformation("Saved KodyOrder ID {KodyOrderId} to POS DB with PosOrderId: {PosOrderId}",
                    kodyOrder.OrderId, posOrderId);

                // Create state record
                var initialState = new OrderProcessingState
                {
                    KodyOrderId = kodyOrder.OrderId,
                    PosOrderId = posOrderId,
                    LastStatusSentToKody = null,
                    OrderPulledTimestamp = DateTime.UtcNow,
                };

                await _stateRepo.AddProcessedOrderAsync(initialState, stoppingToken);
                _logger.LogInformation("Successfully processed and saved KodyOrder ID {KodyOrderId} to POS and State DB.",
                    kodyOrder.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process or save KodyOrder ID {KodyOrderId}", kodyOrder.OrderId);
            }
        }
    }
}