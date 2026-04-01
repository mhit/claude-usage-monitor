using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using ClaudeUsageMonitor.Services;
using ClaudeUsageMonitor.ViewModels;
using ClaudeUsageMonitor.Views;

namespace ClaudeUsageMonitor;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TaskbarIcon? _trayIcon;
    private MainViewModel? _viewModel;
    private int _lastNotifiedLevel = 0;
    private DateTime _lastResetTime = DateTime.MinValue;
    private DispatcherTimer? _iconTimer;
    private bool _showSession = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        Logger.Log("App", $"Starting... Log file: {Logger.GetLogPath()}");
        
        // Prevent multiple instances
        const string mutexName = "ClaudeUsageMonitor_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        
        if (!createdNew)
        {
            Logger.Log("App", "Already running, exiting");
            MessageBox.Show("Claude 使用量モニターは既に起動しています。\nタスクトレイを確認してください。", 
                "多重起動エラー", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            Logger.Log("App", "Creating ViewModel...");
            _viewModel = new MainViewModel();
            
            // Use system icon
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Claude 使用量モニター",
                Icon = SystemIcons.Information,
                TrayPopup = new MainPopup { DataContext = _viewModel }
            };

            // Update icon and check for notifications
            _viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Utilization) ||
                    args.PropertyName == nameof(MainViewModel.WeeklyUtilization) ||
                    args.PropertyName == nameof(MainViewModel.CurrentLevel))
                {
                    UpdateTrayIcon();
                    CheckAndNotify();
                }
            };

            // 起動直後にキャッシュ値でアイコンを即時更新
            UpdateTrayIcon();

            // 2秒ごとにセッション%/週間%を交互表示
            _iconTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _iconTimer.Tick += (s, e) => { _showSession = !_showSession; UpdateTrayIcon(); };
            _iconTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"起動エラー: {ex.Message}\n\n{ex.StackTrace}", "Claude 使用量モニター", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null || _viewModel == null) return;

        try
        {
            var session = _viewModel.Utilization;
            var weekly = _viewModel.WeeklyUtilization;
            _trayIcon.ToolTipText = $"Claude 使用量\nセッション: {session}%\n週間: {weekly}%";

            var pct = _showSession ? session : weekly;
            var color = _showSession
                ? _viewModel.CurrentLevel switch
                {
                    Models.UsageLevel.Critical => Color.FromArgb(255, 100, 100),
                    Models.UsageLevel.Moderate => Color.FromArgb(255, 210, 0),
                    _ => Color.FromArgb(80, 230, 80)
                }
                : Color.FromArgb(80, 160, 255);
            var label = _showSession ? "S" : "W";

            var newIcon = CreateSingleIcon(pct, color, label);
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = newIcon;
            if (oldIcon != null && oldIcon != SystemIcons.Information &&
                oldIcon != SystemIcons.Warning && oldIcon != SystemIcons.Error)
            {
                oldIcon.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateTrayIcon failed", ex);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon CreateSingleIcon(int pct, Color color, string label)
    {
        const int W = 64;
        const int H = 64;
        using var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        // SetResolution は Graphics.FromImage より前に呼ぶ
        bmp.SetResolution(96, 96);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PageUnit = GraphicsUnit.Pixel;

        using var brush = new SolidBrush(color);

        // フォントサイズを数値が収まるよう動的に決定
        float fontSize = 52f;
        Font? font = null;
        SizeF textSize = SizeF.Empty;
        do
        {
            font?.Dispose();
            font = new Font("Arial", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            textSize = g.MeasureString($"{pct}", font, PointF.Empty, StringFormat.GenericTypographic);
            if (textSize.Width <= W - 2 && textSize.Height <= H - 2) break;
            fontSize -= 4f;
        } while (fontSize > 8f);

        float x = (W - textSize.Width) / 2f;
        float y = (H - textSize.Height) / 2f;
        g.DrawString($"{pct}", font!, brush, x, y, StringFormat.GenericTypographic);
        font!.Dispose();

        // Clone() でアイコンがリソースの所有権を持つようにし、元のGDIハンドルを解放
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private void CheckAndNotify()
    {
        if (_trayIcon == null || _viewModel == null) return;

        var utilization = _viewModel.Utilization;

        // Notify at 80% (only once)
        if (utilization >= 80 && _lastNotifiedLevel < 80)
        {
            ShowNotification("⚠️ 使用量 80%", 
                $"Claude の使用量が {utilization}% に達しました。\nペースを落とすことを検討してください。",
                BalloonIcon.Warning);
            _lastNotifiedLevel = 80;
        }
        // Notify at 90% (only once)
        else if (utilization >= 90 && _lastNotifiedLevel < 90)
        {
            ShowNotification("🚨 使用量 90%", 
                $"Claude の使用量が {utilization}% に達しました！\nまもなく制限に達します。",
                BalloonIcon.Error);
            _lastNotifiedLevel = 90;
        }

        // Check for reset (utilization dropped significantly and time changed)
        if (_lastNotifiedLevel > 0 && utilization < 20)
        {
            ShowNotification("🔄 リセット完了", 
                "使用量がリセットされました。\n新しいセッションを開始できます。",
                BalloonIcon.Info);
            _lastNotifiedLevel = 0;
        }
    }

    private void ShowNotification(string title, string message, BalloonIcon icon)
    {
        _trayIcon?.ShowBalloonTip(title, message, icon);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _iconTimer?.Stop();
            _trayIcon?.Dispose();
            _viewModel?.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        finally
        {
            base.OnExit(e);
            // Force kill any remaining threads
            Environment.Exit(0);
        }
    }
}
