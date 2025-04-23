using System.Data;
using Com.Kodypay.Grpc.Ordering.V1;

namespace KodyOrderSync.Repositories;

public static class OrderMapper
{
    public static (string sql, Dictionary<string, object> parameters) MapOrderToInsertSql(Order order)
    {
        var parameters = new Dictionary<string, object>
        {
            ["@checkId"] = 1, // Default check ID
            ["@posName"] = "KODYORDER", // Mark source as Kody
            ["@checkName"] = order.OrderId, // Store Kody order ID in check_name
            ["@orderStartTime"] = order.DateCreated.ToDateTime(),
            ["@isMake"] = 0, // Not yet prepared
            ["@shouldAmount"] = decimal.Parse(order.TotalAmount),
            ["@tableId"] = 0, // Default table
            ["@eatType"] = 0, // Default eat type, dining in
            ["@remark"] = order.OrderNotes ?? string.Empty,
            ["@serviceAmount"] = order.ServiceChargeAmount != null ? 
                decimal.Parse(order.ServiceChargeAmount) : 0m
        };

        string sql = @"
            INSERT INTO order_head (
                check_id, pos_name, check_name, order_start_time, 
                should_amount, is_make, table_id, eat_type, remark, 
                service_amount, status
            ) VALUES (
                @checkId, @posName, @checkName, @orderStartTime, 
                @shouldAmount, @isMake, @tableId, @eatType, @remark, 
                @serviceAmount, 0
            );
            SELECT LAST_INSERT_ID();";

        return (sql, parameters);
    }

    public static (string sql, List<Dictionary<string, object>> parametersList) MapOrderItemsToInsertSql(
        Order order, int orderHeadId)
    {
        string sql = @"
            INSERT INTO order_detail (
                order_head_id, check_id, menu_item_id, menu_item_name,
                product_price, quantity, actual_price, sales_amount,
                description, order_time, is_make
            ) VALUES (
                @orderHeadId, @checkId, @menuItemId, @menuItemName,
                @productPrice, @quantity, @actualPrice, @salesAmount,
                @description, @orderTime, 0
            );";

        var parametersList = new List<Dictionary<string, object>>();
        
        foreach (var orderItemCombo in order.Items)
        {
            if (orderItemCombo.Item != null)
            {
                var item = orderItemCombo.Item;
                decimal unitPrice = decimal.Parse(item.UnitPrice);
                decimal quantity = item.Quantity;
                decimal totalPrice = unitPrice * quantity;

                var parameters = new Dictionary<string, object>
                {
                    ["@orderHeadId"] = orderHeadId,
                    ["@checkId"] = 1,
                    ["@menuItemId"] = int.TryParse(item.IntegrationId, out int menuId) ? menuId : 0,
                    ["@menuItemName"] = "not fill yet",
                    ["@productPrice"] = unitPrice,
                    ["@quantity"] = quantity,
                    ["@actualPrice"] = unitPrice,
                    ["@salesAmount"] = totalPrice,
                    ["@description"] = item.ItemNotes ?? string.Empty,
                    ["@orderTime"] = order.DateCreated.ToDateTime()
                };

                parametersList.Add(parameters);
            }
        }

        return (sql, parametersList);
    }

    public static PosOrderStatusInfo MapToPosOrderStatusInfo(IDataReader reader)
    {
        return new PosOrderStatusInfo
        {
            GicaterOrderHeadId = Convert.ToInt32(reader["order_head_id"]),
            KodyOrderId =  reader["check_name"] != DBNull.Value ? reader["check_name"].ToString() : string.Empty,
            GicaterStatus = Convert.ToInt32(reader["status"]),
            IsMake = Convert.ToInt32(reader["is_make"]),
            GicaterOrderEndTime = reader["order_end_time"] != DBNull.Value
                ? Convert.ToDateTime(reader["order_end_time"])
                : null
        };
    }
}