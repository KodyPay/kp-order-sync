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

        public StateDbMaintenanceWorker(
            ILogger<StateDbMaintenanceWorker> logger,
            IOptions<OrderSyncSettings> settings)
        {
            _logger = logger;
            _dbPath = settings.Value.StateDbPath;
            _retentionDays = settings.Value.StateDbRetentionDays > 0 ? settings.Value.StateDbRetentionDays : 15;
            _maintenanceInterval = TimeSpan.FromHours(settings.Value.StateDbMaintenanceIntervalHours > 0 ? settings.Value.StateDbMaintenanceIntervalHours : 24);

            if (string.IsNullOrEmpty(_dbPath))
            {
                _logger.LogError("StateDbPath is not configured. State DB Maintenance Worker will NOT run.");
            }
            if (_retentionDays <= 0)
            {
                 _logger.LogWarning("StateDbRetentionDays is configured to {Days}. No state records will be deleted.", _retentionDays);
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

            _logger.LogInformation("State DB Maintenance Worker starting. Retention: {Days} days, Interval: {Interval}", _retentionDays, _maintenanceInterval);

            // Optional: Add an initial delay before the first run
            // await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Running State DB Maintenance...");

                    var cutoffDate = DateTime.UtcNow.AddDays(-_retentionDays);
                    long deletedCount = 0;
                    long initialSize = 0;
                    long finalSize = 0;

                    // Check if the file exists before trying to open/get size
                     FileInfo fileInfo = new FileInfo(_dbPath);
                     if (!fileInfo.Exists)
                     {
                          _logger.LogWarning("State DB file not found at {DbPath}. Skipping maintenance cycle.", _dbPath);
                          goto WaitNextCycle; // Skip to the delay
                     }
                     initialSize = fileInfo.Length;


                    // Use a connection string or shared mode if needed, but exclusive access might be okay for infrequent maintenance.
                    // Ensure connection closes properly even on error using 'using'.
                    using (var db = new LiteDatabase(_dbPath))
                    {
                        var collection = db.GetCollection<OrderProcessingState>(CollectionName);

                        // Ensure index exists for efficient deletion (might be redundant if repo creates it, but safe)
                        collection.EnsureIndex(x => x.LastUpdatedInStateDb);

                        _logger.LogInformation("Deleting state records last updated before {CutoffDate}", cutoffDate);

                        // Perform deletion
                        deletedCount = collection.DeleteMany(s => s.LastUpdatedInStateDb < cutoffDate);

                        _logger.LogInformation("Deleted {Count} old state records.", deletedCount);

                        // Only compact if records were actually deleted
                        if (deletedCount > 0)
                        {
                            _logger.LogInformation("Rebuilding LiteDB to reclaim space...");
                            var rebuildOptions = new RebuildOptions { Password = null /* Add password if DB is encrypted */ };
                            var rebuilt = db.Rebuild(rebuildOptions); // Compact the file
                            fileInfo.Refresh(); // Get updated size after compaction attempt
                            finalSize = fileInfo.Exists ? fileInfo.Length : 0;
                             _logger.LogInformation("LiteDB rebuild attempted (Success={Rebuilt}). Initial size: {InitialSize} bytes, Final size: {FinalSize} bytes", rebuilt, initialSize, finalSize);
                        }
                        else
                        {
                            _logger.LogInformation("No old records found to delete. Compaction skipped.");
                        }
                    } // LiteDatabase connection is disposed here
                }
                catch (OperationCanceledException) { _logger.LogInformation("State DB Maintenance Worker cancellation requested during operation."); break; } // Allow cancellation
                catch (IOException ioex)
                {
                     // Catch file access errors (e.g., file locked by another process)
                      _logger.LogError(ioex, "IO Error during State DB Maintenance. Could the DB file be locked? Path: {DbPath}", _dbPath);
                      // Don't hammer the file system on persistent IO errors - maybe increase delay?
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error during State DB Maintenance.");
                }

                WaitNextCycle:
                try
                {
                    _logger.LogInformation("State DB Maintenance finished. Waiting for {Interval}", _maintenanceInterval);
                    await Task.Delay(_maintenanceInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("State DB Maintenance Worker stopping during delay.");
                    break; // Exit loop if cancelled during delay
                }
            }
            _logger.LogInformation("State DB Maintenance Worker stopped.");
        }
    }