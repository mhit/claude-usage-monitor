using System.Timers;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Service for polling Claude API at regular intervals
/// </summary>
public class PollingService : IDisposable
{
    private readonly ClaudeApiClient _apiClient;
    private readonly System.Timers.Timer _timer;
    private bool _isRunning;

    public event EventHandler<UsageData>? UsageUpdated;
    public event EventHandler<Exception>? ErrorOccurred;

    public TimeSpan Interval
    {
        get => TimeSpan.FromMilliseconds(_timer.Interval);
        set => _timer.Interval = value.TotalMilliseconds;
    }

    public bool IsRunning => _isRunning;
    public UsageData? LastUsageData { get; private set; }
    public DateTime? LastUpdateTime { get; private set; }

    public PollingService(ClaudeApiClient apiClient, TimeSpan? interval = null)
    {
        _apiClient = apiClient;
        _timer = new System.Timers.Timer
        {
            Interval = (interval ?? TimeSpan.FromSeconds(30)).TotalMilliseconds,
            AutoReset = true
        };
        _timer.Elapsed += OnTimerElapsed;
    }

    /// <summary>
    /// Start polling
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;
        
        _isRunning = true;
        _timer.Start();
        
        // Fetch immediately on start
        _ = FetchAsync();
    }

    /// <summary>
    /// Stop polling
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _timer.Stop();
    }

    /// <summary>
    /// Force an immediate fetch
    /// </summary>
    public async Task RefreshAsync()
    {
        await FetchAsync();
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await FetchAsync();
    }

    private async Task FetchAsync()
    {
        if (!_apiClient.HasCredentials)
            return;

        try
        {
            var usageData = await _apiClient.GetUsageAsync();
            if (usageData != null)
            {
                LastUsageData = usageData;
                LastUpdateTime = DateTime.Now;
                UsageUpdated?.Invoke(this, usageData);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
