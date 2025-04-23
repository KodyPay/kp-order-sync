using LiteDB;

namespace KodyOrderSync.Models;

public class OrderProcessingState
{
    [BsonId] public ObjectId Id { get; init; } = new();

    public required string KodyOrderId { get; init; }

    // Gicater's internal Order ID (optional, for reference)
    public string? PosOrderId { get; init; }

    // The last status successfully reported BACK to KodyOrder API
    public string? LastStatusSentToKody { get; init; }

    // When the order was first pulled from KodyOrder by OrderSyncWorker
    public DateTime OrderPulledTimestamp { get; init; }

    // Timestamp for tracking state record age for maintenance/deletion
    public DateTime? LastUpdatedInStateDb { get; set; }
}