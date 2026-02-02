using System.Text.Json.Serialization;

namespace ClaudeUsageMonitor.Models;

/// <summary>
/// Response from /api/organizations/{orgId}/usage
/// </summary>
public class UsageApiResponse
{
    [JsonPropertyName("five_hour")]
    public FiveHourUsage? FiveHour { get; set; }
}

public class FiveHourUsage
{
    [JsonPropertyName("utilization")]
    public int Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }
}

/// <summary>
/// Response from /api/bootstrap/{orgId}/statsig
/// </summary>
public class StatsigApiResponse
{
    [JsonPropertyName("user")]
    public StatsigUser? User { get; set; }
}

public class StatsigUser
{
    [JsonPropertyName("custom")]
    public StatsigCustom? Custom { get; set; }
}

public class StatsigCustom
{
    [JsonPropertyName("orgType")]
    public string? OrgType { get; set; }

    [JsonPropertyName("isRaven")]
    public bool IsRaven { get; set; }
}

/// <summary>
/// Response from /api/organizations
/// </summary>
public class OrganizationApiResponse
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("rate_limit_tier")]
    public string? RateLimitTier { get; set; }
}
