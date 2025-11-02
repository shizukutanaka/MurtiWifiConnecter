# Stripe課金統合ガイド / Stripe Integration Guide

## 概要 / Overview

MurtiWifiConnecterに月額サブスクリプション課金機能を実装しました。Stripe APIを使用して、Free/Professional/Enterprise の3段階の課金モデルを提供します。

## 📊 課金モデル / Pricing Tiers

### Free Edition (無料)
- **価格**: ¥0/月
- **制限**:
  - ネットワーク保存: 5個まで
  - ログ保持期間: 7日
  - 履歴エントリ: 50件
  - 基本コマンドのみ (scan, connect, disconnect, status)
- **対象**: 個人ユーザー、試用

### Professional Edition (プロフェッショナル)
- **価格**: $0.50/月 (月額サブスクリプション)
- **機能**:
  - ネットワーク保存: 50個
  - ログ保持期間: 90日
  - 履歴エントリ: 1000件
  - 自動化機能 (automation, monitor, realtime)
  - 高度な分析 (analytics, predict, speed)
  - バックアップ/リストア
- **対象**: 中小企業、パワーユーザー

### Enterprise Edition (エンタープライズ)
- **価格**: $3.00 (買い切り/永久ライセンス)
- **機能**:
  - ネットワーク保存: 無制限
  - ログ保持期間: 365日
  - 履歴エントリ: 無制限
  - セキュリティスキャン (security-scan, security-audit)
  - コンプライアンスレポート (compliance, audit-trail)
  - コマンド異常検知 (command-anomalies)
  - 優先サポート
- **対象**: 大企業、コンプライアンス要求がある組織

---

## 🏗️ アーキテクチャ / Architecture

### コンポーネント構成

```
Core/Billing/
├── BillingTypes.cs           - データ型定義 (Edition, State, Result)
├── BillingStateCache.cs      - ローカル状態キャッシュ
├── BillingManager.cs         - 課金システム統合マネージャー
├── StripeClient.cs           - Stripe API統合レイヤー
├── SubscriptionManager.cs    - サブスクライフサイクル管理
├── WebhookProcessor.cs       - Stripeイベント処理
└── FeatureGate.cs           - 機能制限ゲート

Core/Handlers/
└── BillingCommandHandlers.cs - 課金関連コマンド

Core/
├── CommandProcessor.cs       - 機能ゲート統合 (Line 48-59)
└── ConfigManager.cs          - Stripe設定管理
```

### データフロー

1. **初回起動**
   - BillingManager初期化
   - ローカルキャッシュからEdition取得 (デフォルト: Free)
   - 機能制限適用

2. **購入フロー**
   ```
   User: billing upgrade professional
   → StripeClient: Checkout Session作成
   → User: ブラウザで決済
   → Stripe: Webhook送信
   → WebhookProcessor: イベント処理
   → SubscriptionManager: Edition更新
   → BillingStateCache: キャッシュ保存
   ```

3. **コマンド実行時**
   ```
   User: automation (Professional以上必要)
   → CommandProcessor: FeatureGate.CheckAccess()
   → Allowed? → 実行
   → Denied? → エラー表示 + アップグレード案内
   ```

4. **定期同期**
   ```
   User: billing sync
   → StripeClient: Subscription Status取得
   → BillingStateCache: 最新状態を保存
   ```

---

## 🔧 セットアップ / Setup

### 1. Stripe Dashboard設定

#### Products & Prices作成
1. [Stripe Dashboard](https://dashboard.stripe.com/products) にアクセス
2. **Products** → **Add Product** をクリック
3. **Professional Edition** を作成:
   - Name: `MurtiWifi Professional`
   - Price: $0.50 (recurring monthly)
   - Price IDをコピー (例: `price_xxxxxxxxxxxxx`)
4. **Enterprise Edition** を作成:
   - Name: `MurtiWifi Enterprise`
   - Price: $3.00 (one-time payment / 買い切り)
   - Price IDをコピー

#### Webhook設定
1. **Developers** → **Webhooks** → **Add endpoint**
2. Endpoint URL: `https://yourdomain.com/stripe-webhook`
3. Events to send:
   - `customer.subscription.created`
   - `customer.subscription.updated`
   - `customer.subscription.deleted`
   - `invoice.payment_succeeded`
   - `invoice.payment_failed`
   - `checkout.session.completed`
4. Webhook signing secretをコピー (例: `whsec_xxxxxxxxxxxxx`)

#### API Keys
1. **Developers** → **API keys**
2. **Secret key** をコピー (例: `sk_test_xxxxxxxxxxxxx`)

### 2. アプリケーション設定

#### config.jsonに追加
```json
{
  "Billing": {
    "Enabled": true,
    "DefaultEdition": "Free",
    "GracePeriodDays": 7,
    "CacheTtlSeconds": 60,
    "OfflineToleranceHours": 12,
    "Stripe": {
      "ApiKey": "sk_test_xxxxxxxxxxxxx",
      "PriceProfessionalMonthly": "price_xxxxxxxxxxxxx",
      "PriceEnterpriseMonthly": "price_xxxxxxxxxxxxx"
    },
    "Webhook": {
      "EndpointSecret": "whsec_xxxxxxxxxxxxx",
      "ListenAddress": "http://127.0.0.1:8787/stripe-webhook"
    }
  }
}
```

#### 環境変数 (本番環境推奨)
```bash
export STRIPE_API_KEY="sk_live_xxxxxxxxxxxxx"
export STRIPE_WEBHOOK_SECRET="whsec_xxxxxxxxxxxxx"
```

### 3. Stripe Price IDの更新

`Core/Billing/StripeClient.cs:17-21` を編集:
```csharp
private static readonly Dictionary<BillingEdition, string> PriceIds = new()
{
    [BillingEdition.Professional] = "price_xxxxxxxxxxxxx", // 実際のPrice IDに置き換え
    [BillingEdition.Enterprise] = "price_xxxxxxxxxxxxx"    // 実際のPrice IDに置き換え
};
```

### 4. リダイレクトURL設定

`Core/Billing/StripeClient.cs:56-57` を編集:
```csharp
SuccessUrl = "https://yourdomain.com/billing/success?session_id={CHECKOUT_SESSION_ID}",
CancelUrl = "https://yourdomain.com/billing/cancel",
```

---

## 💻 使用方法 / Usage

### ユーザーコマンド

#### 現在のステータス確認
```bash
./MurtiWifiConnecter.exe billing status
```
出力例:
```
═══════════════════════════════════
Billing Status
═══════════════════════════════════

Edition:     Free
Status:      ○ No Subscription
Source:      Configuration

Feature Limits:
  Max networks:        5
  Log retention:       7 days
  Automation:          Disabled
  Advanced security:   Disabled

To unlock more features, run: billing upgrade
```

#### アップグレード可能なプランを表示
```bash
./MurtiWifiConnecter.exe billing upgrade
```

#### プラン購入
```bash
# Professional Edition
./MurtiWifiConnecter.exe billing upgrade professional

# Enterprise Edition
./MurtiWifiConnecter.exe billing upgrade enterprise
```
→ Stripe Checkout URLが表示されるのでブラウザで開いて決済

#### 決済後、同期
```bash
./MurtiWifiConnecter.exe billing sync
```

#### サブスク管理ポータルを開く
```bash
./MurtiWifiConnecter.exe billing manage
```
→ Stripe Customer PortalのURLが表示され、キャンセル/プラン変更が可能

#### 利用可能な機能を確認
```bash
./MurtiWifiConnecter.exe billing features
```

#### トラブルシューティング
```bash
./MurtiWifiConnecter.exe billing diagnostics
```

### 機能制限の動作確認

Free Editionで制限されたコマンドを実行:
```bash
./MurtiWifiConnecter.exe automation
```

出力:
```
✗ Feature not available: automation

This feature requires Professional edition or higher.
Your current edition: Free

To upgrade, run: billing upgrade
```

---

## 🔄 Webhook処理 / Webhook Processing

### サポートされるイベント

| Event | 処理内容 |
|-------|---------|
| `customer.subscription.created` | 新規サブスク → Edition有効化 |
| `customer.subscription.updated` | プラン変更 → Edition更新 |
| `customer.subscription.deleted` | キャンセル → Free降格 |
| `invoice.payment_succeeded` | 支払い成功 → 更新日延長 |
| `invoice.payment_failed` | 支払い失敗 → Grace Period開始 |
| `checkout.session.completed` | 決済完了通知 |

### Webhook Endpoint実装例 (ASP.NET Core)

```csharp
[HttpPost("stripe-webhook")]
public async Task<IActionResult> StripeWebhook()
{
    var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
    var signature = Request.Headers["Stripe-Signature"];

    var success = await BillingManager.ProcessWebhookAsync(json, signature);

    if (success)
    {
        return Ok();
    }
    else
    {
        return BadRequest("Webhook signature verification failed");
    }
}
```

---

## 🧪 テスト / Testing

### テストモード (Stripe Test Keys)

1. Test APIキーを使用 (`sk_test_`, `pk_test_`)
2. [Stripe Test Cards](https://stripe.com/docs/testing#cards) で決済テスト:
   - 成功: `4242 4242 4242 4242`
   - 失敗: `4000 0000 0000 0002`
   - 3Dセキュア: `4000 0027 6000 3184`

### ローカルWebhookテスト

```bash
# Stripe CLIのインストール
brew install stripe/stripe-cli/stripe

# Webhookリスニング
stripe listen --forward-to localhost:8787/stripe-webhook

# テストイベント送信
stripe trigger customer.subscription.created
```

### 手動オーバーライド (開発用)

一時的にEditionを変更:
```csharp
await BillingManager.ApplyOverrideAsync(
    edition: BillingEdition.Enterprise,
    duration: TimeSpan.FromHours(1),
    reason: "Testing enterprise features"
);
```

解除:
```csharp
await BillingManager.ClearOverrideAsync();
```

---

## 🔒 セキュリティ / Security

### 実装済みセキュリティ機能

1. **Webhook署名検証**
   - Stripe署名を必ず検証 (`WebhookProcessor.cs`)
   - 不正リクエストは拒否

2. **API Key保護**
   - 設定ファイルにSecure ACL適用
   - ログには API Keyを出力しない

3. **Machine ID暗号化**
   - SHA256ハッシュで匿名化
   - プライバシー保護

4. **Grace Period**
   - 支払い失敗後7日間は機能継続
   - 猶予期間後にFreeへ降格

5. **監査ログ**
   - すべての課金イベントを記録
   - 不正利用の追跡可能

---

## 📊 監視 / Monitoring

### ログ確認

```bash
# 課金関連ログ
grep "Billing" logs/application.log

# Stripe API呼び出しログ
grep "StripeClient" logs/application.log
```

### メトリクス

- `BillingManager.GetStateAsync()` - 現在のEdition状態
- `SubscriptionManager.GetHealthAsync()` - サブスク健全性
- `BillingManager.GetDiagnosticsAsync()` - 診断情報

---

## 🐛 トラブルシューティング / Troubleshooting

### 問題: Stripe API Keyエラー

**症状**:
```
Stripe API key not configured. Set 'Billing.Stripe.ApiKey' in config.
```

**解決**:
1. `config.json` の `Billing.Stripe.ApiKey` が設定されているか確認
2. APIキーの形式が正しいか確認 (`sk_test_` または `sk_live_`)
3. `billing diagnostics` で設定確認

### 問題: Webhook署名検証失敗

**症状**:
```
Webhook signature verification failed
```

**解決**:
1. Webhook Secretが正しいか確認 (`whsec_`)
2. Stripe Dashboardで設定したエンドポイントURLが一致しているか確認
3. タイムスタンプのズレ (5分以上古いリクエストは拒否される)

### 問題: 決済後も Free Edition のまま

**症状**:
Checkout完了後、`billing status` でFreeのまま

**解決**:
1. `billing sync` を実行して強制同期
2. Webhook が正しく設定されているか確認
3. Stripe Dashboardの「Events」でWebhookが送信されているか確認

### 問題: 制限された機能にアクセスできない

**症状**:
```
Feature not available: automation
This feature requires Professional edition or higher.
```

**解決**:
1. `billing status` で現在のEditionを確認
2. アップグレード: `billing upgrade professional`
3. 同期: `billing sync`

---

## 📈 今後の拡張案 / Future Enhancements

1. **年額プランの追加**
   - 割引率: 月額 × 10ヶ月分
   - Stripe Priceに年額プラン追加

2. **トライアル期間**
   - Professional: 14日間無料トライアル
   - `SubscriptionData.TrialPeriodDays = 14`

3. **使用量ベース課金**
   - ネットワーク接続回数による従量課金
   - Stripe Meteringの活用

4. **チームプラン**
   - 複数ユーザーでのサブスク共有
   - Stripe Customer Metadata でチーム管理

5. **クーポン/プロモーションコード**
   - Stripe Coupon API統合
   - 割引キャンペーン実施

---

## 📚 参考資料 / References

- [Stripe Documentation](https://stripe.com/docs)
- [Stripe .NET SDK](https://github.com/stripe/stripe-dotnet)
- [Stripe Checkout](https://stripe.com/docs/payments/checkout)
- [Stripe Customer Portal](https://stripe.com/docs/billing/subscriptions/integrating-customer-portal)
- [Stripe Webhooks](https://stripe.com/docs/webhooks)
- [Stripe Testing](https://stripe.com/docs/testing)

---

## ✅ 実装チェックリスト / Implementation Checklist

- [x] BillingManager - 課金システムコア
- [x] StripeClient - Stripe API統合
- [x] SubscriptionManager - ライフサイクル管理
- [x] WebhookProcessor - イベント処理
- [x] FeatureGate - 機能制限システム
- [x] BillingCommandHandlers - CLIコマンド
- [x] CommandProcessor統合 - 自動ゲート適用
- [x] ConfigManager - Stripe設定管理
- [x] Program.cs初期化
- [x] ドキュメント作成

### 本番リリース前に必要な作業

- [ ] Stripe Product/Price ID設定
- [ ] 本番APIキー設定
- [ ] Webhook URLを本番環境に変更
- [ ] リダイレクトURLを本番ドメインに変更
- [ ] 本番環境でエンドツーエンドテスト
- [ ] 監査ログ監視の設定
- [ ] カスタマーサポート体制の構築

---

生成日時: 2025-10-08
バージョン: 2.0.0
