using System.Reflection;

namespace KodyOrderSync;

public static class VersionInfo
{
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    public static string InformationalVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";
    public static string CompatiblePosVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(m => m.Key == "CompatiblePosVersion")?.Value ?? "Unknown";
}