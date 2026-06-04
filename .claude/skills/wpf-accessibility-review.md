# Skill: wpf-accessibility-review

## 用途
WPF コントロールの WCAG 2.1 AA / Apple HIG アクセシビリティ準拠チェック。

## 必須チェックリスト

### AutomationProperties
全インタラクティブ要素に付与:
```xml
<!-- Button -->
<Button AutomationProperties.Name="再スキャン"
        AutomationProperties.HelpText="Wi-Fi ネットワークを再検索"/>

<!-- ListBox -->
<ListBox AutomationProperties.Name="検出ネットワーク一覧"/>

<!-- TextBox / PasswordBox -->
<PasswordBox AutomationProperties.Name="パスフレーズ入力"/>
```

### キーボードナビゲーション
- Tab 順序が論理的か (`TabIndex` または自然な視覚順)
- Enter キーで主要アクション実行 (`IsDefault="True"`)
- Esc キーでキャンセル (`IsCancel="True"`)
- フォーカスリングが見えるか

### コントラスト比 (WCAG AA: 4.5:1, AAA: 7:1)
MWC ダークテーマの主要ペア (AccessibilityService.CalcContrast で確認済み):
- FgBrush (#E6E8EB) on BgBrush (#0F1115)  → 約 14:1 ✅ AAA
- AccentBrush (#00C4CC) on BgBrush          → 約  7:1 ✅ AAA
- FgMutedBrush (#9CA3AF) on BgBrush         → 約  6:1 ✅ AA

### Live Region (スクリーンリーダー通知)
```csharp
// 接続状態変更を読み上げ
AccessibilityService.AnnounceConnectionStatus("HomeNet に接続しました");
AccessibilityService.AnnounceError("パスフレーズが正しくありません");
```

## 検証ツール
- Accessibility Insights for Windows (Microsoft製, 無料)
- NVDA スクリーンリーダー
- Narrator (Windows内蔵)
