using Com.Kodypay.Grpc.Ordering.V1;
using KodyOrderSync.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KodyOrderSync.Workers;

public class OrderStatusUpdateWorker : BackgroundService
    {
        private readonly ILogger<OrderStatusUpdateWorker> _logger;
        private readonly IOrderRepository _orderRepo; // POS DB Repo
        private readonly IProcessingStateRepository _stateRepo; // LiteDB Repo
        private readonly IKodyOrderClient _kodyClient; // Kody API Client
        private readonly OrderSyncSettings _settings;

        // Define the status string expected by KodyOrder API when is_make=1
        private const string KodyStatusForMakeComplete = "CompletedByPOS";

        public OrderStatusUpdateWorker(
            ILogger<OrderStatusUpdateWorker> logger,
            IOrderRepository orderRepo,
            IProcessingStateRepository stateRepo,
            IKodyOrderClient kodyClient,
            IOptions<OrderSyncSettings> settings)
        {
            _logger = logger;
            _orderRepo = orderRepo;
            _stateRepo = stateRepo;
            _kodyClient = kodyClient;
            _settings = settings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Status Update Worker starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var pollingInterval = TimeSpan.FromSeconds(_settings.StatusPollingIntervalSeconds > 0 ? _settings.StatusPollingIntervalSeconds : 30);
                var lookbackHours = _settings.HoursToLookBackForStatus > 0 ? _settings.HoursToLookBackForStatus : 24;
                var lookbackTime = DateTime.UtcNow.AddHours(-lookbackHours);

                _logger.LogInformation("Checking for POS order status updates (is_make=1) since {LookbackTime}", lookbackTime);

                try
                {
                    // 1. Query POS DB (Gicater MySQL) for recently completed orders known to KodyOrder
                    var completedPosOrders = await _orderRepo.GetOrderStatusUpdatesAsync(lookbackTime, stoppingToken);
                    _logger.LogDebug("Found {OrderCount} potential status updates in POS DB.", completedPosOrders?.Count() ?? 0);


                    if (completedPosOrders != null)
                    {
                        // 2. Process each potential update
                        foreach (var posOrderInfo in completedPosOrders)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            // Sanity check KodyOrderId retrieved from POS
                            if (string.IsNullOrEmpty(posOrderInfo.KodyOrderId))
                            {
                                _logger.LogWarning("Found completed order in POS DB (Gicater ID: {GicaterOrderId}) without a KodyOrder ID. Skipping.", posOrderInfo.GicaterOrderHeadId);
                                continue;
                            }

                            // 3. Get the last known state from our local LiteDB
                            var currentState = await _stateRepo.GetOrderStateByKodyIdAsync(posOrderInfo.KodyOrderId, stoppingToken);

                            if (currentState == null)
                            {
                                _logger.LogWarning("Found status update for KodyOrder ID {KodyOrderId} in POS, but no corresponding state found in local DB. Skipping.", posOrderInfo.KodyOrderId);
                                continue;
                            }

                            // 4. Compare POS status (is_make=1 implies KodyStatusForMakeComplete) with the last status we SENT to KodyOrder
                            if (currentState.LastStatusSentToKody != KodyStatusForMakeComplete)
                            {
                                _logger.LogInformation("Detected 'is_make=1' for KodyOrder ID {KodyOrderId}. Previous synced status: '{OldStatus}'. Sending '{NewStatus}' update.",
                                    posOrderInfo.KodyOrderId,
                                    currentState.LastStatusSentToKody ?? "N/A",
                                    KodyStatusForMakeComplete);

                                // 5. Send the specific "CompletedByPOS" update to KodyOrder API
                                var response = await _kodyClient.UpdateOrderStatusAsync(posOrderInfo.KodyOrderId, OrderStatus.Completed, stoppingToken);

                                // 6. If successful, update our local state (LiteDB)
                                if (response.Success)
                                {
                                    // This call updates both the status AND the LastUpdatedInStateDb timestamp internally
                                    await _stateRepo.SetLastStatusSentAsync(posOrderInfo.KodyOrderId, KodyStatusForMakeComplete, stoppingToken);
                                    _logger.LogInformation("Successfully sent '{Status}' status update for KodyOrder ID {KodyOrderId} and updated state DB.", KodyStatusForMakeComplete, posOrderInfo.KodyOrderId);
                                }
                                else
                                {
                                    _logger.LogError("Failed to send '{Status}' status update for KodyOrder ID {KodyOrderId}", KodyStatusForMakeComplete, posOrderInfo.KodyOrderId);
                                    // Consider retry logic or error handling for persistent failures
                                }
                            }
                            else
                            {
                                // Status already sent, log for debugging if needed
                                _logger.LogDebug("'{Status}' status for KodyOrder ID {KodyOrderId} was already sent previously. Skipping.", KodyStatusForMakeComplete, posOrderInfo.KodyOrderId);
                            }
                        } // end foreach
                    }
                }
                catch (OperationCanceledException) { _logger.LogInformation("Status Update Worker cancellation requested."); break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during status update polling cycle.");
                }

                _logger.LogDebug("Status update cycle complete. Waiting for {PollingInterval}", pollingInterval);
                try { await Task.Delay(pollingInterval, stoppingToken); }
                catch (OperationCanceledException) { _logger.LogInformation("Status Update Worker stopping during delay."); break; }

            } // end while

            _logger.LogInformation("Status Update Worker stopped.");
        }
    }