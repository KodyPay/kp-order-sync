namespace KodyOrderSync.Repositories;

public class PosOrderStatusInfo
{
    // Gicater's internal primary key for the order
    public int GicaterOrderHeadId { get; init; }

    // The KodyOrder ID stored when inserting the order (ASSUMED COLUMN)
    public string KodyOrderId { get; init; }

    // The status value from Gicater's 'status' column (e.g., 1 = Paid/Closed)
    // Or potentially derived based on is_make, order_end_time etc.
    public int? GicaterStatus { get; init; }

    // Gicater's is_make flag (1 = cooking complete)
    public int IsMake { get; set; }

    // Timestamp when order was marked as ended/paid in Gicater
    public DateTime? GicaterOrderEndTime { get; set; }
}