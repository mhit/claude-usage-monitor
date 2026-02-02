# Claude.ai API 調査ガイド

## 準備
1. Chrome または Edge を使用
2. Claude.ai にログイン済みの状態

## 調査手順

### Step 1: DevTools を開く
1. Claude.ai を開く (https://claude.ai)
2. F12 キーで開発者ツールを開く
3. **Network** タブを選択
4. 以下の設定を確認:
   - ☑️ Preserve log（ログを保持）
   - フィルタ: `Fetch/XHR` を選択

### Step 2: 使用量関連のAPI を探す
以下のキーワードでフィルタリング:
- `usage`
- `limit`
- `quota`
- `settings`
- `account`
- `organization`

### Step 3: 記録する情報
各APIエンドポイントについて:

```
【エンドポイント名】
URL: 
Method: GET/POST
Headers:
  - Cookie: (どんなCookieが送られているか)
  - Authorization: (もしあれば)
Response:
  (JSONをコピペ)
```

### Step 4: 特に注目
1. **ページロード時** に呼ばれるAPI（初期データ取得）
2. **メッセージ送信後** に呼ばれるAPI（使用量更新）
3. **設定ページ** でのAPI（プラン情報）

## 確認したいこと

### 使用制限情報
- [ ] 週間メッセージ上限
- [ ] 現在の使用量
- [ ] リセット日時
- [ ] Extended Thinking の残量
- [ ] Opus/Sonnet 等モデル別の制限

### 認証情報
- [ ] セッションCookieの名前
- [ ] 有効期限
- [ ] リフレッシュの仕組み

## 出力形式

調査結果は以下の形式で `api-research.md` に追記:

```markdown
### [エンドポイント名]
- **URL**: `https://claude.ai/api/xxx`
- **Method**: GET
- **認証**: Cookie (sessionKey)
- **Response**:
```json
{
  ...
}
```
```

---

## Tips

### Cookie の取得方法
1. DevTools > Application タブ
2. Storage > Cookies > https://claude.ai
3. 主要なCookie:
   - `sessionKey` (or similar)
   - `__cf_bm` (Cloudflare)

### HAR ファイルの保存
Network タブで右クリック → 「Save all as HAR with content」
→ 後で詳細分析できる

---
*このガイドに沿って調査し、結果を共有してください*
