namespace CatalogEntitlementManager.Models;

public sealed class OperationLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string Level { get; init; } = "INFO";
    public string Category { get; init; } = "System";
    public string Message { get; init; } = string.Empty;

    public string FormattedLine => $"[{Timestamp:HH:mm:ss}] [{Level}] [{Category}] {Message}";
}