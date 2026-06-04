# MWC.SDK

[![NuGet](https://img.shields.io/nuget/v/MWC.SDK)](https://www.nuget.org/packages/MWC.SDK)
[![License: MIT](https://img.shields.io/badge/License-MIT-green)](https://github.com/shizukutanaka/MurtiWifiConnecter/blob/main/LICENSE)

Lightweight .NET library for Wi-Fi profile management, URI encoding, regulatory compliance, and enterprise authentication — extracted from [MWC (Multi WiFi Connector)](https://github.com/shizukutanaka/MurtiWifiConnecter).

**Zero external dependencies. Supports net8.0 and netstandard2.0.**

## Install

```bash
dotnet add package MWC.SDK
```

## Quick Start

```csharp
using MWC.Core.Profile;
using MWC.Core.Models;
using MWC.Core.Services;

// Wi-Fi QR コード URI を生成
var spec = new WifiProfileSpec { Ssid = "MyHome", Auth = AuthMethod.WPA2PSK, Passphrase = "secret" };
var uri  = WifiUri.Build(spec);    // WIFI:T:WPA2;S:MyHome;P:secret;;

// WLAN プロファイル XML を生成 (WPA3-SAE, Enterprise, EAP-TLS 対応)
var xml  = ProfileXmlBuilder.Build(spec);

// eduroam CAT XML をインポート
var cat    = new CatImportService();
var result = cat.ParseEapConfig(File.ReadAllText("university.eap-config"));
var profile = cat.BuildEduroamSpec(result.First());

// 6GHz 規制ドメイン
var reg = new RegulatoryDomainService();
var channels = reg.GetAvailable6GHzChannels("JP");   // 日本で使用可能なチャネル

// OWE 自動選択
var owe = new OweSelectionService();
var filtered = owe.ApplyOwePreference(scannedNetworks);  // Open AP を OWE に昇格

// EAP-TLS 証明書ストア
var certSvc = new CertificateStoreService();
var certs   = certSvc.GetClientCertificates();
var spec2   = certSvc.BuildEapTlsSpec("CorpWifi", certs.First());
```

## Included

| クラス | 説明 |
|---|---|
| `WifiUri` | WIFI: URI スキーム生成・解析 (QR コード用) |
| `ProfileXmlBuilder` | Windows WLAN プロファイル XML 生成 |
| `CatImportService` | eduroam CAT XML (eap-config) インポート |
| `RegulatoryDomainService` | 6GHz 帯の国別規制チャネル管理 |
| `OweSelectionService` | WPA3-OWE 自動選択 |
| `Hotspot20Service` | Passpoint/Hotspot2.0 プロファイル |
| `CertificateStoreService` | EAP-TLS 証明書ストア選択 |
| `SecurityBadgeService` | 認証方式 → セキュリティレベル変換 |
| `ExportService` | スキャン結果 CSV/JSON/TXT エクスポート |
| `TroubleshootingHelper` | 接続エラー → 人間語の解決策 |

## License

MIT
