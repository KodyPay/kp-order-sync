using LiteDB;

namespace KodyOrderSync.Models;

public class OrderProcessingState
{
    [BsonId]
    public ObjectId Id { get; set; }

    public string KodyOrderId { get; set; }

    // Gicater's internal Order ID (optional, for reference)
    public string PosOrderId { get; set; } // Or int, depending on Gicater's type

    // The last status successfully reported BACK to KodyOrder API
    public string LastStatusSentToKody { get; set; }

    // When the order was first pulled from KodyOrder by OrderSyncWorker
    public DateTime OrderPulledTimestamp { get; set; }

    // Timestamp for tracking state record age for maintenance/deletion
    public DateTime LastUpdatedInStateDb { get; set; }
}