# Security Policy

## サポート対象バージョン

| Version | Supported |
|---------|-----------|
| 1.x     | ✅ |
| 0.x     | ❌ (legacy) |

## 脆弱性報告

**公開 Issue で報告しないでください。**

[GitHub Security Advisories](https://github.com/shizukutanaka/MurtiWifiConnecter/security/advisories/new) からプライベート報告を送信してください。

### 報告に含める内容
- 脆弱性の種類(CWE 推奨)
- 影響範囲(認証情報漏洩、コード実行、DoS 等)
- 再現手順(可能な限り PoC コード)
- 影響バージョン
- 報告者の連絡先

### 応答 SLA
- **48 時間以内** に受領通知
- **7 日以内** に初期評価結果
- **30 日以内** に修正リリース(Critical/High)
- **90 日以内** に修正リリース(Medium/Low)

## セキュリティ設計

### 機密情報保護
- パスワードは **DPAPI** (`DataProtectionScope.CurrentUser`) で暗号化
- メモリ上は `SecureString`、使用直後 `Marshal.ZeroFreeGlobalAllocUnicode`
- 一時ファイルへのプロファイル書出は **行わない**(`WlanSetProfile` 直渡し)

### 入力検証
- SSID: UTF-8 32 バイト制限、制御文字 + `" & | < > %` 拒否
- パスフレーズ: 8〜63 ASCII 文字 or 64 桁 16 進
- すべての XML は `XElement` 経由で生成(エスケープ自動)

### プロセス起動
- `netsh.exe` 等の外部プロセス起動は **行わない**
- `ProcessStartInfo.UseShellExecute = false` を強制

### 通信
- 疎通確認の HTTP は `msftconnecttest.com` のみ(Microsoft NCSI 標準)
- TLS 1.2 以上を強制
- User-Agent 最小化、Cookie/Proxy 不使用

### 依存パッケージ
- Dependabot で週次自動更新
- リリース毎に CycloneDX SBOM 生成
- `dotnet list package --vulnerable` を CI で実行

### 配布物保護
- MSI / zip は **Sigstore keyless signing**(cosign)で署名
- **SLSA build provenance** 添付(GitHub OIDC ベース)
- ハッシュは GitHub Releases に記載

## 既知の制限

- 一部 Enterprise EAP メソッド(EAP-AKA, EAP-FAST)は未実装
- パスワード保存はユーザーバウンド(別マシン/別ユーザーへ移行不可)
- WLAN プロファイル登録には管理者権限必要

## 過去の脆弱性

なし(v1.0.0 以降)

旧 v0.x の致命的問題は ROADMAP.md で開示済(意図的にコード削除済)。
