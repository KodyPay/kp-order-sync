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


            // Insert order header
            var (headerSql, headerParams) = OrderMapper.MapOrderToInsertSql(order);
            int orderHeadId;

            // Create a single command for all operations
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            // Execute header SQL
            cmd.CommandText = headerSql;
            foreach (var param in headerParams)
                cmd.Parameters.AddWithValue(param.Key, param.Value);
            orderHeadId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            cmd.Parameters.Clear();

            // Get menu item IDs from order items
            var menuItemIds = new HashSet<int>();
            foreach (var orderItemCombo in order.Items)
            {
                if (orderItemCombo.Item != null && int.TryParse(orderItemCombo.Item.IntegrationId, out int menuId))
                    menuItemIds.Add(menuId);
            }

            // Fetch menu item names
            var menuItems = new Dictionary<int, string>();
            if (menuItemIds.Any())
            {
                cmd.CommandText = "SELECT item_id, item_name1 FROM menu_item WHERE item_id IN (" +
                                  string.Join(",", menuItemIds) + ")";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    int itemId = reader.GetInt32("item_id");
                    string itemName = reader.GetString("item_name1");
                    menuItems[itemId] = itemName;
                }

                cmd.Parameters.Clear();
            }

            // Insert order items
            var (detailSql, detailParamsList) = OrderMapper.MapOrderItemsToInsertSql(order, orderHeadId);
            foreach (var itemParams in detailParamsList)
            {
                if (itemParams["@menuItemId"] is int menuItemId && menuItems.TryGetValue(menuItemId, out var name))
                    itemParams["@menuItemName"] = name;

                cmd.CommandText = detailSql;
                cmd.Parameters.Clear();
                foreach (var param in itemParams)
                    cmd.Parameters.AddWithValue(param.Key, param.Value);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }



            // Insert print record
            string paymentName = "KODYORDER";
            string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            cmd.CommandText = @"
                    INSERT INTO order_detail(
                        order_head_id, check_id, menu_item_id, menu_item_name,
                        product_price, actual_price, order_employee_name,
                        pos_device_id, pos_name, order_time)
                    VALUES(
                        @orderHeadId, 1, -3, @itemName,
                        0, 0, @employeeName,
                        0, @posName, NOW())";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@orderHeadId", orderHeadId);
            cmd.Parameters.AddWithValue("@itemName", $"**{paymentName} {currentTime}**");
            cmd.Parameters.AddWithValue("@employeeName", paymentName);
            cmd.Parameters.AddWithValue("@posName", paymentName);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            
            // Insert payment record in order_detail
            decimal checkAmount = decimal.Parse(order.TotalAmount);
            cmd.CommandText = @"
                    INSERT INTO order_detail(
                        order_head_id, check_id, menu_item_id, menu_item_name,
                        product_price, actual_price, quantity,  
                        order_employee_name, pos_device_id, pos_name, order_time, 
                        discount_id)
                    VALUES(
                        @orderHeadId, 1, -2, @paymentInfo,
                        0, 0, 0,  
                        @employeeName, 0, @posName, NOW(),
                        0)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@orderHeadId", orderHeadId);
            cmd.Parameters.AddWithValue("@paymentInfo", $"{paymentName}:{checkAmount:0.00}");
            cmd.Parameters.AddWithValue("@employeeName", paymentName);
            cmd.Parameters.AddWithValue("@posName", paymentName);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Get inserted ID, find tender_media, and insert payment
            cmd.CommandText = "SELECT LAST_INSERT_ID()";
            cmd.Parameters.Clear();
            long orderDetailId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
            
            cmd.CommandText = "SELECT tender_media_id FROM tender_media WHERE tender_media_name LIKE '%kody%'";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            int tenderMediaId = result != null ? Convert.ToInt32(result) : 0;
            
            cmd.CommandText = @"
                    INSERT INTO payment(
                        order_head_id, check_id, tender_media_id, total,
                        employee_id, payment_time, pos_device_id,
                        order_detail_id)
                    VALUES(
                        @orderHeadId, 1, @tenderMedia, @amount,
                        @employeeId, NOW(), @posDeviceId,
                        @orderDetailId)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@orderHeadId", orderHeadId);
            cmd.Parameters.AddWithValue("@amount", checkAmount);
            cmd.Parameters.AddWithValue("@employeeId", 0);
            cmd.Parameters.AddWithValue("@posDeviceId", 0);
            cmd.Parameters.AddWithValue("@orderDetailId", orderDetailId);
            cmd.Parameters.AddWithValue("@tenderMedia", tenderMediaId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Saved KodyOrder ID {KodyOrderId} to MySQL with POS ID {PosOrderId}",
                order.OrderId, orderHeadId);

            return orderHeadId.ToString();
        }
        catch (Exception ex)
        {
            string errorMessage = ex is MySqlException
                ? "Database error when saving order {OrderId}"
                : "Failed to save order {OrderId} to MySQL";

            _logger.LogError(ex, errorMessage, order.OrderId);
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