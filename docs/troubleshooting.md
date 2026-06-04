# トラブルシューティングガイド

## 接続エラー別の対処

### 認証失敗 (Bad Credentials)

パスフレーズが正しくない。大文字・小文字、記号を確認する。WPA2/WPA3 のパスフレーズは 8〜63 文字。MWC はこのエラーでは自動再試行しない (誤ったパスフレーズでの再試行は無意味なため)。

### 範囲外 (Not In Range)

対象 SSID の電波が届いていない。アクセスポイントに近づくか、再スキャン (Ctrl+R) を実行する。ネットワークが隠し SSID の場合、手動で SSID を入力する必要がある。

### タイムアウト (Timeout)

接続要求が時間内に完了しなかった。MWC は指数バックオフ (500ms → 1000ms) で最大 2 回再試行する。アクセスポイントの混雑や電波干渉が原因の場合がある。

### 権限不足 (Insufficient Privilege)

プロファイル登録に管理者権限が必要。MWC を管理者として実行する。このエラーでは自動再試行しない。

### アダプター無効 (Adapter Disabled)

Wi-Fi アダプターが無効化されている。OS の設定または物理スイッチ (一部ラップトップ) で Wi-Fi を有効にする。

## プラットフォーム別

### Windows

WLAN AutoConfig サービスが停止していると動作しない。`services.msc` で「WLAN AutoConfig」が実行中か確認する。

```powershell
# サービス状態確認
Get-Service WlanSvc

# 起動
Start-Service WlanSvc
```

### Linux

NetworkManager が必要。

```bash
# nmcli が利用可能か確認
nmcli --version

# NetworkManager の状態
systemctl status NetworkManager
```

ユーザーが `netdev` グループに所属していない場合、一部操作に sudo が必要。

### macOS

位置情報サービスの許可が必要 (Wi-Fi スキャンに macOS が要求する)。システム設定 → プライバシーとセキュリティ → 位置情報サービスで MWC を許可する。

## ログの確認

MWC は構造化ログ (JSON) を出力する。問題報告時にはログを添付すると解決が早い。

```
%LocalAppData%\MWC\logs\        (Windows)
~/.local/share/MWC/logs/        (Linux)
~/Library/Application Support/MWC/logs/  (macOS)
```

ログには PII (個人識別情報) は含まれない。SSID やパスフレーズはログに記録されない。

## 6 GHz / Wi-Fi 7 関連

### Wi-Fi 7 の速度が出ない

Wi-Fi 7 の理論最大速度 (最大 46 Gbps) は 320 MHz チャネル幅 + 4096-QAM + 4 空間ストリームの理想条件下の値。実環境では干渉やデバイス能力により大幅に低下する。MWC はアダプターと AP の対応状況を表示する。

### 規制チャネルが表示されない

6 GHz の使用可能チャネルは地域の規制ドメインに従う。VPN 等でシステムロケールと実際の所在地が異なる場合、誤った規制が適用される可能性がある。

## 問題が解決しない場合

GitHub の Issue で報告する。報告時には以下を含めると解決が早い:

- OS とバージョン
- MWC のバージョン (ヘルプ → バージョン情報)
- エラーメッセージとエラー ID
- ログファイル (PII は含まれない)
- 再現手順
