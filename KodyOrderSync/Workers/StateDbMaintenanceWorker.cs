using KodyOrderSync.Models;
using LiteDB;
using LiteDB.Engine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KodyOrderSync.Workers;

public class StateDbMaintenanceWorker : BackgroundService
{
    private readonly ILogger<StateDbMaintenanceWorker> _logger;
    private readonly string _dbPath;
    private readonly int _retentionDays;
    private readonly TimeSpan _maintenanceInterval;
    private const string CollectionName = "order_processing_state"; // Must match repository
    private const int MinimumRecordsForCompaction = 10;
    private const int DefaultMaintenanceIntervalHours = 24;
    private const int DefaultRetentionDays = 15;

    public StateDbMaintenanceWorker(
        ILogger<StateDbMaintenanceWorker> logger,
        IOptions<OrderSyncSettings> settings)
    {
        _logger = logger;
        _dbPath = settings.Value.StateDbPath;
        _retentionDays = settings.Value.StateDbRetentionDays > 0 ? settings.Value.StateDbRetentionDays : DefaultRetentionDays;
        _maintenanceInterval = TimeSpan.FromHours(settings.Value.StateDbMaintenanceIntervalHours > 0
            ? settings.Value.StateDbMaintenanceIntervalHours
            : DefaultMaintenanceIntervalHours);

        if (string.IsNullOrEmpty(_dbPath))
        {
            _logger.LogError("StateDbPath is not configured. State DB Maintenance Worker will NOT run.");
        }

        if (_retentionDays <= 0)
        {
            _logger.LogWarning("StateDbRetentionDays is configured to {Days}. No state records will be deleted.",
                _retentionDays);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not run if not configured or retention is non-positive
        if (string.IsNullOrEmpty(_dbPath) || _retentionDays <= 0)
        {
            _logger.LogWarning("State DB Maintenance Worker is disabled due to configuration.");
            return;
        }

        _logger.LogInformation("State DB Maintenance Worker starting. Retention: {Days} days, Interval: {Interval}",
            _retentionDays, _maintenanceInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunMaintenanceCycleAsync();
                await DelayUntilNextCycleAsync(stoppingToken); 
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("State DB Maintenance Worker cancellation requested during operation.");
                break;
            } // Allow cancellation
            catch (IOException ioex)
            {
                // Catch file access errors (e.g., file locked by another process)
                _logger.LogError(ioex,
                    "IO Error during State DB Maintenance. Could the DB file be locked? Path: {DbPath}", _dbPath);
                await DelayUntilNextCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during State DB Maintenance.");
                await DelayUntilNextCycleAsync(stoppingToken);
            }
        }

        _logger.LogInformation("State DB Maintenance Worker stopped.");
    }
    
    private async Task RunMaintenanceCycleAsync()
    {
        _logger.LogInformation("Running State DB Maintenance...");
        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionDays);
        
        var fileInfo = new FileInfo(_dbPath);
        if (!fileInfo.Exists)
        {
            _logger.LogWarning("State DB file not found at {DbPath}. Skipping maintenance cycle.", _dbPath);
            return;
        }
        
        long initialSize = fileInfo.Length;

        await Task.Run(() =>
        {
            using var db = new LiteDatabase(_dbPath);
            var collection = db.GetCollection<OrderProcessingState>(CollectionName);
            collection.EnsureIndex(x => x.LastUpdatedInStateDb);

            _logger.LogInformation("Deleting state records last updated before {CutoffDate}", cutoffDate);
            long deletedCount = collection.DeleteMany(s => s.LastUpdatedInStateDb < cutoffDate);
            _logger.LogInformation("Deleted {DeletedCount} old state records.", deletedCount);

            if (deletedCount >= MinimumRecordsForCompaction)
            {
                _logger.LogInformation("Rebuilding LiteDB to reclaim space...");
                long rebuildSize = db.Rebuild(new RebuildOptions());

                fileInfo.Refresh();
                long finalSize = fileInfo.Exists ? fileInfo.Length : 0;
                double savedPercent = initialSize > 0 ? (double)(initialSize - finalSize) / initialSize : 0;

                _logger.LogInformation(
                    "LiteDB rebuild completed. Initial: {InitialSize:N0} bytes, Reported: {RebuildSize:N0} bytes, " +
                    "Final: {FinalSize:N0} bytes, Saved: {SavedPercent:P2}",
                    initialSize, rebuildSize, finalSize, savedPercent);
            }
            else
            {
                _logger.LogInformation("Skipping compaction ({DeletedCount} records deleted < {MinimumRecords} threshold)",
                    deletedCount, MinimumRecordsForCompaction);
            }
        });
    }
    
    private async Task DelayUntilNextCycleAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Maintenance finished. Waiting for {Interval}", _maintenanceInterval);
        await Task.Delay(_maintenanceInterval, stoppingToken);
    }
}