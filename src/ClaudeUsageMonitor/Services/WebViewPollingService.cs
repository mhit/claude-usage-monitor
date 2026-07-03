using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Polls usage data via hidden WebView2 to bypass Cloudflare
/// </summary>
public class WebViewPollingService : IDisposable
{
    private readonly CredentialService _credentialService;
    private readonly DispatcherTimer _timer;
    private WebView2? _webView;
    private Window? _hiddenWindow;
    private bool _isPolling = false;
    private bool _isInitialized = false;

    public event EventHandler<UsageDataFull>? UsageUpdated;
    public event EventHandler<string>? StatusChanged;

    public WebViewPollingService(CredentialService credentialService)
    {
        _credentialService = credentialService;
        
        // Load saved interval or default to 2 minutes
        var savedInterval = LoadSavedInterval();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(savedInterval)
        };
        _timer.Tick += async (s, e) => await PollAsync();
    }

    private static int LoadSavedInterval()
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeUsageMonitor", "settings.json");
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("IntervalMinutes", out var interval))
                {
                    return interval.GetInt32();
                }
            }
        }
        catch { }
        return 2; // Default 2 minutes
    }

    public void SetInterval(int minutes)
    {
        _timer.Interval = TimeSpan.FromMinutes(minutes);
        
        // Save setting
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeUsageMonitor", "settings.json");
            var json = System.Text.Json.JsonSerializer.Serialize(new { IntervalMinutes = minutes });
            System.IO.File.WriteAllText(path, json);
            Logger.Log("WebViewPoll", $"Interval set to {minutes} minutes");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save interval", ex);
        }
    }

    public int GetIntervalMinutes() => (int)_timer.Interval.TotalMinutes;

    public async Task StartAsync()
    {
        if (_isInitialized)
        {
            // Already initialized, just trigger a poll
            await ForcePollAsync();
            return;
        }

        try
        {
            Logger.Log("WebViewPoll", "Initializing hidden WebView2...");
            
            // Create hidden window with WebView2
            _hiddenWindow = new Window
            {
                Width = 1,
                Height = 1,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Visibility = Visibility.Hidden
            };
            
            _webView = new WebView2();
            _hiddenWindow.Content = _webView;
            _hiddenWindow.Show();
            _hiddenWindow.Hide();

            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            
            _isInitialized = true;
            Logger.Log("WebViewPoll", "WebView2 initialized");

            // Initial poll
            await PollAsync();
            
            // Start timer
            _timer.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("WebViewPoll init failed", ex);
        }
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public async Task ForcePollAsync()
    {
        if (!_isInitialized) return;
        _isPolling = false; // Reset to allow immediate poll
        await PollAsync();
    }

    private async Task PollAsync()
    {
        if (_isPolling || !_isInitialized || _webView?.CoreWebView2 == null) return;
        
        var sessionKey = _credentialService.GetSessionKey();
        var orgId = _credentialService.GetOrganizationId();
        
        if (string.IsNullOrEmpty(sessionKey) || string.IsNullOrEmpty(orgId))
        {
            Logger.Log("WebViewPoll", "No credentials, skipping poll");
            return;
        }

        _isPolling = true;
        StatusChanged?.Invoke(this, "更新中...");
        
        try
        {
            Logger.Log("WebViewPoll", $"Polling usage for org {orgId}...");
            _webView.CoreWebView2.Navigate($"https://claude.ai/api/organizations/{orgId}/usage");
        }
        catch (Exception ex)
        {
            Logger.Error("WebViewPoll navigate failed", ex);
            _isPolling = false;
            StatusChanged?.Invoke(this, "エラー");
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_isPolling) return;
        
        var url = _webView?.CoreWebView2?.Source ?? "";
        Logger.Log("WebViewPoll", $"Navigation completed: {url}");

        if (!url.Contains("/usage"))
        {
            _isPolling = false;
            return;
        }

        try
        {
            var json = await _webView!.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            
            if (json.StartsWith("\""))
            {
                json = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "";
            }
            
            Logger.Log("WebViewPoll", $"Got response: {json.Substring(0, Math.Min(100, json.Length))}...");

            if (!string.IsNullOrEmpty(json) && json.Contains("five_hour"))
            {
                var usage = ParseUsage(json);
                if (usage != null)
                {
                    SaveCache(usage);
                    UsageUpdated?.Invoke(this, usage);
                    StatusChanged?.Invoke(this, "接続済み");
                    Logger.Log("WebViewPoll", $"Updated: 5h={usage.FiveHourUtilization}%, 7d={usage.WeeklyUtilization}%, scoped={(usage.FableUtilization.HasValue ? $"{usage.FableLabel}:{usage.FableUtilization}%" : "なし")}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("WebViewPoll parse failed", ex);
            StatusChanged?.Invoke(this, "解析エラー");
        }
        finally
        {
            _isPolling = false;
        }
    }

    // モデル別週間制限(weekly_scoped)の使用率とモデル名を取得。
    // 第一候補: limits配列の kind="weekly_scoped" エントリー(2026-07時点の形式)。ラベルは scope.model.display_name。
    // フォールバック: 旧形式の seven_day_fable / seven_day_mythos キー。
    // 2026-07-08以降はFableが従量課金(usage credits)に移行し、エントリー自体が消える想定。
    // その場合はnullを返し、UI側はゲージ行ごと非表示にする。
    private static (string? Label, int Percent)? FindScopedWeeklyLimit(System.Text.Json.JsonElement root)
    {
        if (root.TryGetProperty("limits", out var limits) &&
            limits.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var entry in limits.EnumerateArray())
            {
                if (entry.TryGetProperty("kind", out var kind) &&
                    kind.GetString() == "weekly_scoped" &&
                    entry.TryGetProperty("percent", out var pct) &&
                    pct.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    string? label = null;
                    if (entry.TryGetProperty("scope", out var scope) &&
                        scope.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        scope.TryGetProperty("model", out var model) &&
                        model.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        model.TryGetProperty("display_name", out var dn) &&
                        dn.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        label = dn.GetString();
                    }
                    return (label, (int)pct.GetDouble());
                }
            }
            return null;
        }

        foreach (var key in new[] { "seven_day_fable", "seven_day_mythos" })
        {
            if (root.TryGetProperty(key, out var el) &&
                el.ValueKind == System.Text.Json.JsonValueKind.Object &&
                el.TryGetProperty("utilization", out var u) &&
                u.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                return (key == "seven_day_mythos" ? "Mythos" : "Fable", (int)u.GetDouble());
            }
        }
        return null;
    }

    private UsageDataFull? ParseUsage(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var usage = new UsageDataFull();

            if (root.TryGetProperty("five_hour", out var fiveHour) && fiveHour.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                usage.FiveHourUtilization = (int)fiveHour.GetProperty("utilization").GetDouble();
                if (fiveHour.TryGetProperty("resets_at", out var resetsAt) && resetsAt.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    usage.FiveHourResetsAt = DateTime.Parse(resetsAt.GetString()!).ToUniversalTime();
                }
            }
            
            if (root.TryGetProperty("seven_day", out var sevenDay) && sevenDay.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                usage.WeeklyUtilization = (int)sevenDay.GetProperty("utilization").GetDouble();
                if (sevenDay.TryGetProperty("resets_at", out var resetsAt) && resetsAt.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    usage.WeeklyResetsAt = DateTime.Parse(resetsAt.GetString()!).ToUniversalTime();
                }
            }
            
            // 7/8の従量課金移行後にlimits配列へ新形式(credits等)が来た場合に備え、生JSONを毎回記録する
            if (root.TryGetProperty("limits", out var limitsRaw))
            {
                Logger.Log("WebViewPoll", $"limits raw: {limitsRaw.GetRawText()}");
            }

            var scoped = FindScopedWeeklyLimit(root);
            if (scoped.HasValue)
            {
                usage.FableUtilization = scoped.Value.Percent;
                usage.FableLabel = scoped.Value.Label ?? "Fable";
            }

            usage.FetchedAt = DateTime.UtcNow;
            return usage;
        }
        catch (Exception ex)
        {
            Logger.Error("Parse usage failed", ex);
            return null;
        }
    }

    private void SaveCache(UsageDataFull usage)
    {
        try
        {
            var cachePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeUsageMonitor", "usage_cache.json");

            // 既存のBillingType/RateLimitTier/PlanTypeを保持（使用量APIはこれらを返さないため）
            string billingType = "stripe_subscription";
            string rateLimitTier = "";
            string planType = "";
            if (System.IO.File.Exists(cachePath))
            {
                try
                {
                    var existing = System.Text.Json.JsonDocument.Parse(
                        System.IO.File.ReadAllText(cachePath));
                    if (existing.RootElement.TryGetProperty("BillingType", out var bt))
                        billingType = bt.GetString() ?? billingType;
                    if (existing.RootElement.TryGetProperty("RateLimitTier", out var rt))
                        rateLimitTier = rt.GetString() ?? rateLimitTier;
                    if (existing.RootElement.TryGetProperty("PlanType", out var pt))
                        planType = pt.GetString() ?? planType;
                }
                catch { }
            }

            var cache = new
            {
                Utilization = usage.FiveHourUtilization,
                ResetsAt = usage.FiveHourResetsAt?.ToString("o"),
                WeeklyUtilization = usage.WeeklyUtilization,
                WeeklyResetsAt = usage.WeeklyResetsAt?.ToString("o"),
                FableUtilization = usage.FableUtilization,
                FableLabel = usage.FableLabel,
                BillingType = billingType,
                RateLimitTier = rateLimitTier,
                PlanType = planType,
                FetchedAt = DateTime.UtcNow.ToString("o")
            };

            var cacheJson = System.Text.Json.JsonSerializer.Serialize(cache);
            System.IO.File.WriteAllText(cachePath, cacheJson);
        }
        catch (Exception ex)
        {
            Logger.Error("Save cache failed", ex);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _webView?.Dispose();
        _hiddenWindow?.Close();
    }
}

public class UsageDataFull
{
    public int FiveHourUtilization { get; set; }
    public DateTime? FiveHourResetsAt { get; set; }
    public int WeeklyUtilization { get; set; }
    public DateTime? WeeklyResetsAt { get; set; }
    // null = APIがモデル別週間制限を返していない(従量課金移行後など) → ゲージ非表示
    public int? FableUtilization { get; set; }
    public string? FableLabel { get; set; }
    public DateTime FetchedAt { get; set; }
}
