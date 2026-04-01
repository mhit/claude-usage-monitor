using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ClaudeApiClient _apiClient;
    private readonly CredentialService _credentialService;
    private readonly PollingService _pollingService;
    private readonly WebViewPollingService _webViewPolling;

    // 5-hour session limit
    [ObservableProperty]
    private int _utilization;

    [ObservableProperty]
    private string _utilizationText = "0%";

    [ObservableProperty]
    private string _resetTimeText = "--:--";

    [ObservableProperty]
    private string _timeUntilResetText = "";

    // 7-day weekly limit
    [ObservableProperty]
    private int _weeklyUtilization;

    [ObservableProperty]
    private string _weeklyUtilizationText = "0%";

    [ObservableProperty]
    private string _weeklyResetText = "";

    // Sonnet limit
    [ObservableProperty]
    private int _sonnetUtilization;

    [ObservableProperty]
    private string _sonnetUtilizationText = "0%";

    [ObservableProperty]
    private string _lastUpdateText = "未取得";

    [ObservableProperty]
    private string _planText = "不明";

    [ObservableProperty]
    private string _statusText = "未接続";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private UsageLevel _currentLevel = UsageLevel.Safe;

    public MainViewModel()
    {
        _apiClient = new ClaudeApiClient();
        _credentialService = new CredentialService();
        _pollingService = new PollingService(_apiClient);
        _webViewPolling = new WebViewPollingService(_credentialService);

        _webViewPolling.UsageUpdated += OnWebViewUsageUpdated;
        _webViewPolling.StatusChanged += OnStatusChanged;

        // Load saved credentials
        LoadCredentials();
    }

    private void LoadCredentials()
    {
        var sessionKey = _credentialService.GetSessionKey();
        var orgId = _credentialService.GetOrganizationId();

        if (!string.IsNullOrEmpty(sessionKey) && !string.IsNullOrEmpty(orgId))
        {
            _apiClient.SetCredentials(sessionKey, orgId);
            IsConnected = true;
            
            // Load cached usage (includes plan info)
            LoadCachedUsage();
            
            // Start background WebView2 polling (bypasses Cloudflare)
            _ = _webViewPolling.StartAsync();
            
            StatusText = "接続済み";
        }
        else
        {
            StatusText = "未設定 - 設定を開いてください";
        }
    }

    private void LoadCachedUsage()
    {
        try
        {
            var cachePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeUsageMonitor", "usage_cache.json");
            
            if (!System.IO.File.Exists(cachePath)) return;
            
            var json = System.IO.File.ReadAllText(cachePath);
            var cache = System.Text.Json.JsonDocument.Parse(json);
            
            var utilization = cache.RootElement.GetProperty("Utilization").GetInt32();
            var fetchedAtStr = cache.RootElement.GetProperty("FetchedAt").GetString();
            var fetchedAt = DateTime.Parse(fetchedAtStr ?? "");
            
            DateTime? resetsAt = null;
            if (cache.RootElement.TryGetProperty("ResetsAt", out var resetsAtElem) && 
                resetsAtElem.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                resetsAt = DateTime.Parse(resetsAtElem.GetString() ?? "");
            }
            
            // Load plan info - PlanType (from capabilities) takes precedence over RateLimitTier
            if (cache.RootElement.TryGetProperty("PlanType", out var planTypeElem) &&
                !string.IsNullOrEmpty(planTypeElem.GetString()))
            {
                var info = new SubscriptionInfo { PlanType = planTypeElem.GetString()! };
                PlanText = info.DisplayName;
            }
            else if (cache.RootElement.TryGetProperty("BillingType", out var billingElem))
            {
                var billingType = billingElem.GetString() ?? "unknown";
                PlanText = billingType == "stripe_subscription" ? "Pro" : billingType;
            }
            
            // Update UI with cached data - 5-hour limit
            Utilization = utilization;
            UtilizationText = $"{utilization}%";
            if (resetsAt.HasValue)
            {
                var remaining = resetsAt.Value - DateTime.UtcNow;
                if (remaining.TotalMinutes > 0)
                {
                    var hours = (int)remaining.TotalHours;
                    var mins = remaining.Minutes;
                    TimeUntilResetText = hours > 0 ? $"{hours}時間{mins}分後" : $"{mins}分後";
                    ResetTimeText = resetsAt.Value.ToLocalTime().ToString("HH:mm");
                }
                else
                {
                    TimeUntilResetText = "まもなく";
                    ResetTimeText = "リセット中";
                }
            }
            
            // 7-day weekly limit
            if (cache.RootElement.TryGetProperty("WeeklyUtilization", out var weeklyElem))
            {
                var weekly = weeklyElem.GetInt32();
                WeeklyUtilization = weekly;
                WeeklyUtilizationText = $"{weekly}%";
                
                if (cache.RootElement.TryGetProperty("WeeklyResetsAt", out var weeklyResetElem) &&
                    weeklyResetElem.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    var weeklyReset = DateTime.Parse(weeklyResetElem.GetString() ?? "");
                    WeeklyResetText = weeklyReset.ToLocalTime().ToString("M/d HH:mm");
                }
            }
            
            // Sonnet limit
            if (cache.RootElement.TryGetProperty("SonnetUtilization", out var sonnetElem))
            {
                var sonnet = sonnetElem.GetInt32();
                SonnetUtilization = sonnet;
                SonnetUtilizationText = $"{sonnet}%";
            }
            
            LastUpdateText = $"{fetchedAt.ToLocalTime():HH:mm:ss}";
            
            // Determine level
            CurrentLevel = utilization switch
            {
                >= 80 => UsageLevel.Critical,
                >= 50 => UsageLevel.Moderate,
                _ => UsageLevel.Safe
            };
            
            Logger.Log("MainVM", $"Loaded cache: {utilization}%, plan={PlanText}");
        }
        catch (Exception ex)
        {
            Logger.Log("MainVM", $"Failed to load cache: {ex.Message}");
        }
    }

    private static string GetPlanDisplayName(string billingType, string rateLimitTier)
    {
        if (rateLimitTier.Contains("20x")) return "Max 20x";
        if (rateLimitTier.Contains("5x")) return "Max 5x";
        if (rateLimitTier.Contains("claude_max")) return "Max";
        if (billingType == "stripe_subscription")
            return "Pro";
        if (billingType == "prepaid")
            return "API (Prepaid)";
        if (billingType == "free")
            return "Free";
        return billingType;
    }

    private async Task LoadSubscriptionInfoAsync()
    {
        try
        {
            var subscriptionInfo = await _apiClient.GetSubscriptionInfoAsync();
            if (subscriptionInfo != null)
            {
                PlanText = subscriptionInfo.DisplayName;
            }
        }
        catch
        {
            PlanText = "不明";
        }
    }

    private void OnUsageUpdated(object? sender, UsageData data)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Utilization = data.Utilization;
            UtilizationText = $"{data.Utilization}%";
            ResetTimeText = data.ResetsAtLocal.ToString("HH:mm");
            TimeUntilResetText = $"in {data.TimeUntilResetFormatted}";
            LastUpdateText = DateTime.Now.ToString("HH:mm:ss");
            CurrentLevel = data.Level;
            StatusText = "接続中";
        });
    }

    private void OnErrorOccurred(object? sender, Exception ex)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = $"エラー: {ex.Message}";
        });
    }

    private void OnWebViewUsageUpdated(object? sender, UsageDataFull data)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // 5-hour session
            Utilization = data.FiveHourUtilization;
            UtilizationText = $"{data.FiveHourUtilization}%";
            if (data.FiveHourResetsAt.HasValue)
            {
                var remaining = data.FiveHourResetsAt.Value - DateTime.UtcNow;
                if (remaining.TotalMinutes > 0)
                {
                    var hours = (int)remaining.TotalHours;
                    var mins = remaining.Minutes;
                    TimeUntilResetText = hours > 0 ? $"{hours}時間{mins}分後" : $"{mins}分後";
                    ResetTimeText = data.FiveHourResetsAt.Value.ToLocalTime().ToString("HH:mm");
                }
            }
            CurrentLevel = data.FiveHourUtilization switch
            {
                >= 80 => UsageLevel.Critical,
                >= 50 => UsageLevel.Moderate,
                _ => UsageLevel.Safe
            };

            // Weekly
            WeeklyUtilization = data.WeeklyUtilization;
            WeeklyUtilizationText = $"{data.WeeklyUtilization}%";
            if (data.WeeklyResetsAt.HasValue)
            {
                WeeklyResetText = data.WeeklyResetsAt.Value.ToLocalTime().ToString("M/d HH:mm");
            }

            // Sonnet
            SonnetUtilization = data.SonnetUtilization;
            SonnetUtilizationText = $"{data.SonnetUtilization}%";

            LastUpdateText = DateTime.Now.ToString("HH:mm:ss");
        });
    }

    private void OnStatusChanged(object? sender, string status)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = status;
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Trigger WebView2 polling (runs in background, no window)
        StatusText = "更新中...";
        await _webViewPolling.StartAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsWindow = new Views.SettingsWindow(_apiClient, _credentialService);
        settingsWindow.CredentialsSaved += () =>
        {
            LoadCredentials();
        };
        settingsWindow.ShowDialog();
    }

    [RelayCommand]
    private void Quit()
    {
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _webViewPolling.UsageUpdated -= OnWebViewUsageUpdated;
        _webViewPolling.StatusChanged -= OnStatusChanged;
        _webViewPolling.Dispose();
        _pollingService.Dispose();
        _apiClient.Dispose();
    }
}
