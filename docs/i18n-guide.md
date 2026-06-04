# i18n Guide

## 目標
- 既定: 日本語(`Strings.ja.resx`)
- 完全対応: ja, en, zh-Hans, zh-Hant, ko, es, fr, de, pt-BR, ru, ar, hi, vi, th, id, it, tr, pl, nl, sv (上位 20言語)
- コミュニティ駆動: 30以上の追加言語(Crowdin)

## ファイル命名
`Strings.<culture>.resx` 形式。`<culture>` は BCP-47 (例: `ja`, `en`, `zh-Hans`, `pt-BR`)

## 追加手順
1. `src/MWC.App/Resources/Strings.<culture>.resx` 作成
2. 既存 `Strings.resx` 全キー翻訳
3. ビルドして `bin/` 直下に `<culture>/MWC.resources.dll` 生成確認
4. PR 提出

## RTL対応
ar, fa, he はXAML側で:
```xml
FlowDirection="{Binding Source={x:Static Globalization:CultureInfo.CurrentUICulture}, Path=TextInfo.IsRightToLeft, Converter={StaticResource RtlConverter}}"
```

## フォント
`App.xaml` の `FontFamilyDefault` でフォールバックチェーン定義済。新言語追加時は専用 `FontFamilyXx` を必要に応じて追加。

## 翻訳ガイドライン
- UI文字列は **体言止め優先**(例: `Connect` = `接続`, NOT `接続します`)
- エラー文は2行以内
- ボタンラベル4文字目安
- プレースホルダ `{0}` 位置は言語都合で自由移動可

## 自動化
- GitHub Actions で resx → JSON 変換 → Crowdin 同期(将来)
- `dotnet tool install -g resx-translator` で en基準から自動翻訳ドラフト生成
