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
- Dependabot で週次更新(`.github/dependabot.yml`。NuGet と GitHub Actions の両方を対象)
- 以下は `docs/ci/` に実装済みだが **`.github/workflows/` が空のため未稼働**:
  - `dotnet list package --vulnerable` の CI 実行(`ci.yml`)
  - リリース毎の CycloneDX SBOM 生成(`release.yml`)

### 配布物保護

> ⚠️ **現時点で署名済みの配布物は存在しない。** リリースが 1 度も作られておらず、
> リリースワークフローも未設置のため、下記はすべて **設置後に有効になる設計** であり、
> 現在の事実ではない。**「MWC の配布物」を名乗るファイルを受け取った場合、
> それは本プロジェクトが公開したものではない。**

`docs/ci/release.yml` を `.github/workflows/` に設置し、タグを push すると:

- zip は **Sigstore keyless signing**(cosign)で署名される
- **SLSA build provenance** が添付される(GitHub OIDC ベース、`actions/attest-build-provenance`)
- SHA256 ハッシュが `SHA256SUMS.txt` として Release に添付される
- CycloneDX SBOM が添付される

検証方法(リリース公開後):

```bash
cosign verify-blob \
  --certificate <file>.pem \
  --signature   <file>.sig \
  --certificate-identity-regexp 'https://github.com/shizukutanaka/MurtiWifiConnecter/' \
  --certificate-oidc-issuer     'https://token.actions.githubusercontent.com' \
  <file>
```

MSI は `installer/wix/Product.wxs` が存在するが、ファイル harvest が未整備のため
パイプラインではまだビルドしていない(zip のみ)。

## 既知の制限

- 一部 Enterprise EAP メソッド(EAP-AKA, EAP-FAST)は未実装
- パスワード保存はユーザーバウンド(別マシン/別ユーザーへ移行不可)
- WLAN プロファイル登録には管理者権限必要

## 過去の脆弱性

なし(v1.0.0 以降)

旧 v0.x の致命的問題は ROADMAP.md で開示済(意図的にコード削除済)。
