using System.Windows;
using Microsoft.Win32;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor.Views;

public partial class SettingsWindow : Window
{
    private readonly ClaudeApiClient _apiClient;
    private readonly CredentialService _credentialService;
    private List<Organization> _organizations = new();

    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ClaudeUsageMonitor";

    public event Action? CredentialsSaved;

    public SettingsWindow(ClaudeApiClient apiClient, CredentialService credentialService)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _credentialService = credentialService;

        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var sessionKey = _credentialService.GetSessionKey();
        if (!string.IsNullOrEmpty(sessionKey))
        {
            // Show masked version
            SessionKeyTextBox.Text = MaskSessionKey(sessionKey);
            SessionKeyTextBox.Tag = sessionKey; // Store actual value
        }

        // Load startup setting
        StartWithWindowsCheckBox.IsChecked = IsStartupEnabled();
        
        // Load interval setting
        var interval = LoadSavedInterval();
        for (int i = 0; i < IntervalComboBox.Items.Count; i++)
        {
            var item = IntervalComboBox.Items[i] as System.Windows.Controls.ComboBoxItem;
            if (item?.Tag?.ToString() == interval.ToString())
            {
                IntervalComboBox.SelectedIndex = i;
                break;
            }
        }
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
                if (doc.RootElement.TryGetProperty("IntervalMinutes", out var intervalElem))
                {
                    return intervalElem.GetInt32();
                }
            }
        }
        catch { }
        return 2;
    }

    private void IntervalComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IntervalComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag != null)
        {
            var minutes = int.Parse(item.Tag.ToString()!);
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClaudeUsageMonitor", "settings.json");
                var json = System.Text.Json.JsonSerializer.Serialize(new { IntervalMinutes = minutes });
                System.IO.File.WriteAllText(path, json);
            }
            catch { }
        }
    }

    private static string MaskSessionKey(string key)
    {
        if (key.Length <= 10) return new string('•', key.Length);
        return key[..6] + new string('•', key.Length - 10) + key[^4..];
    }

    private void LoginToGetSessionKey_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow();
        var result = loginWindow.ShowDialog();

        if (result == true && loginWindow.LoginSuccessful)
        {
            var sessionKey = loginWindow.SessionKey!;
            var orgId = loginWindow.OrganizationId!;
            var orgName = loginWindow.OrganizationName ?? orgId;
            
            SessionKeyTextBox.Text = MaskSessionKey(sessionKey);
            SessionKeyTextBox.Tag = sessionKey;
            
            // Set organization directly from login
            var org = new Organization { Uuid = orgId, Name = orgName };
            _organizations = new List<Organization> { org };
            OrganizationComboBox.ItemsSource = _organizations;
            OrganizationComboBox.SelectedItem = org;
            
            // Cache usage and plan data from WebView2
            var planType = DeterminePlanType(loginWindow.Capabilities);
            var usageCache = new
            {
                Utilization = loginWindow.UsagePercent ?? 0,
                ResetsAt = loginWindow.UsageResetsAt?.ToString("o"),
                WeeklyUtilization = loginWindow.WeeklyUsagePercent ?? 0,
                WeeklyResetsAt = loginWindow.WeeklyResetsAt?.ToString("o"),
                SonnetUtilization = loginWindow.SonnetUsagePercent ?? 0,
                BillingType = loginWindow.BillingType ?? "unknown",
                RateLimitTier = loginWindow.RateLimitTier ?? "unknown",
                PlanType = planType,
                FetchedAt = DateTime.UtcNow.ToString("o")
            };
            var cacheJson = System.Text.Json.JsonSerializer.Serialize(usageCache);
            var cachePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeUsageMonitor", "usage_cache.json");
            System.IO.File.WriteAllText(cachePath, cacheJson);
            Logger.Log("Settings", $"Cached: usage={loginWindow.UsagePercent}%, billing={loginWindow.BillingType}");
            
            if (loginWindow.UsagePercent.HasValue)
            {
                ValidationStatus.Text = $"✓ ログイン成功: {orgName} (使用率: {loginWindow.UsagePercent}%)";
            }
            else
            {
                ValidationStatus.Text = $"✓ ログイン成功: {orgName}";
            }
            ValidationStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
    }

    private async Task ValidateAndFetchOrganizationsAsync(string sessionKey)
    {
        try
        {
            var isValid = await _apiClient.ValidateSessionKeyAsync(sessionKey);
            if (isValid)
            {
                await RefreshOrganizationsAsync(sessionKey);
            }
        }
        catch (Exception ex)
        {
            ValidationStatus.Text = $"エラー: {ex.Message}";
            ValidationStatus.Foreground = System.Windows.Media.Brushes.Salmon;
        }
    }

    private void PasteSessionKey_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            SessionKeyTextBox.Text = text;
            SessionKeyTextBox.Tag = text;
            ValidationStatus.Text = "貼り付けました。検証ボタンで確認してください。";
        }
    }

    private async void ValidateSessionKey_Click(object sender, RoutedEventArgs e)
    {
        var sessionKey = SessionKeyTextBox.Tag as string ?? SessionKeyTextBox.Text;
        
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            ValidationStatus.Text = "セッションキーを入力してください";
            ValidationStatus.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }

        ValidationStatus.Text = "検証中...";
        ValidationStatus.Foreground = System.Windows.Media.Brushes.Gray;

        try
        {
            var isValid = await _apiClient.ValidateSessionKeyAsync(sessionKey);
            
            if (isValid)
            {
                ValidationStatus.Text = "✓ 有効なセッションキーです";
                ValidationStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                
                // Fetch organizations
                await RefreshOrganizationsAsync(sessionKey);
            }
            else
            {
                ValidationStatus.Text = "✗ 無効なセッションキーです";
                ValidationStatus.Foreground = System.Windows.Media.Brushes.Salmon;
            }
        }
        catch (Exception ex)
        {
            ValidationStatus.Text = $"エラー: {ex.Message}";
            ValidationStatus.Foreground = System.Windows.Media.Brushes.Salmon;
        }
    }

    private async void RefreshOrganizations_Click(object sender, RoutedEventArgs e)
    {
        var sessionKey = SessionKeyTextBox.Tag as string ?? SessionKeyTextBox.Text;
        await RefreshOrganizationsAsync(sessionKey);
    }

    private async Task RefreshOrganizationsAsync(string sessionKey)
    {
        try
        {
            _apiClient.SetCredentials(sessionKey, "temp");
            
            _organizations = await _apiClient.GetOrganizationsAsync();
            OrganizationComboBox.ItemsSource = _organizations;
            
            if (_organizations.Count > 0)
            {
                // Try to select previously saved org
                var savedOrgId = _credentialService.GetOrganizationId();
                var savedOrg = _organizations.FirstOrDefault(o => o.Uuid == savedOrgId);
                OrganizationComboBox.SelectedItem = savedOrg ?? _organizations[0];
                ValidationStatus.Text = $"✓ {_organizations.Count}件の組織を取得しました";
                ValidationStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
        }
        catch (Exception ex)
        {
            // Show error but allow manual org ID entry
            ValidationStatus.Text = $"組織取得エラー: {ex.Message}";
            ValidationStatus.Foreground = System.Windows.Media.Brushes.Orange;
            
            // Add a default option so user can still save
            var defaultOrg = new Organization { Uuid = "default", Name = "(デフォルト組織)" };
            _organizations = new List<Organization> { defaultOrg };
            OrganizationComboBox.ItemsSource = _organizations;
            OrganizationComboBox.SelectedItem = defaultOrg;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var sessionKey = SessionKeyTextBox.Tag as string ?? SessionKeyTextBox.Text;
        var selectedOrg = OrganizationComboBox.SelectedItem as Organization;

        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            MessageBox.Show("セッションキーを入力してください", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (selectedOrg == null)
        {
            MessageBox.Show("組織を選択してください", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Save credentials
        _credentialService.SaveSessionKey(sessionKey);
        _credentialService.SaveOrganizationId(selectedOrg.Uuid);

        // Update API client
        _apiClient.SetCredentials(sessionKey, selectedOrg.Uuid);

        // Save startup setting
        SetStartupEnabled(StartWithWindowsCheckBox.IsChecked == true);

        CredentialsSaved?.Invoke();
        Close();
    }

    private static string DeterminePlanType(List<string>? capabilities)
    {
        if (capabilities == null) return "claude_pro";
        if (capabilities.Contains("claude_max_20x")) return "claude_max_20x";
        if (capabilities.Contains("claude_max_5x")) return "claude_max_5x";
        if (capabilities.Contains("claude_team")) return "claude_team";
        if (capabilities.Contains("claude_pro")) return "claude_pro";
        return "free";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #region Startup Registry

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch
        {
            // Ignore registry errors
        }
    }

    #endregion
}
