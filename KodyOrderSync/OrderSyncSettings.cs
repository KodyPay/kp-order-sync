namespace KodyOrderSync;

public class OrderSyncSettings
{
    public string? KodyOrderApiBaseUrl { get; init; }
    public string? KodyOrderApiKey { get; init; }
    public string KodyStoreId { get; init; } = string.Empty;
    public string PosDbConnectionString  { get; init; } = string.Empty; // Remember not to use for production!
    public string StateDbPath { get; init; } = "Data/sync_state.db"; // Default path

    // Order Sync Worker settings
    public int OrderPollingIntervalSeconds { get; init; } = 30;

    // Order Status Update Worker settings
    public int StatusPollingIntervalSeconds { get; init; } = 120;
    public int HoursToLookBackForStatus { get; init; } = 24;

    // State DB Maintenance Worker settings
    public int StateDbRetentionDays { get; init; } = 90; // How long to keep state records
    public int StateDbMaintenanceIntervalHours { get; init; } = 24; // How often to run cleanup
}