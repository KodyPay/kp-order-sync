using Com.Kodypay.Grpc.Ordering.V1;

namespace KodyOrderSync;

public interface IKodyOrderClient
{
    Task<GetOrdersResponse> GetOrdersAsync(GetOrdersRequest request, CancellationToken cancellationToken);
    Task<UpdateOrderStatusResponse> UpdateOrderStatusAsync(string orderId, OrderStatus status, CancellationToken cancellationToken);
}