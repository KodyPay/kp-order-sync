using Com.Kodypay.Grpc.Ordering.V1;
using KodyOrderSync.Models;

using KodyOrderSync.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KodyOrderSync.Workers;

public class OrderSyncWorker : BackgroundService
{
    private readonly ILogger<OrderSyncWorker> _logger;
    private readonly IKodyOrderClient _kodyClient;
    private readonly IOrderRepository _posOrderRepo; // Repository for POS DB (e.g., MySQL)
    private readonly IProcessingStateRepository _stateRepo; // LiteDB state repo
    private readonly OrderSyncSettings _settings;

    public OrderSyncWorker(
        ILogger<OrderSyncWorker> logger,
        IKodyOrderClient kodyClient,
        IOrderRepository posOrderRepo,
        IProcessingStateRepository stateRepo,
        IOptions<OrderSyncSettings> settings)
    {
        _logger = logger;
        _kodyClient = kodyClient;
        _posOrderRepo = posOrderRepo;
        _stateRepo = stateRepo;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order Sync Worker starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var pollingInterval = TimeSpan.FromSeconds(_settings.OrderPollingIntervalSeconds > 0
                ? _settings.OrderPollingIntervalSeconds
                : 60); // Default 60s

            _logger.LogInformation("Checking KodyOrder API for new orders...");
            try
            {
                // Determine the timestamp to query from (optional, based on Kody API capability)
                DateTime? lastProcessedTime = await _stateRepo.GetLastProcessedOrderTimestampAsync(stoppingToken);
                _logger.LogDebug("Querying KodyOrder for orders since: {LastProcessedTime}", lastProcessedTime?.ToString("o") ?? "Beginning");

                // 1. Fetch new orders from KodyOrder API
                GetOrdersRequest getOrdersRequest = new GetOrdersRequest
                {
                    StoreId = _settings.KodyStoreId,
                    AfterDate = lastProcessedTime.HasValue 
                        ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(lastProcessedTime.Value.ToUniversalTime()) 
                        : null,
                    PageSize = 100
                };
                
                var response =
                    await _kodyClient.GetOrdersAsync( getOrdersRequest, stoppingToken);

                _logger.LogInformation("Found {OrderCount} new orders from KodyOrder.", response?.Orders?.Count ?? 0);

                if (response?.Orders != null)
                {
                    foreach (var kodyOrder in response.Orders)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        _logger.LogDebug("Processing KodyOrder ID: {KodyOrderId}",
                            kodyOrder.OrderId); // Use actual ID property

                        try
                        {
                            // Optional: Check if already processed (belt-and-braces check if API might return duplicates)
                            var existingState =
                                await _stateRepo.GetOrderStateByKodyIdAsync(kodyOrder.OrderId, stoppingToken);
                            if (existingState != null)
                            {
                                _logger.LogWarning(
                                    "KodyOrder ID {KodyOrderId} already exists in state DB. Skipping insertion.",
                                    kodyOrder.OrderId);
                                continue;
                            }

                            // 3. Save the order to the POS Database (e.g., Gicater MySQL)
                            // Ensure SaveOrderAsync saves the kodyOrder.Id into the POS DB's kody_order_id column!
                            // It might return the new PosOrderId if needed.
                            string posOrderId = await _posOrderRepo.SaveOrderAsync(  kodyOrder, stoppingToken);
                            
                            _logger.LogInformation("Saved KodyOrder ID {KodyOrderId} to POS DB with PosOrderId: {PosOrderId}",
                                kodyOrder.OrderId, posOrderId);
                                ;

                            // 4. If POS save was successful, create the initial state record in LiteDB
                            var initialState = new OrderProcessingState
                            {
                                KodyOrderId = kodyOrder.OrderId, // Use actual ID property
                                PosOrderId = posOrderId, // Optional: Store the ID returned by SaveOrderAsync
                                LastStatusSentToKody = null, // Nothing sent back yet
                                OrderPulledTimestamp = DateTime.UtcNow, // When it was pulled now
                                // LastUpdatedInStateDb will be set by AddProcessedOrderAsync
                            };

                            await _stateRepo.AddProcessedOrderAsync(initialState, stoppingToken);
                            _logger.LogInformation(
                                "Successfully processed and saved KodyOrder ID {KodyOrderId} to POS and State DB.",
                                kodyOrder.OrderId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to process or save KodyOrder ID {KodyOrderId}", kodyOrder.OrderId);
                            // Decide if you stop processing others or continue
                        }
                    } // end foreach
                }
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

            _logger.LogDebug("Order sync cycle complete. Waiting for {PollingInterval}", pollingInterval);
            try
            {
                await Task.Delay(pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Order Sync Worker stopping during delay.");
                break;
            }
        } // end while

        _logger.LogInformation("Order Sync Worker stopped.");
    }

    // TODO: Implement mapping logic
    private object MapKodyOrderToPosOrder(Order kodyOrder)
    {
        // Map fields from kodyOrder to the object structure expected by _posOrderRepo.SaveOrderAsync
        // Ensure you include kodyOrder.Id to be saved in the POS kody_order_id column
        // Set pos_name = 'KodyOrder' if needed
        _logger.LogWarning("MapKodyOrderToPosOrder needs implementation!");
        throw new NotImplementedException();
    }

    // Placeholder for the model returned by your Kody Client
    public class KodyOrderModel
    {
        public string Id { get; set; } /* ... other properties ... */
    }
}