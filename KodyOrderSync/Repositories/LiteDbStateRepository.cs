using System.Linq.Expressions;
using KodyOrderSync.Models;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KodyOrderSync.Repositories;

public class LiteDbStateRepository : IProcessingStateRepository, IDisposable
{
    private readonly ILogger<LiteDbStateRepository> _logger;
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<OrderProcessingState> _collection;
    private const string CollectionName = "order_processing_state"; // Consistent name

    public LiteDbStateRepository(IOptions<OrderSyncSettings> settings, ILogger<LiteDbStateRepository> logger)
    {
        _logger = logger;
        var dbPath = settings.Value.StateDbPath;
        if (string.IsNullOrEmpty(dbPath))
        {
            throw new ArgumentNullException(nameof(settings.Value.StateDbPath), "StateDbPath cannot be null or empty.");
        }

        try
        {
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
                _logger.LogInformation("Created LiteDB directory: {DirectoryPath}", dbDir);
            }

            // Consider Connection=Shared mode if multiple workers access concurrently frequently,
            // though separate connections might be fine for less frequent access.
            _db = new LiteDatabase(dbPath);
            _collection = _db.GetCollection<OrderProcessingState>(CollectionName);

            // Ensure necessary indexes exist
            _collection.EnsureIndex(x => x.KodyOrderId, true); // Unique index is good here
            _collection.EnsureIndex(x => x.HashedKodyOrderId, true); // Unique index is good here
            _collection.EnsureIndex(x => x.LastUpdatedInStateDb); // Needed for efficient deletion

            _logger.LogInformation("LiteDB State Repository initialized at {DbPath}", dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize LiteDB database at {DbPath}", dbPath);
            throw;
        }
    }

    public Task AddProcessedOrderAsync(OrderProcessingState state, CancellationToken cancellationToken)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (string.IsNullOrEmpty(state.KodyOrderId))
            throw new ArgumentException("KodyOrderId cannot be null or empty.", nameof(state));

        // Ensure timestamp is set on add
        state.LastUpdatedInStateDb = DateTime.UtcNow;
        try
        {
            _logger.LogInformation("Adding processed order: {OrderId}", state.KodyOrderId);
            _collection.Insert(state);
        }
        catch (LiteException lex) when (lex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
        {
            _logger.LogWarning("Attempted to add duplicate KodyOrderId {KodyOrderId} to state DB. Ignoring.",
                state.KodyOrderId);
            // Ignore duplicates based on the unique index - means it was likely already processed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert state for KodyOrderId {KodyOrderId}", state.KodyOrderId);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<OrderProcessingState> GetOrderStateByKodyIdAsync(string kodyOrderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(kodyOrderId)) return Task.FromResult<OrderProcessingState>(null);

        _logger.LogInformation("Retrieving order state for KodyOrderId {KodyOrderId} from LiteDB.", kodyOrderId);
        var state = _collection.FindOne(s => s.KodyOrderId == kodyOrderId);
        return Task.FromResult(state);
    }

    public Task SetLastStatusSentAsync(string kodyOrderId, string status, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(kodyOrderId))
        {
            _logger.LogWarning("SetLastStatusSentAsync called with null or empty kodyOrderId.");
            return Task.CompletedTask; // Or throw ArgumentNullException
        }

        try
        {
            // Define the predicate (filter) expression
            Expression<Func<OrderProcessingState, bool>> predicate =
                s => s.KodyOrderId == kodyOrderId;

            // Define the extend expression (how to modify the document)
            // This creates a *new* OrderProcessingState object based on the existing one 's',
            // overriding only the fields we want to change.
            // It MUST be an expression body, not a statement block {}.
            Expression<Func<OrderProcessingState, OrderProcessingState>> extend =
                s => new OrderProcessingState
                {
                    // --- Copy existing fields ---
                    Id = s.Id, // Preserve the original ObjectId
                    KodyOrderId = s.KodyOrderId, // Preserve the KodyOrderId
                    HashedKodyOrderId = s.HashedKodyOrderId, // Preserve the hashed KodyOrderId
                    PosOrderId = s.PosOrderId, // Preserve the PosOrderId
                    OrderPulledTimestamp = s.OrderPulledTimestamp, // Preserve the original pull time

                    // --- Set NEW values for updated fields ---
                    LastStatusSentToKody = status, // Set the new status passed to the method
                    LastUpdatedInStateDb = DateTime.UtcNow // Set the update timestamp to now
                };

            // Execute the update using the correct overload
            var updatedCount = _collection.UpdateMany(extend, predicate);

            if (updatedCount == 0)
            {
                _logger.LogWarning(
                    "Attempted to update status via SetLastStatusSentAsync for non-existent KodyOrderId {KodyOrderId} in state DB.",
                    kodyOrderId);
            }
            else
            {
                _logger.LogDebug(
                    "Updated status state using extend expression for KodyOrderId {KodyOrderId}. {Count} documents affected.",
                    kodyOrderId, updatedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status state using extend expression for KodyOrderId {KodyOrderId}",
                kodyOrderId);
            throw; // Rethrow to signal failure
        }

        return Task.CompletedTask;
    }

    public Task<DateTime?> GetLastProcessedOrderTimestampAsync(CancellationToken cancellationToken)
    {
        // Example: Find the latest OrderPulledTimestamp. Adjust logic as needed.
        var latest = _collection.Query()
            .OrderByDescending(s => s.OrderPulledTimestamp)
            .FirstOrDefault();
        return Task.FromResult(latest?.OrderPulledTimestamp);
    }


    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}