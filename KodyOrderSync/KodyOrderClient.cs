using Com.Kodypay.Grpc.Ordering.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KodyOrderSync;

public class KodyOrderClient: IKodyOrderClient
{
    private readonly OrderService.OrderServiceClient _client;
    private readonly string _storeId;
    private readonly Metadata _headers;
    private readonly ILogger<KodyOrderClient> _logger;

    public KodyOrderClient(IOptions<OrderSyncSettings> syncSettings,  ILogger<KodyOrderClient> logger)
    {
        _logger = logger;
        
        var settings = syncSettings.Value;
        
        _logger.LogInformation("Initializing KodyOrderClient with API URL: {ApiUrl}", 
            string.IsNullOrEmpty(settings.KodyOrderApiBaseUrl) ? "(empty)" : settings.KodyOrderApiBaseUrl);
        
        _storeId = settings.KodyStoreId ?? throw new ArgumentNullException(nameof(settings.KodyStoreId),
            "KodyStoreId is required in configuration");
        
        if (string.IsNullOrEmpty(settings.KodyOrderApiBaseUrl))
        {
            throw new ArgumentException("SourceApiUrl is missing in configuration", nameof(syncSettings));
        }
        
        var channel = GrpcChannel.ForAddress(settings.KodyOrderApiBaseUrl);
        _client = new OrderService.OrderServiceClient(channel);
        
        if (string.IsNullOrEmpty(settings.KodyOrderApiKey))
        {
            throw new ArgumentException("SourceApiKey is missing in configuration", nameof(syncSettings));
        }
        
        _headers = new Metadata { { "X-API-KEY", settings.KodyOrderApiKey } };
        _logger.LogInformation("KodyOrderClient initialized successfully");
    }

    public async Task<GetOrdersResponse> GetOrdersAsync(GetOrdersRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Getting orders with request: {Request}", request);
                
            return await _client.GetOrdersAsync(request, _headers, deadline: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders");
            throw;
        }
    }

    public async Task<UpdateOrderStatusResponse> UpdateOrderStatusAsync(string orderId, OrderStatus status, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Updating order status - OrderId: {OrderId}, new status: {Status}", 
                orderId, status);
                
            var request = new UpdateOrderStatusRequest
            {
                StoreId = _storeId,
                OrderId = orderId,
                NewStatus = status
            };
            
            return await _client.UpdateOrderStatusAsync(request, _headers, deadline: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status for OrderId: {OrderId}", orderId);
            throw;
        }
    }
}