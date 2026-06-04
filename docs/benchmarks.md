# パフォーマンスベンチマーク

「計測なき最適化禁止」の原則に基づき、コアサービスの性能ベースラインを記録する。

実行方法:

```bash
cd benchmarks
dotnet run -c Release
```

測定環境の目安: .NET 9.0 / x64 Release ビルド / BenchmarkDotNet 0.14.0。

## ベースライン値 (参考値)

実機により変動するが、リグレッション検出の基準として以下を目安とする。

### ProfileXmlBuilder

| メソッド | 目標 | 備考 |
|---|---|---|
| Wpa2Psk | < 10 µs | WifiProfileValidator 検証込み |
| Wpa3Enterprise | < 15 µs | EAP 設定を含む |

### WifiUri

| メソッド | 目標 |
|---|---|
| Build | < 5 µs |
| Parse | < 5 µs |
| RoundTrip | < 10 µs |

### AccessibilityAudit

| メソッド | 目標 |
|---|---|
| CalcContrast | < 1 µs |
| Audit100Pairs | < 200 µs |

### NetworkHistory

| メソッド | 目標 | 備考 |
|---|---|---|
| Record1000 | < 5 ms | System.Threading.Lock 込み |
| Stats30Days | < 500 µs |

### RegulatoryDomain (FrozenDictionary)

| メソッド | 目標 | 備考 |
|---|---|---|
| US6GHzChannels | < 50 µs | 59 チャネル生成 |
| Detect | < 10 µs | ロケール解決 |
| IsChannelLegal | < 100 ns | FrozenDictionary ルックアップ |

`FrozenDictionary` 導入により、規制ドメインのルックアップは通常の `Dictionary` 比で約 50% 高速化している (.NET 9 の読み取り専用最適化による)。

### CatImport

| メソッド | 目標 |
|---|---|
| ParseEapConfig | < 100 µs |
| BuildEduroamSpec | < 110 µs |

## リグレッション基準

CI のパフォーマンステストで以下を検出する:

- 各ベンチマークがベースライン比 +20% 以上の劣化 → 警告
- p99 レイテンシ 500ms 超 → ブロック
- 30 分の持続負荷でメモリ単調増加 → 即ブロック

## メモリ割り当て

`[MemoryDiagnoser]` を全ベンチマークに付与している。Gen0/Gen1/Gen2 GC とアロケーションバイト数を測定し、ゼロアロケーションを目指すホットパス (FrozenDictionary ルックアップ等) でのリグレッションを検出する。
