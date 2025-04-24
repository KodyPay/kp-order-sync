using System;
using System.Threading.Tasks;
using Com.Kodypay.Grpc.Ordering.V1;
using Google.Protobuf.WellKnownTypes;
using MySql.Data.MySqlClient;

namespace KodyOrderSync.Tests.Integration;

public static class DatabaseTestHelpers
{
   public static Order CreateTestOrder()
    {
        string orderId = $"to-{Guid.NewGuid().ToString("N").Substring(0, 20)}";

        var order = new Order
        {
            OrderId = orderId,
            StoreId = "test-store-123",
            TotalAmount = "100.00",
            Status = OrderStatus.Unpaid,
            DateCreated = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime())
        };

        // Add optional fields
        order.OrderNotes = "Test order notes";
        order.LocationNumber = "LOC-001";
        order.ServiceChargeAmount = "5.00";

        // Add a test order item
        var orderItem = new Order.Types.OrderItemWithAddOns
        {
            OrderItemId = "item-1",
            MerchantItemId = "menu-item-1",
            Quantity = 2,
            UnitPrice = "10.00",
            IntegrationId = "101"
        };

        var orderItemCombo = new Order.Types.OrderItemOrCombo();
        orderItemCombo.Item = orderItem;

        order.Items.Add(orderItemCombo);

        return order;
    }
    
    public static async Task SeedCompletedOrdersAsync(string connectionString, int count, int hoursAgo = 1, int startId = 1000)
    {
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        for (int i = 1; i <= count; i++)
        {
            string kodyOrderId = $"tk-{Guid.NewGuid().ToString("N").Substring(0, 20)}";
            int posOrderId = startId + i;  // Use startId parameter to avoid conflicts

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO order_head
            (order_head_id, check_id, status, is_make, pos_name, check_name,
             order_start_time, order_end_time, should_amount)
            VALUES
            (@posId, 1, 1, 1, 'KODYORDER', @kodyId,
             @startTime, @endTime, 100.00);";

            cmd.Parameters.AddWithValue("@posId", posOrderId);
            cmd.Parameters.AddWithValue("@kodyId", kodyOrderId);
            cmd.Parameters.AddWithValue("@startTime", DateTime.UtcNow.AddHours(-hoursAgo));
            cmd.Parameters.AddWithValue("@endTime", DateTime.UtcNow.AddHours(-hoursAgo).AddMinutes(30));

            await cmd.ExecuteNonQueryAsync();
        }
    }
    
    public static async Task SeedMenuItemsAsync(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO menu_item (item_id, item_name1) 
            VALUES (@itemId, @itemName)
            ON DUPLICATE KEY UPDATE item_name1 = @itemName";
        
            cmd.Parameters.AddWithValue("@itemId", "101");
            cmd.Parameters.AddWithValue("@itemName", "test item 101");
        
            await cmd.ExecuteNonQueryAsync();
    }
}