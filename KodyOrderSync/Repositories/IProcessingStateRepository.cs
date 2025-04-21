using KodyOrderSync.Models;

namespace KodyOrderSync.Repositories;

public interface IProcessingStateRepository
{
    Task AddProcessedOrderAsync(OrderProcessingState state, CancellationToken cancellationToken);

    Task<OrderProcessingState> GetOrderStateByKodyIdAsync(string kodyOrderId, CancellationToken cancellationToken);

    Task SetLastStatusSentAsync(string kodyOrderId, string status, CancellationToken cancellationToken);

    // You might still need this for the OrderSyncWorker's initial pull logic
    Task<DateTime?> GetLastProcessedOrderTimestampAsync(CancellationToken cancellationToken);
}