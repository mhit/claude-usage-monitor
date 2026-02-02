namespace ClaudeUsageMonitor.Models;

/// <summary>
/// Claude.ai organization
/// </summary>
public record Organization
{
    public string Uuid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? RateLimitTier { get; init; }
}

/// <summary>
/// Subscription plan information
/// </summary>
public record SubscriptionInfo
{
    public string PlanType { get; init; } = "free";
    public bool IsRaven { get; init; }
    
    public string DisplayName => PlanType switch
    {
        "claude_pro" => "Pro",
        "claude_team" => "Team",
        "claude_max_5x" => "Max 5x",
        "claude_max_20x" => "Max 20x",
        "free" => "Free",
        _ => PlanType
    };
}
