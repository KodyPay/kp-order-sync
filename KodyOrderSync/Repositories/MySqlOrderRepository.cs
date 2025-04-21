using System.Collections.Concurrent;
using Com.Kodypay.Grpc.Ordering.V1;

using Microsoft.Extensions.Options; // For settings
using Microsoft.Extensions.Logging;
using MySqlConnector; // For logging

namespace KodyOrderSync.Repositories;
public class MySqlOrderRepository : IOrderRepository
{
    private readonly string _connectionString;
    private readonly ILogger<MySqlOrderRepository> _logger;

    public MySqlOrderRepository(IOptions<OrderSyncSettings> settings, ILogger<MySqlOrderRepository> logger)
    {
        _connectionString = settings.Value.PosDbConnectionString
            ?? throw new ArgumentNullException(nameof(settings), "PosDbConnectionString is required");
        _logger = logger;

        _logger.LogInformation("Created MySqlOrderRepository with connection to {Database}",
            new MySqlConnectionStringBuilder(_connectionString).Database);
    }

    public async Task<string> SaveOrderAsync(Order order, CancellationToken cancellationToken)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Insert order header
                var (headerSql, headerParams) = OrderMapper.MapOrderToInsertSql(order);
                int orderHeadId;

                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = headerSql;

                    foreach (var param in headerParams)
                        cmd.Parameters.AddWithValue(param.Key, param.Value);

                    // Get the auto-generated order_head_id
                    orderHeadId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                }
                
                // Get unique menu item IDs from order items
                var menuItemIds = new HashSet<int>();
                foreach (var orderItemCombo in order.Items)
                {
                    if (orderItemCombo.Item != null && int.TryParse(orderItemCombo.Item.IntegrationId, out int menuId))
                    {
                        menuItemIds.Add(menuId);
                    }
                }

                // Fetch menu item names from menu_item table
                var menuItems = new Dictionary<int, string>();
                if (menuItemIds.Any())
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT item_id, item_name1 FROM menu_item WHERE item_id IN (" + 
                                      string.Join(",", menuItemIds) + ")";
                
                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        int itemId = reader.GetInt32("item_id");
                        string itemName = reader.GetString("item_name1");
                        menuItems[itemId] = itemName;
                    }
                }

                // Insert order items
                var (detailSql, detailParamsList) = OrderMapper.MapOrderItemsToInsertSql(order, orderHeadId);

                foreach (var itemParams in detailParamsList)
                {
                    // Update menu item name if available
                    if (itemParams["@menuItemId"] is int menuItemId && menuItems.TryGetValue(menuItemId, out var name))
                    {
                        itemParams["@menuItemName"] = name;
                    }
                    
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = detailSql;

                    foreach (var param in itemParams)
                        cmd.Parameters.AddWithValue(param.Key, param.Value);

                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Saved KodyOrder ID {KodyOrderId} to MySQL with POS ID {PosOrderId}",
                    order.OrderId, orderHeadId);

                return orderHeadId.ToString();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to save order {OrderId} to MySQL", order.OrderId);
                throw;
            }
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "Database error when saving order {OrderId}", order.OrderId);
            throw;
        }
    }

    public async Task<IEnumerable<PosOrderStatusInfo>> GetOrderStatusUpdatesAsync(
        DateTime lookbackStartTime,
        CancellationToken cancellationToken)
    {
        var results = new List<PosOrderStatusInfo>();

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT order_head_id, check_name, status, is_make, order_end_time
                FROM order_head
                WHERE pos_name = 'KODYORDER'
                AND order_start_time >= @lookbackTime
                AND (status >= 1 OR is_make = 1)";

            cmd.Parameters.AddWithValue("@lookbackTime", lookbackStartTime);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(OrderMapper.MapToPosOrderStatusInfo(reader));
            }

            _logger.LogInformation("Retrieved {Count} order status updates since {LookbackTime}",
                results.Count, lookbackStartTime);

            return results;
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "Database error when retrieving order status updates");
            throw;
        }
    }
}