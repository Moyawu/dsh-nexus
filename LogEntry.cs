namespace DshTrayLauncher;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Message { get; init; } = string.Empty;
    public bool IsError { get; init; }

    public string FormattedLine =>
        $"[{Timestamp:HH:mm:ss}] {(IsError ? "[ERR] " : string.Empty)}{Message}";
}
