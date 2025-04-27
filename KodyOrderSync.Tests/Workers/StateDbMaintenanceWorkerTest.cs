using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KodyOrderSync.Models;
using KodyOrderSync.Workers;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace KodyOrderSync.Tests.Workers
{
    public class StateDbMaintenanceWorkerTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly Mock<ILogger<StateDbMaintenanceWorker>> _loggerMock;
        private readonly Mock<IOptions<OrderSyncSettings>> _optionsMock;
        private readonly OrderSyncSettings _settings;
        
        public StateDbMaintenanceWorkerTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"test_statedb_{Guid.NewGuid()}.db");
            _loggerMock = new Mock<ILogger<StateDbMaintenanceWorker>>();
            _optionsMock = new Mock<IOptions<OrderSyncSettings>>();
            
            _settings = new OrderSyncSettings
            {
                StateDbPath = _testDbPath,
                StateDbRetentionDays = 7,
                StateDbMaintenanceIntervalHours = 1
            };
            
            _optionsMock.Setup(o => o.Value).Returns(_settings);
        }
        
        [Fact]
        public async Task ExecuteAsync_ShouldDeleteOldRecords()
        {
            // Arrange
            SeedTestDatabase();
            
            var worker = new StateDbMaintenanceWorker(_loggerMock.Object, _optionsMock.Object);
            
            // Act
            // Use a cancellation token that cancels after a short delay
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await worker.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(3)); // Give it time to execute
            await worker.StopAsync(CancellationToken.None);
            
            // Assert
            var recordCount = CountRecordsInDb();
            Assert.Equal(5, recordCount); // Only recent records should remain
        }
        
        private void SeedTestDatabase()
        {
            using var db = new LiteDatabase(_testDbPath);
            var collection = db.GetCollection<OrderProcessingState>("order_processing_state");
            collection.EnsureIndex(x => x.LastUpdatedInStateDb);
            
            // Add 5 old records that should be deleted
            var cutoffDate = DateTime.UtcNow.AddDays(-10);
            for (int i = 0; i < 5; i++)
            {
                collection.Insert(new OrderProcessingState
                {
                    KodyOrderId = $"old-{i}",
                    HashedKodyOrderId = IdHasher.HashOrderId($"old-{i}"),
                    LastUpdatedInStateDb = cutoffDate.AddDays(-i)
                });
            }
            
            // Add 5 recent records that should be kept
            var recentDate = DateTime.UtcNow.AddDays(-1);
            for (int i = 0; i < 5; i++)
            {
                collection.Insert(new OrderProcessingState
                {
                    KodyOrderId = $"recent-{i}",
                    HashedKodyOrderId = IdHasher.HashOrderId($"recent-{i}"),
                    LastUpdatedInStateDb = recentDate.AddHours(-i)
                });
            }
        }
        
        private int CountRecordsInDb()
        {
            using var db = new LiteDatabase(_testDbPath);
            var collection = db.GetCollection<OrderProcessingState>("order_processing_state");
            return collection.Count();
        }
        
        public void Dispose()
        {
            // Clean up the test database
            if (File.Exists(_testDbPath))
            {
                try
                {
                    File.Delete(_testDbPath);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }
}