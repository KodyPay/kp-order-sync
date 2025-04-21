using Com.Kodypay.Grpc.Ordering.V1;

namespace KodyOrderSync.Repositories;

public interface IOrderRepository
{
    // Task SaveOrdersAsync(List<Order> orders, CancellationToken cancellationToken);
    Task<string> SaveOrderAsync(Order order, CancellationToken cancellationToken);
    Task<IEnumerable<PosOrderStatusInfo>> GetOrderStatusUpdatesAsync(
        DateTime lookbackStartTime,
        CancellationToken cancellationToken);
}