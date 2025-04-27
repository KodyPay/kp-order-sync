namespace KodyOrderSync;

public static class IdHasher
{
    public static string HashOrderId(string orderId)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(orderId));
        
        // Convert to Base64 and take first 30 chars (will be unique enough)
        // Replace characters that might cause issues in SQL
        return Convert.ToBase64String(hashBytes)
            .Replace("/", "_")
            .Replace("+", "-")
            .Replace("=", "")
            .Substring(0, Math.Min(30, Convert.ToBase64String(hashBytes).Length));
    }
}