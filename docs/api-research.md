# Claude.ai API 調査

## 概要
Claude.ai（ウェブ版）の使用制限情報を取得するための内部API調査。

## 調査対象
1. **カレントセッション使用量** - 現在の会話での使用量
2. **週間制限** - Pro/Teamプランの週間メッセージ制限
3. **All mode残量** - Extended thinking等のモード別残量

## 既知の情報

### Claude.ai プラン制限
- **Free**: 制限あり（詳細非公開）
- **Pro ($20/月)**: 5倍の使用量、優先アクセス
- **Team ($25/月/人)**: Pro同等 + チーム機能

### 制限の種類
1. **メッセージ制限** - 一定期間内のメッセージ数
2. **トークン制限** - 会話あたりのトークン数
3. **レート制限** - 短時間での連続リクエスト制限

## 調査方法

### 1. ブラウザ DevTools での調査
```
1. Claude.ai にログイン
2. F12 で開発者ツールを開く
3. Network タブを選択
4. 「Preserve log」にチェック
5. XHR/Fetch フィルタを適用
6. メッセージを送信して、APIコールを観察
```

### 2. 注目すべきエンドポイント
- `/api/organizations/*/usage` - 使用量関連?
- `/api/account` - アカウント情報
- `/api/settings` - 設定関連
- `/api/chat_conversations` - 会話関連

### 3. 認証方式
- セッションCookie (`sessionKey` or similar)
- Bearer Token（可能性）

## 調査結果

### API Base URL
```
https://claude.ai/api/
```

### エンドポイント一覧

| エンドポイント | メソッド | 説明 | 認証 |
|--------------|---------|------|------|
| TBD | | | |

### リクエスト/レスポンス例

#### 使用量取得（予想）
```http
GET /api/usage HTTP/1.1
Host: claude.ai
Cookie: sessionKey=...
```

```json
{
  "current_session": {
    "messages": 0,
    "tokens": 0
  },
  "weekly": {
    "used": 0,
    "limit": 0,
    "reset_at": "2026-02-09T00:00:00Z"
  },
  "extended_thinking": {
    "used": 0,
    "limit": 0
  }
}
```

## 次のステップ
1. [ ] ブラウザでClaude.aiにログインしてAPI調査
2. [ ] エンドポイント特定
3. [ ] 認証方式確認
4. [ ] レスポンス構造の文書化

## 参考リンク
- https://claude.ai
- https://docs.anthropic.com
- https://support.anthropic.com

---
*最終更新: 2026-02-02*
