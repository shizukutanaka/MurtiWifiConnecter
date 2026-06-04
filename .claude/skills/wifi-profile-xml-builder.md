# Skill: wifi-profile-xml-builder

## 用途
WLANProfile XML を認証方式別に正しく生成するパターンを参照する。

## 実装場所
`src/MWC.Core/Profile/ProfileXmlBuilder.cs`

## 認証方式対応表

| AuthMethod | authentication | encryption | useOneX |
|---|---|---|---|
| Open | open | none | false |
| OWE | OWE | AES | false |
| WEP | open | WEP | false |
| WPAPSK | WPAPSK | AES/TKIP | false |
| WPA2PSK | WPA2PSK | AES | false |
| WPA3SAE | WPA3SAE | AES | false |
| WPA3Transition | WPA3SAE | AES | false |
| WPA2Enterprise | WPA2 | AES | **true** |
| WPA3Enterprise | WPA3 | AES | **true** |
| WPA3Enterprise192 | WPA3ENT192 | GCMP256 | **true** |

## 禁止事項
- **文字列連結で XML を組み立てない** (インジェクション危険)
- **必ず XElement 経由で組み立てる**
- `<n>` タグは使わない → 正しくは `<name>`

## 正しいコード例
```csharp
var profile = new XElement(WlanNs + "WLANProfile",
    new XElement(WlanNs + "name", ssid),
    new XElement(WlanNs + "SSIDConfig",
        new XElement(WlanNs + "SSID",
            new XElement(WlanNs + "name", ssid))));
```

## テスト
`tests/MWC.Core.Tests/ProfileXmlBuilderTests.cs` — 全認証方式のゴールデンテスト
