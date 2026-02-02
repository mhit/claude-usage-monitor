namespace ClaudeUsageMonitor.Models;

/// <summary>
/// Claude.ai usage data from the API
/// </summary>
public record UsageData
{
    /// <summary>
    /// Usage percentage (0-100) for the 5-hour rolling window
    /// </summary>
    public int Utilization { get; init; }

    /// <summary>
    /// When the usage counter resets (UTC)
    /// </summary>
    public DateTime ResetsAt { get; init; }

    /// <summary>
    /// Time remaining until reset
    /// </summary>
    public TimeSpan TimeUntilReset => ResetsAt > DateTime.UtcNow 
        ? ResetsAt - DateTime.UtcNow 
        : TimeSpan.Zero;

    /// <summary>
    /// Formatted time until reset (e.g., "2h 15m")
    /// </summary>
    public string TimeUntilResetFormatted
    {
        get
        {
            var remaining = TimeUntilReset;
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
            if (remaining.TotalMinutes >= 1)
                return $"{(int)remaining.TotalMinutes}m";
            return "< 1m";
        }
    }

    /// <summary>
    /// Reset time in local timezone
    /// </summary>
    public DateTime ResetsAtLocal => ResetsAt.ToLocalTime();

    /// <summary>
    /// Status level based on utilization
    /// </summary>
    public UsageLevel Level => Utilization switch
    {
        < 50 => UsageLevel.Safe,
        < 80 => UsageLevel.Moderate,
        _ => UsageLevel.Critical
    };

    /// <summary>
    /// When this data was fetched
    /// </summary>
    public DateTime FetchedAt { get; init; } = DateTime.UtcNow;

    public static UsageData Empty => new()
    {
        Utilization = 0,
        ResetsAt = DateTime.UtcNow.AddHours(5)
    };
}

public enum UsageLevel
{
    Safe,       // 0-49%  - Green
    Moderate,   // 50-79% - Orange
    Critical    // 80-100% - Red
}
