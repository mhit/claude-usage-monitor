using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using ClaudeUsageMonitor.ViewModels;
using ClaudeUsageMonitor.Views;

namespace ClaudeUsageMonitor;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _viewModel = new MainViewModel();
        
        // Create tray icon
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Claude Usage Monitor",
            IconSource = CreateDefaultIcon(),
            TrayPopup = new MainPopup { DataContext = _viewModel }
        };

        // Update icon based on usage level
        _viewModel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.Utilization) ||
                args.PropertyName == nameof(MainViewModel.CurrentLevel))
            {
                UpdateTrayIcon();
            }
        };
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null || _viewModel == null) return;

        _trayIcon.ToolTipText = $"Claude Usage: {_viewModel.Utilization}%";
        // TODO: Generate colored icon based on level
    }

    private static System.Windows.Media.ImageSource CreateDefaultIcon()
    {
        // Create a simple default icon
        var visual = new System.Windows.Media.DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawEllipse(
                System.Windows.Media.Brushes.DodgerBlue,
                null,
                new System.Windows.Point(8, 8),
                8, 8);
        }
        
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            16, 16, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        
        return bitmap;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _viewModel?.Dispose();
        base.OnExit(e);
    }
}
