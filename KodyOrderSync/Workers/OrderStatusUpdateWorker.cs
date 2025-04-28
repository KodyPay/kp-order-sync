using Com.Kodypay.Grpc.Ordering.V1;
using KodyOrderSync.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KodyOrderSync.Workers;

public class OrderStatusUpdateWorker(
    ILogger<OrderStatusUpdateWorker> logger,
    IOrderRepository orderRepo,
    IProcessingStateRepository stateRepo,
    IKodyOrderClient kodyClient,
    IOptions<OrderSyncSettings> settings)
    : BackgroundService
{
    private readonly ILogger<OrderStatusUpdateWorker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IOrderRepository _orderRepo = orderRepo ?? throw new ArgumentNullException(nameof(orderRepo)); // POS DB Repo
    private readonly IProcessingStateRepository _stateRepo = stateRepo ?? throw new ArgumentNullException(nameof(stateRepo)); // LiteDB Repo
    private readonly IKodyOrderClient _kodyClient = kodyClient ?? throw new ArgumentNullException(nameof(kodyClient)); // Kody API Client
    private readonly OrderSyncSettings _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
    
    private const int DefaultStatusPollingIntervalSeconds = 30;
    private const int DefaultLookbackHours = 24;

    // Define the status string expected by KodyOrder API when is_make=1
    private const string KodyStatusForMakeComplete = "CompletedByPOS";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Status Update Worker starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessStatusUpdateCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Status Update Worker cancellation requested.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during status update cycle.");
            }

            var pollingInterval = TimeSpan.FromSeconds(_settings.StatusPollingIntervalSeconds > 0
                ? _settings.StatusPollingIntervalSeconds
                : DefaultStatusPollingIntervalSeconds);

            _logger.LogDebug("Status update cycle complete. Waiting for {PollingInterval}", pollingInterval);
            await Task.Delay(pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Status Update Worker stopped.");
    }
    
     private async Task ProcessStatusUpdateCycleAsync(CancellationToken stoppingToken)
    {
        var lookbackHours = _settings.HoursToLookBackForStatus > 0 ? _settings.HoursToLookBackForStatus : DefaultLookbackHours;
        var lookbackTime = DateTime.UtcNow.AddHours(-lookbackHours);

        _logger.LogInformation("Checking for POS order status updates (is_make=1) since {LookbackTime}", lookbackTime);

        // Get completed orders from POS database
        var completedPosOrders = (await _orderRepo.GetOrderStatusUpdatesAsync(lookbackTime, stoppingToken))?.ToList();
        _logger.LogDebug("Found {OrderCount} potential status updates in POS DB", completedPosOrders?.Count ?? 0);
        if (completedPosOrders == null || !completedPosOrders.Any()) return;

        foreach (var posOrderInfo in completedPosOrders)
        {
            if (stoppingToken.IsCancellationRequested) break;
            await ProcessSingleStatusUpdateAsync(posOrderInfo, stoppingToken);
        }
    }

    private async Task ProcessSingleStatusUpdateAsync(PosOrderStatusInfo posOrderInfo, CancellationToken stoppingToken)
    {
        // Sanity check HashedKodyOrderId
        if (string.IsNullOrEmpty(posOrderInfo.HashedKodyOrderId))
        {
            _logger.LogWarning("Found completed order in POS DB (ID: {PosOrderId}) without a KodyOrder ID. Skipping.",
                posOrderInfo.GicaterOrderHeadId);
            return;
        }

        // Get current state from LiteDB
        var currentState = await _stateRepo.GetOrderStateByHashedKodyIdAsync(posOrderInfo.HashedKodyOrderId, stoppingToken);
        if (currentState == null)
        {
            _logger.LogWarning("Found status update for HashedKodyOrderId ID {HashedKodyOrderId} in POS, but no corresponding state found in local DB. Skipping.",
                posOrderInfo.HashedKodyOrderId);
            return;
        }

        // Check if status update is needed
        if (currentState.LastStatusSentToKody == KodyStatusForMakeComplete)
        {
            _logger.LogDebug("'{Status}' status for KodyOrder ID {KodyOrderId} was already sent previously. Skipping.",
                KodyStatusForMakeComplete, currentState.KodyOrderId);
            return;
        }

        _logger.LogInformation("Detected 'is_make=1' for KodyOrder ID {KodyOrderId}. Previous status: '{OldStatus}'. Sending '{NewStatus}' update.",
            currentState.KodyOrderId,
            currentState.LastStatusSentToKody ?? "N/A",
            KodyStatusForMakeComplete);

        // Send status update to KodyOrder
        var response = await _kodyClient.UpdateOrderStatusAsync(currentState.KodyOrderId, OrderStatus.Completed, stoppingToken);

        if (response.Success)
        {
            await _stateRepo.SetLastStatusSentAsync(currentState.KodyOrderId, KodyStatusForMakeComplete, stoppingToken);
            _logger.LogInformation("Successfully sent '{Status}' status update for KodyOrder ID {KodyOrderId}",
                KodyStatusForMakeComplete, currentState.KodyOrderId);
        }
        else
        {
            _logger.LogError("Failed to send '{Status}' status update for KodyOrder ID {KodyOrderId}",
                KodyStatusForMakeComplete, currentState.KodyOrderId);
        }
    }
}