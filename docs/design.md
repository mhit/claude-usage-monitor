# Claude Usage Monitor for Windows - 全体設計書

## 概要
Windowsタスクバーに常駐し、Claude.aiの使用量をリアルタイム表示するアプリ。

## 目標
- カレントセッション使用量（5時間ウィンドウ）の表示
- 週間制限の残量表示
- リセット時刻の表示
- システムトレイでの常時監視

---

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────┐
│                    Windows Taskbar                       │
│  ┌─────────────────────────────────────────────────┐    │
│  │ 🔵 45% | Reset: 15:30                           │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                   Tray Icon Popup                        │
│  ┌─────────────────────────────────────────────────┐    │
│  │ Session:  ████████░░ 45%  (Reset: 15:30)       │    │
│  │ Weekly:   ██░░░░░░░░ 12%  (Reset: Mon 00:59)   │    │
│  │ ─────────────────────────────────────────────   │    │
│  │ Plan: Pro  |  Org: Personal                    │    │
│  │ [Settings] [Refresh] [Quit]                    │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                    Application Layer                     │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │  TrayIcon    │  │  Settings    │  │  Notifier    │  │
│  │  Manager     │  │  Window      │  │  Service     │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                    Service Layer                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │  Claude API  │  │  Credential  │  │  Polling     │  │
│  │  Client      │  │  Manager     │  │  Service     │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                    Claude.ai API                         │
│  GET /api/organizations/{orgId}/usage                   │
│  Cookie: sessionKey=xxx                                  │
└─────────────────────────────────────────────────────────┘
```

---

## 技術スタック

| レイヤー | 技術 |
|---------|------|
| Framework | .NET 8 (Windows) |
| UI | WPF + Hardcodet.NotifyIcon.Wpf |
| HTTP Client | System.Net.Http.HttpClient |
| JSON | System.Text.Json |
| Credential Storage | Windows Credential Manager (CredentialManager NuGet) |
| Settings | user.config or JSON file |

---

## コンポーネント設計

### 1. ClaudeApiClient
```csharp
public class ClaudeApiClient
{
    private readonly HttpClient _httpClient;
    private string _sessionKey;
    private string _organizationId;

    // 使用量取得
    public async Task<UsageData> GetUsageAsync();
    
    // 組織一覧取得
    public async Task<List<Organization>> GetOrganizationsAsync();
    
    // サブスクリプション情報取得
    public async Task<SubscriptionInfo> GetSubscriptionInfoAsync();
}
```

### 2. UsageData モデル
```csharp
public record UsageData
{
    public int Utilization { get; init; }      // 0-100%
    public DateTime ResetsAt { get; init; }    // リセット時刻
    public TimeSpan TimeUntilReset => ResetsAt - DateTime.UtcNow;
}
```

### 3. CredentialManager
```csharp
public class CredentialManager
{
    private const string CredentialTarget = "ClaudeUsageMonitor";
    
    public void SaveSessionKey(string sessionKey);
    public string? GetSessionKey();
    public void DeleteSessionKey();
}
```

### 4. PollingService
```csharp
public class PollingService
{
    private readonly Timer _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(30);
    
    public event EventHandler<UsageData>? UsageUpdated;
    
    public void Start();
    public void Stop();
    public void SetInterval(TimeSpan interval);
}
```

### 5. TrayIconManager
```csharp
public class TrayIconManager
{
    private readonly TaskbarIcon _trayIcon;
    
    public void UpdateIcon(int percentage);  // 色付きアイコン生成
    public void UpdateTooltip(UsageData data);
    public void ShowNotification(string title, string message);
}
```

---

## 画面設計

### メインポップアップ
```
╔════════════════════════════════════════╗
║  Claude Usage Monitor                  ║
╠════════════════════════════════════════╣
║                                        ║
║  📊 Session (5h window)                ║
║  ████████████░░░░░░░░  62%            ║
║  Resets at: 15:30 (in 2h 15m)         ║
║                                        ║
║  📅 Weekly                             ║
║  ███░░░░░░░░░░░░░░░░░  15%            ║
║  Resets: Mon 00:59                     ║
║                                        ║
╠════════════════════════════════════════╣
║  Plan: Pro  │  Last update: 12:34     ║
╠════════════════════════════════════════╣
║  [⚙ Settings]  [🔄 Refresh]  [✕ Quit] ║
╚════════════════════════════════════════╝
```

### 設定画面
```
╔════════════════════════════════════════╗
║  Settings                         [X]  ║
╠════════════════════════════════════════╣
║                                        ║
║  Session Key:                          ║
║  ┌────────────────────────────────┐   ║
║  │ sk-ant-••••••••••••••••••••••  │   ║
║  └────────────────────────────────┘   ║
║  [Paste from clipboard] [Clear]       ║
║                                        ║
║  Organization:                         ║
║  ┌────────────────────────────────┐   ║
║  │ Personal                    ▼  │   ║
║  └────────────────────────────────┘   ║
║                                        ║
║  Refresh Interval:                     ║
║  ┌────────────────────────────────┐   ║
║  │ 30 seconds                  ▼  │   ║
║  └────────────────────────────────┘   ║
║                                        ║
║  ☑ Start with Windows                 ║
║  ☑ Show notifications at 80%/90%      ║
║                                        ║
╠════════════════════════════════════════╣
║           [Save]    [Cancel]           ║
╚════════════════════════════════════════╝
```

---

## フォルダ構成

```
ClaudeUsageMonitor/
├── ClaudeUsageMonitor.sln
├── src/
│   └── ClaudeUsageMonitor/
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── Models/
│       │   ├── UsageData.cs
│       │   ├── Organization.cs
│       │   └── SubscriptionInfo.cs
│       ├── Services/
│       │   ├── ClaudeApiClient.cs
│       │   ├── CredentialManager.cs
│       │   └── PollingService.cs
│       ├── ViewModels/
│       │   ├── MainViewModel.cs
│       │   └── SettingsViewModel.cs
│       ├── Views/
│       │   ├── MainPopup.xaml
│       │   └── SettingsWindow.xaml
│       ├── Helpers/
│       │   ├── TrayIconManager.cs
│       │   └── IconGenerator.cs
│       └── Resources/
│           └── Icons/
├── tests/
│   └── ClaudeUsageMonitor.Tests/
└── docs/
    ├── api-research.md
    └── design.md
```

---

## 実装フェーズ

### Phase 1: 基本機能 (MVP)
- [x] API仕様調査
- [ ] プロジェクト作成
- [ ] ClaudeApiClient実装
- [ ] 基本的なトレイアイコン表示
- [ ] 手動セッションキー入力

### Phase 2: UI改善
- [ ] ポップアップUI作成
- [ ] 設定画面作成
- [ ] アイコン色変化（使用率に応じて）

### Phase 3: 追加機能
- [ ] Windows起動時に自動起動
- [ ] 使用率警告通知
- [ ] 週間使用量表示

### Phase 4: 改善
- [ ] Chromium Cookie自動取得（オプション）
- [ ] 多言語対応
- [ ] テーマ対応（ダーク/ライト）

---

## ビルド環境

### 必要なもの
- Visual Studio 2022 または VS Code + C# Dev Kit
- .NET 8 SDK
- Windows 10/11

### WSLとの連携
- ソースコードはWSL側 (`/mnt/c/dev/claude-usage-monitor`)
- ビルドはWindows側 PowerShell or VS

---

*作成日: 2026-02-02*
