# KodyOrderSync Service

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
<!-- Add other badges later if desired, e.g., build status -->

A .NET Windows Service to synchronise orders between KodyOrder and Point-of-Sale (POS) systems. Initially developed for Gicater POS using MySQL, but designed to be adaptable for other POS providers.

## Overview

`KodyOrderSync` runs as a background service on a POS machine. Its primary functions are:

1.  **Pull New Orders:** Fetches new orders placed via KodyOrder.
2.  **Push to POS:** Inserts these new orders into the local POS database (initially Gicater's MySQL DB).
3.  **Pull Status Updates:** Detects order status changes within the local POS database.
4.  **Push Status Updates:** Sends these status updates back to KodyOrder.

This project is a joint venture between [Kody](https://kody.com) and Gicater, released as open source to serve as a practical example of integrating with the KodyOrder platform and to encourage contributions from other POS providers or developers.

## Features

*   Runs as a persistent Windows Service.
*   Fetches orders from KodyOrder API.
*   Writes orders to a local MySQL database (easily adaptable to other repositories).
*   Tracks processed orders using a local LiteDB state database to prevent duplicates.
*   Monitors local order status changes.
*   Reports status changes back to the KodyOrder API.
*   Configurable via `appsettings.json`.
*   Built with modern .NET (using Worker Service template).

## Target Scenario (Initial)

*   **POS System:** Gicater POS
*   **Local Database:** MySQL
*   **Order Source:** KodyOrder Platform

## Prerequisites

*   .NET SDK (Check the `.csproj` file for the specific version, e.g., .NET 6, 7, or 8)
*   Access to a KodyOrder API endpoint and credentials.
*   Access to the target POS system's local database (e.g., MySQL connection details for Gicater).
*   Windows operating system (for running as a Windows Service).

## Getting Started

1.  **Clone the repository:**
    ```bash
    git clone git@github.com:KodyPay/kp-order-sync.git
    cd kody-order-sync
    ```

2.  **Configure Settings:**
    *   Adjust `appsettings.json` for your environment-specific settings.
    *   **IMPORTANT:** Never commit sensitive information (API keys, passwords) directly into `appsettings.json` in source control. Use user secrets, environment variables, or other secure configuration methods for production.
    *   Edit your configuration file (`appsettings.json`) and fill in the `OrderSyncSettings` section.

3.  **Build the project:**
    ```bash
    dotnet build --configuration Release
    ```

4.  **Publish the project:** (Publish for your target Windows architecture, e.g., win-x64)
    ```bash
    dotnet publish --configuration Release --runtime win-x64 --output ./publish --self-contained false
    # Use --self-contained true if the target machine might not have the correct .NET runtime installed
    ```
## Runtime Dependencies

The service requires the following components to run properly:

- **Runtime:**
    - **.NET 8.0 Runtime** - Required base runtime
    - **ASP.NET Core Runtime 8.0** - Required for Microsoft.Extensions components
- **Database Systems:**
    - **LiteDB:** Used for local state persistence to prevent duplicate order processing
- **External Services:**
    - **KodyOrder API:** Requires network access to the configured endpoint
    - **MySQL:** For the local POS database (Gicater) - requires the MySQL Connector/NET to be installed on the target machine
- **Core Libraries:**
    - **Serilog:** For structured logging
    - **Microsoft.Extensions.Hosting:** For the hosted service implementation
    - **Microsoft.Extensions.DependencyInjection:** For dependency injection container
    - **Microsoft.Extensions.Configuration:** For configuration management
    - **Windows Service components:** For running as a background Windows service

### Version Information

The application displays the following version details at startup:
- **Assembly Version:** The core version number (e.g., 1.0.0.0)
- **Informational Version:** Includes alpha/beta status and Git commit information
- **Compatible POS Version:** Indicates which version of the POS system this release supports
- 
## Installation as a Windows Service

1.  Open **Command Prompt** or **PowerShell as Administrator**.
2.  Navigate to the publish directory (e.g., `cd C:\path\to\KodyOrderSync\publish`).
3.  Create the service (ensure `binPath=` points to your `.exe` and includes the required space after `=`):
    ```powershell
    sc.exe create KodyOrderSyncService binPath= "C:\path\to\KodyOrderSync\publish\KodyOrderSync.exe" DisplayName= "Kody Order Sync Service" start= auto
    ```
4.  Start the service:
    ```powershell
    sc.exe start KodyOrderSyncService
    ```

**To Manage the Service:**

*   Stop: `sc.exe stop KodyOrderSyncService`
*   Delete: `sc.exe delete KodyOrderSyncService` (stop it first)
*   Check Status: `sc.exe query KodyOrderSyncService`
*   Check Logs: Look in the Windows Event Viewer (Application Log) if EventLog logging is configured, or check file logs if configured differently.

## How It Works

The service runs two main background tasks:

*   `OrderSyncWorker`: Periodically polls the KodyOrder API for new orders, checks against the local state database (`LiteDbStateRepository`), and inserts new orders into the POS database (`MySqlOrderRepository`).
*   `OrderStatusUpdateWorker`: Periodically queries the POS database for recent order status changes, checks against the local state database, and pushes qualifying updates back to the KodyOrder API (`KodyOrderClient`).

## Contributing

Contributions are welcome! Please read our [CONTRIBUTING.md](CONTRIBUTING.md) file for details on how to submit pull requests, report issues, and coding standards.

Please also adhere to our [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.