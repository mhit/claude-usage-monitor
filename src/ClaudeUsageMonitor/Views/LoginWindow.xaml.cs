using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor.Views;

public partial class LoginWindow : Window
{
    public string? SessionKey { get; private set; }
    public string? OrganizationId { get; private set; }
    public string? OrganizationName { get; private set; }
    public string? BillingType { get; private set; }
    public string? RateLimitTier { get; private set; }
    public List<string>? Capabilities { get; private set; }
    public int? UsagePercent { get; private set; }
    public DateTime? UsageResetsAt { get; private set; }
    public int? WeeklyUsagePercent { get; private set; }
    public DateTime? WeeklyResetsAt { get; private set; }
    public int? SonnetUsagePercent { get; private set; }
    public bool LoginSuccessful => !string.IsNullOrEmpty(SessionKey) && !string.IsNullOrEmpty(OrganizationId);

    private DispatcherTimer? _cookiePollingTimer;
    private bool _isExtracting = false;

    public LoginWindow()
    {
        InitializeComponent();
        Logger.Log("LoginWindow", "Window opened");
        InitializeWebView();
    }

    private async void InitializeWebView()
    {
        try
        {
            Logger.Log("LoginWindow", "Initializing WebView2...");
            await WebView.EnsureCoreWebView2Async();
            Logger.Log("LoginWindow", "WebView2 initialized");
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            
            // Start cookie polling (for Google OAuth popup login)
            StartCookiePolling();
        }
        catch (Exception ex)
        {
            Logger.Error("WebView2 init failed", ex);
            StatusText.Text = $"WebView2 エラー: {ex.Message}";
        }
    }

    private void StartCookiePolling()
    {
        _cookiePollingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _cookiePollingTimer.Tick += async (s, e) => await CheckForSessionKey();
        _cookiePollingTimer.Start();
        Logger.Log("LoginWindow", "Cookie polling started");
    }

    private async Task CheckForSessionKey()
    {
        if (_isExtracting || LoginSuccessful) return;
        if (!string.IsNullOrEmpty(SessionKey)) return; // Already found
        
        try
        {
            var cookieManager = WebView.CoreWebView2?.CookieManager;
            if (cookieManager == null) return;
            
            var cookies = await cookieManager.GetCookiesAsync("https://claude.ai");
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "sessionKey" && !string.IsNullOrEmpty(cookie.Value))
                {
                    Logger.Log("LoginWindow", $"Cookie polling found sessionKey: {cookie.Value.Substring(0, Math.Min(20, cookie.Value.Length))}...");
                    _cookiePollingTimer?.Stop();
                    SessionKey = cookie.Value;
                    StatusText.Text = "セッションキー検出... 組織情報取得中...";
                    
                    // Navigate directly to organizations API
                    Logger.Log("LoginWindow", "Navigating to organizations API...");
                    WebView.CoreWebView2.Navigate("https://claude.ai/api/organizations");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("LoginWindow", $"Cookie polling error: {ex.Message}");
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var url = WebView.CoreWebView2.Source;
        Logger.Log("LoginWindow", $"Navigation completed: {url}");
        
        // Check if we navigated to the organizations API endpoint
        if (url.Contains("/api/organizations") && !url.Contains("/usage"))
        {
            Logger.Log("LoginWindow", "On organizations API page, extracting JSON...");
            await ExtractOrganizationsFromPage();
        }
        // Check if we navigated to the usage API endpoint
        else if (url.Contains("/usage"))
        {
            Logger.Log("LoginWindow", "On usage API page, extracting JSON...");
            await ExtractUsageFromPage();
        }
    }
    
    private async Task ExtractOrganizationsFromPage()
    {
        if (_isExtracting) return;
        _isExtracting = true;
        
        try
        {
            // Get session key from cookies first
            if (string.IsNullOrEmpty(SessionKey))
            {
                var cookieManager = WebView.CoreWebView2.CookieManager;
                var cookies = await cookieManager.GetCookiesAsync("https://claude.ai");
                foreach (var cookie in cookies)
                {
                    if (cookie.Name == "sessionKey" && !string.IsNullOrEmpty(cookie.Value))
                    {
                        SessionKey = cookie.Value;
                        Logger.Log("LoginWindow", $"SessionKey from cookies: {SessionKey.Substring(0, 20)}...");
                        break;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(SessionKey))
            {
                StatusText.Text = "セッションキーが見つかりません";
                _isExtracting = false;
                return;
            }
            
            // Get the page content (should be JSON)
            var json = await WebView.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            Logger.Log("LoginWindow", $"Page content (raw): {json}");
            
            // Unescape the JSON string
            if (json.StartsWith("\""))
            {
                json = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "";
            }
            Logger.Log("LoginWindow", $"Page content (decoded): {json}");
            
            if (string.IsNullOrEmpty(json) || json.Contains("error") || json.StartsWith("<"))
            {
                Logger.Log("LoginWindow", "Invalid response, using sessionKey only");
                OrganizationId = "unknown";
                OrganizationName = "（組織ID取得失敗）";
                StatusText.Text = "✓ セッションキー取得（組織IDは手動設定必要）";
                await Task.Delay(1000);
                DialogResult = true;
                Close();
                return;
            }
            
            // Parse organizations
            var orgs = System.Text.Json.JsonSerializer.Deserialize<List<OrgResponse>>(json);
            if (orgs != null && orgs.Count > 0)
            {
                OrganizationId = orgs[0].uuid;
                OrganizationName = orgs[0].name ?? orgs[0].uuid;
                BillingType = orgs[0].billing_type;
                RateLimitTier = orgs[0].rate_limit_tier;
                Capabilities = orgs[0].capabilities;
                Logger.Log("LoginWindow", $"Org found: {OrganizationId} ({OrganizationName}), billing: {BillingType}, tier: {RateLimitTier}");
                StatusText.Text = $"組織取得成功... 使用量を取得中...";
                
                // Now navigate to usage API
                _isExtracting = false;
                Logger.Log("LoginWindow", "Navigating to usage API...");
                WebView.CoreWebView2.Navigate($"https://claude.ai/api/organizations/{OrganizationId}/usage");
                return;
            }
            else
            {
                OrganizationId = "unknown";
                OrganizationName = "（組織なし）";
                StatusText.Text = "✓ セッションキー取得（組織IDは手動設定必要）";
                await Task.Delay(1000);
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ExtractOrganizationsFromPage failed", ex);
            StatusText.Text = $"エラー: {ex.Message}";
            _isExtracting = false;
        }
    }

    private async Task ExtractUsageFromPage()
    {
        if (_isExtracting) return;
        _isExtracting = true;
        
        try
        {
            // Get the page content (should be JSON)
            var json = await WebView.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            Logger.Log("LoginWindow", $"Usage content (raw): {json}");
            
            // Unescape the JSON string
            if (json.StartsWith("\""))
            {
                json = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "";
            }
            Logger.Log("LoginWindow", $"Usage content (decoded): {json}");
            
            if (!string.IsNullOrEmpty(json) && !json.StartsWith("<") && json.Contains("five_hour"))
            {
                var usage = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(json);
                
                // 5-hour session limit
                if (usage?.five_hour != null)
                {
                    UsagePercent = (int)usage.five_hour.utilization;
                    if (!string.IsNullOrEmpty(usage.five_hour.resets_at))
                    {
                        UsageResetsAt = DateTime.Parse(usage.five_hour.resets_at).ToUniversalTime();
                    }
                }
                
                // 7-day weekly limit
                if (usage?.seven_day != null)
                {
                    WeeklyUsagePercent = (int)usage.seven_day.utilization;
                    if (!string.IsNullOrEmpty(usage.seven_day.resets_at))
                    {
                        WeeklyResetsAt = DateTime.Parse(usage.seven_day.resets_at).ToUniversalTime();
                    }
                }
                
                // Sonnet limit
                if (usage?.seven_day_sonnet != null)
                {
                    SonnetUsagePercent = (int)usage.seven_day_sonnet.utilization;
                }
                
                Logger.Log("LoginWindow", $"Usage: 5h={UsagePercent}%, 7d={WeeklyUsagePercent}%, sonnet={SonnetUsagePercent}%");
                StatusText.Text = $"✓ 取得成功: {OrganizationName} ({UsagePercent}%)";
            }
            else
            {
                Logger.Log("LoginWindow", "Could not parse usage, but org was successful");
                StatusText.Text = $"✓ 組織取得成功: {OrganizationName}";
            }
            
            await Task.Delay(1000);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("ExtractUsageFromPage failed", ex);
            // Still succeed if we have org
            StatusText.Text = $"✓ 組織取得成功: {OrganizationName}";
            await Task.Delay(1000);
            DialogResult = true;
            Close();
        }
    }

    private class UsageResponse
    {
        public UsageTier? five_hour { get; set; }
        public UsageTier? seven_day { get; set; }
        public UsageTier? seven_day_sonnet { get; set; }
    }
    
    private class UsageTier
    {
        public double utilization { get; set; }
        public string? resets_at { get; set; }
    }

    private async Task ExtractSessionData()
    {
        if (_isExtracting) return;
        _isExtracting = true;
        
        try
        {
            // Get session key from cookies if not already set
            if (string.IsNullOrEmpty(SessionKey))
            {
                Logger.Log("LoginWindow", "Getting cookies...");
                var cookieManager = WebView.CoreWebView2.CookieManager;
                var cookies = await cookieManager.GetCookiesAsync("https://claude.ai");
                
                Logger.Log("LoginWindow", $"Got {cookies.Count} cookies");
                
                foreach (var cookie in cookies)
                {
                    Logger.Log("LoginWindow", $"Cookie: {cookie.Name} = {(cookie.Name == "sessionKey" ? cookie.Value.Substring(0, Math.Min(20, cookie.Value.Length)) + "..." : "[hidden]")}");
                    if (cookie.Name == "sessionKey" && !string.IsNullOrEmpty(cookie.Value))
                    {
                        SessionKey = cookie.Value;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(SessionKey))
                {
                    Logger.Log("LoginWindow", "No sessionKey cookie found");
                    StatusText.Text = "セッションキーが見つかりません";
                    return;
                }
            }

            Logger.Log("LoginWindow", $"SessionKey: {SessionKey.Substring(0, 20)}...");
            StatusText.Text = "セッションキー取得... 組織情報を取得中...";

            // Use JavaScript to fetch organizations - simpler approach
            Logger.Log("LoginWindow", "Executing JS to fetch organizations...");
            var script = @"
                (async function() {
                    try {
                        const response = await fetch('/api/organizations', {
                            method: 'GET',
                            credentials: 'include'
                        });
                        const text = await response.text();
                        console.log('Org API response:', text);
                        return text;
                    } catch (e) {
                        console.error('Fetch error:', e);
                        return JSON.stringify({error: e.toString()});
                    }
                })()
            ";

            var result = await WebView.CoreWebView2.ExecuteScriptAsync(script);
            Logger.Log("LoginWindow", $"JS result (raw): {result}");
            
            // ExecuteScriptAsync returns JSON-encoded result
            // If the JS returns a string, it will be double-quoted: "\"[{...}]\""
            // If it returns an object directly, it might be "{}"
            string json;
            
            if (result.StartsWith("\"") && result.EndsWith("\""))
            {
                // It's a JSON-encoded string, decode it
                try
                {
                    json = System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? "";
                }
                catch
                {
                    json = result.Trim('"');
                }
            }
            else
            {
                json = result;
            }
            
            Logger.Log("LoginWindow", $"JS result (decoded): {json}");
            
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                Logger.Log("LoginWindow", "Empty JSON response");
                StatusText.Text = "組織情報の取得に失敗しました";
                _isExtracting = false;
                return;
            }

            // Try to parse organizations
            Logger.Log("LoginWindow", "Parsing organizations...");
            
            // Check for error or empty response
            if (string.IsNullOrEmpty(json) || json == "{}" || json.Contains("\"error\""))
            {
                Logger.Log("LoginWindow", $"Org fetch failed, using sessionKey only. Response: {json}");
                // Still succeed with sessionKey only - org can be set manually
                OrganizationId = "unknown";
                OrganizationName = "（組織ID取得失敗 - 設定で手動入力してください）";
                StatusText.Text = $"✓ セッションキー取得成功（組織IDは手動設定が必要）";
                await Task.Delay(1500);
                DialogResult = true;
                Close();
                return;
            }

            try
            {
                var orgs = System.Text.Json.JsonSerializer.Deserialize<List<OrgResponse>>(json);
                if (orgs != null && orgs.Count > 0)
                {
                    OrganizationId = orgs[0].uuid;
                    OrganizationName = orgs[0].name ?? orgs[0].uuid;
                    Logger.Log("LoginWindow", $"Success! Org: {OrganizationId} ({OrganizationName})");
                    StatusText.Text = $"✓ 取得成功: {OrganizationName}";
                }
                else
                {
                    throw new Exception("Empty org list");
                }
            }
            catch
            {
                Logger.Log("LoginWindow", "Org parse failed, using sessionKey only");
                OrganizationId = "unknown";
                OrganizationName = "（組織ID取得失敗 - 設定で手動入力してください）";
                StatusText.Text = $"✓ セッションキー取得成功（組織IDは手動設定が必要）";
            }
            
            await Task.Delay(1000);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("ExtractSessionData failed", ex);
            StatusText.Text = $"エラー: {ex.Message}";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Logger.Log("LoginWindow", "Cancelled by user");
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        Logger.Log("LoginWindow", "Window closed");
        _cookiePollingTimer?.Stop();
        WebView.Dispose();
        base.OnClosed(e);
    }

    private class OrgResponse
    {
        public string uuid { get; set; } = "";
        public string? name { get; set; }
        public string? billing_type { get; set; }
        public string? rate_limit_tier { get; set; }
        public List<string>? capabilities { get; set; }
    }
}
