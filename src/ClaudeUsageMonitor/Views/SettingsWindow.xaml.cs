using System.Windows;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor.Views;

public partial class SettingsWindow : Window
{
    private readonly ClaudeApiClient _apiClient;
    private readonly CredentialService _credentialService;
    private List<Organization> _organizations = new();

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
    }

    private static string MaskSessionKey(string key)
    {
        if (key.Length <= 10) return new string('•', key.Length);
        return key[..6] + new string('•', key.Length - 10) + key[^4..];
    }

    private void PasteSessionKey_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            SessionKeyTextBox.Text = text;
            SessionKeyTextBox.Tag = text;
            ValidationStatus.Text = "Pasted. Click Validate to verify.";
        }
    }

    private async void ValidateSessionKey_Click(object sender, RoutedEventArgs e)
    {
        var sessionKey = SessionKeyTextBox.Tag as string ?? SessionKeyTextBox.Text;
        
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            ValidationStatus.Text = "Please enter a session key";
            ValidationStatus.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }

        ValidationStatus.Text = "Validating...";
        ValidationStatus.Foreground = System.Windows.Media.Brushes.Gray;

        try
        {
            var isValid = await _apiClient.ValidateSessionKeyAsync(sessionKey);
            
            if (isValid)
            {
                ValidationStatus.Text = "✓ Valid session key";
                ValidationStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                
                // Fetch organizations
                await RefreshOrganizationsAsync(sessionKey);
            }
            else
            {
                ValidationStatus.Text = "✗ Invalid session key";
                ValidationStatus.Foreground = System.Windows.Media.Brushes.Salmon;
            }
        }
        catch (Exception ex)
        {
            ValidationStatus.Text = $"Error: {ex.Message}";
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
            var originalKey = _credentialService.GetSessionKey();
            _apiClient.SetCredentials(sessionKey, "temp");
            
            _organizations = await _apiClient.GetOrganizationsAsync();
            OrganizationComboBox.ItemsSource = _organizations;
            
            if (_organizations.Count > 0)
            {
                // Try to select previously saved org
                var savedOrgId = _credentialService.GetOrganizationId();
                var savedOrg = _organizations.FirstOrDefault(o => o.Uuid == savedOrgId);
                OrganizationComboBox.SelectedItem = savedOrg ?? _organizations[0];
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to fetch organizations: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var sessionKey = SessionKeyTextBox.Tag as string ?? SessionKeyTextBox.Text;
        var selectedOrg = OrganizationComboBox.SelectedItem as Organization;

        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            MessageBox.Show("Please enter a session key", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (selectedOrg == null)
        {
            MessageBox.Show("Please select an organization", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Save credentials
        _credentialService.SaveSessionKey(sessionKey);
        _credentialService.SaveOrganizationId(selectedOrg.Uuid);

        // Update API client
        _apiClient.SetCredentials(sessionKey, selectedOrg.Uuid);

        // TODO: Save other settings (interval, notifications, etc.)

        CredentialsSaved?.Invoke();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
