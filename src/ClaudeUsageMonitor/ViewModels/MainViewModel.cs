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

    [ObservableProperty]
    private int _utilization;

    [ObservableProperty]
    private string _utilizationText = "0%";

    [ObservableProperty]
    private string _resetTimeText = "--:--";

    [ObservableProperty]
    private string _timeUntilResetText = "";

    [ObservableProperty]
    private string _lastUpdateText = "Never";

    [ObservableProperty]
    private string _planText = "Unknown";

    [ObservableProperty]
    private string _statusText = "Not connected";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private UsageLevel _currentLevel = UsageLevel.Safe;

    public MainViewModel()
    {
        _apiClient = new ClaudeApiClient();
        _credentialService = new CredentialService();
        _pollingService = new PollingService(_apiClient);

        _pollingService.UsageUpdated += OnUsageUpdated;
        _pollingService.ErrorOccurred += OnErrorOccurred;

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
            StatusText = "Connected";
            _pollingService.Start();
            _ = LoadSubscriptionInfoAsync();
        }
        else
        {
            StatusText = "Not configured";
        }
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
            PlanText = "Unknown";
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
            StatusText = "Connected";
        });
    }

    private void OnErrorOccurred(object? sender, Exception ex)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = $"Error: {ex.Message}";
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        StatusText = "Refreshing...";
        await _pollingService.RefreshAsync();
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
        _pollingService.UsageUpdated -= OnUsageUpdated;
        _pollingService.ErrorOccurred -= OnErrorOccurred;
        _pollingService.Dispose();
        _apiClient.Dispose();
    }
}
