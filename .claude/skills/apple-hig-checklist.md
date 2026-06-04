# Skill: apple-hig-checklist

## MWC Apple HIG 適合チェックリスト

### Clarity (明快さ)
- [ ] 技術用語をUIに直接表示していないか
  - ❌ "WPA3SAE" → ✅ "最高セキュリティ"
  - ❌ "GCMP256" → ✅ "Wi-Fi 7"
  - ❌ "BSSID" → ✅ (詳細タブのみ表示)
- [ ] SecurityBadgeService を使って変換しているか
- [ ] エラーメッセージが原因+解決策を示しているか (TroubleshootingHelper)

### Deference (コンテンツ優先)
- [ ] UIがコンテンツの邪魔をしていないか
- [ ] 頻度の低い操作はオーバーフローメニューに格納されているか
- [ ] EmptyState が適切に表示されるか (ネットワーク0件時)

### Depth (階層)
- [ ] Simple/Expert モードが正しく切り替わるか
- [ ] Expert モードのみ PHY/Band/Vendor を表示しているか
- [ ] 詳細パネルで段階的に情報が深くなるか

### Feedback (即応答)
- [ ] 接続中に ConnectionProgressDialog が表示されるか
- [ ] 接続成功/失敗で Windows トースト通知が出るか
- [ ] 接続失敗後に TroubleshootingDialog が出るか
- [ ] キャプティブポータル検出時に CaptivePortalDialog が出るか
- [ ] 操作(ボタンクリック)後に 200ms 以内に視覚変化があるか

### Onboarding
- [ ] 初回起動時に FirstRunWizard が表示されるか
- [ ] 設定 HasCompletedFirstRun が false の間のみ表示されるか

### Personalization
- [ ] テーマ(ダーク/ライト/システム)が即時切替されるか
- [ ] 言語設定が .resx 経由で反映されるか
- [ ] 設定が %LocalAppData%/MWC/settings.json に永続化されるか

### Accessibility
- [ ] 全ボタンに AutomationProperties.Name があるか
- [ ] キーボードのみで接続まで完結できるか
- [ ] コントラスト比が 4.5:1 以上か (AccessibilityService.CalcContrast)
- [ ] スクリーンリーダーへの状態通知 (Live Region) があるか

## 確認コマンド
```powershell
# アクセシビリティチェック
choco install accessibility-insights -y
# 対象ウィンドウを選択 → Tab 順序, AutomationProperties 確認
```
