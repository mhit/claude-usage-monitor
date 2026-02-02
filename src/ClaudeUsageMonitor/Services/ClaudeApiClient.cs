using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Client for Claude.ai internal API
/// </summary>
public class ClaudeApiClient : IDisposable
{
    private const string BaseUrl = "https://claude.ai/api";
    private readonly HttpClient _httpClient;
    private string? _sessionKey;
    private string? _organizationId;

    public ClaudeApiClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void SetCredentials(string sessionKey, string organizationId)
    {
        _sessionKey = sessionKey;
        _organizationId = organizationId;
    }

    public bool HasCredentials => !string.IsNullOrEmpty(_sessionKey) && !string.IsNullOrEmpty(_organizationId);

    /// <summary>
    /// Get current usage data
    /// </summary>
    public async Task<UsageData?> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        if (!HasCredentials)
            throw new InvalidOperationException("Credentials not set");

        var request = CreateRequest($"/organizations/{_organizationId}/usage");
        
        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<UsageApiResponse>(json);

            if (apiResponse?.FiveHour == null)
                return null;

            return new UsageData
            {
                Utilization = apiResponse.FiveHour.Utilization,
                ResetsAt = ParseDateTime(apiResponse.FiveHour.ResetsAt),
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (HttpRequestException ex)
        {
            throw new ClaudeApiException("Failed to fetch usage data", ex);
        }
    }

    /// <summary>
    /// Get list of organizations
    /// </summary>
    public async Task<List<Organization>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_sessionKey))
            throw new InvalidOperationException("Session key not set");

        var request = CreateRequest("/organizations");
        
        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<List<OrganizationApiResponse>>(json);

            return apiResponse?.Select(o => new Organization
            {
                Uuid = o.Uuid,
                Name = o.Name,
                RateLimitTier = o.RateLimitTier
            }).ToList() ?? new List<Organization>();
        }
        catch (HttpRequestException ex)
        {
            throw new ClaudeApiException("Failed to fetch organizations", ex);
        }
    }

    /// <summary>
    /// Get subscription information
    /// </summary>
    public async Task<SubscriptionInfo?> GetSubscriptionInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!HasCredentials)
            throw new InvalidOperationException("Credentials not set");

        var request = CreateRequest($"/bootstrap/{_organizationId}/statsig");
        
        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<StatsigApiResponse>(json);

            return new SubscriptionInfo
            {
                PlanType = apiResponse?.User?.Custom?.OrgType ?? "free",
                IsRaven = apiResponse?.User?.Custom?.IsRaven ?? false
            };
        }
        catch (HttpRequestException ex)
        {
            throw new ClaudeApiException("Failed to fetch subscription info", ex);
        }
    }

    /// <summary>
    /// Validate session key by attempting to fetch organizations
    /// </summary>
    public async Task<bool> ValidateSessionKeyAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var originalKey = _sessionKey;
        try
        {
            _sessionKey = sessionKey;
            var orgs = await GetOrganizationsAsync(cancellationToken);
            return orgs.Count > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            _sessionKey = originalKey;
        }
    }

    private HttpRequestMessage CreateRequest(string endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{endpoint}");
        request.Headers.Add("Cookie", $"sessionKey={_sessionKey}");
        return request;
    }

    private static DateTime ParseDateTime(string? dateTimeString)
    {
        if (string.IsNullOrEmpty(dateTimeString))
            return DateTime.UtcNow.AddHours(5);

        if (DateTime.TryParse(dateTimeString, out var result))
            return result.ToUniversalTime();

        return DateTime.UtcNow.AddHours(5);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public class ClaudeApiException : Exception
{
    public ClaudeApiException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
