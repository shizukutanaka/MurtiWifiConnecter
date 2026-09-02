#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/typecheck-app-services.sh — MWC.App のうち **WPF に依存しない層**を型検査する。
#
# なぜ部分的なのか:
#   MWC.App 全体は WPF (Microsoft.WindowsDesktop.App 参照パック — 未インストール) と
#   CommunityToolkit.Mvvm (NuGet — `api.nuget.org` がエグレス拒否) を必要とし、
#   この環境ではコンパイルできない。46 ファイル中 40 は WPF/Mvvm に触れている。
#
#   しかし残る数ファイルは**純粋なサービス層**で、Core と BCL だけで型検査できる。
#   そこには `AutoReconnectService`(自動再接続のバックオフ・evil twin 防御・
#   ベースライン永続化)や `SettingsService`、`L.cs`(resx アクセサ全件)が含まれ、
#   ロジック量も変更頻度も高い。全く検査しないより、ここだけでも押さえる価値がある。
#
# 対象から外すもの(理由つき):
#   - `*.xaml.cs`             … XAML 生成の partial が無いと CS0759 が出る
#   - CommunityToolkit.Mvvm を使うファイル … ソースジェネレータの出力が要る
#   - ダイアログ/ViewModel/WinForms に依存するファイル … スタブを書くと
#     **呼び出し側が使う署名をこちらで定義する**ことになり検査が循環する
#     (どのファイルがなぜ外れるかの実測は tools/stubs/WpfMinimal.Stub.cs のヘッダ)
#
# スタブ: `NotificationService`、`Serilog`、および WPF のごく一部
#         (`WpfMinimal.Stub.cs`: Window / Application / Clipboard / DataObject /
#          RoutedEventArgs / TextBlock / Automation の通知列挙)。
#         **WPF スタブを育てないこと** — 育てたくなったら本物の参照パックを用意する合図。
#         署名は本物と一致させること。
#
# 2026-08 にこの範囲を広げた際、`AccessibilityService` の
# `using System.Windows.Automation.Peers;` 欠落 (CS0246) が実際に見つかった。
# 同ファイルは下の行で `Peers.UIElementAutomationPeer` を完全修飾しており、
# 名前空間が別なのを知りながら列挙 2 つの using だけ落としていた。
#
# ★ 通っても保証されないこと: WPF 側のコード、XAML との対応、実行時挙動。
#   **App の大部分は依然として未検査**であり、CI が初めて走るときの主な риск源である。
#
# 使い方: bash tools/typecheck-app-services.sh
# 終了コード: 0 = 成功 / 1 = 型エラー / 2 = SDK 等が無くスキップ
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
cd "$(dirname "$0")/.."

DOTNET_ROOT_DIR=${DOTNET_ROOT:-/usr/lib/dotnet}
SDK_DIR=$(ls -d "$DOTNET_ROOT_DIR"/sdk/*/ 2>/dev/null | sort -V | tail -1)
CSC="${SDK_DIR}Roslyn/bincore/csc.dll"
NETREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.NETCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)
ASPREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.AspNetCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)

if [ ! -f "$CSC" ] || [ -z "$NETREF" ] || [ -z "$ASPREF" ]; then
  echo "SKIP: .NET SDK or reference packs not available"
  exit 2
fi

REFS=""
for f in "$NETREF"*.dll; do REFS="$REFS -r:$f"; done
for d in Microsoft.Extensions.Logging.Abstractions Microsoft.Extensions.DependencyInjection.Abstractions \
         Microsoft.Extensions.DependencyInjection Microsoft.Extensions.Logging Microsoft.Extensions.Options; do
  [ -f "$ASPREF$d.dll" ] && REFS="$REFS -r:$ASPREF$d.dll"
done

GEN=""
g=$(find "$DOTNET_ROOT_DIR/packs" -path '*/analyzers/dotnet/cs/*' -name 'Microsoft.Extensions.Logging.Generators.dll' 2>/dev/null | sort -V | tail -1)
[ -n "$g" ] && GEN="-analyzer:$g"
g2=$(find "$DOTNET_ROOT_DIR/packs" -path '*/analyzers/dotnet/cs/*' -name 'System.Text.RegularExpressions.Generator.dll' 2>/dev/null | sort -V | tail -1)
[ -n "$g2" ] && GEN="$GEN -analyzer:$g2"

OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT

# shellcheck disable=SC2086
dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable -nowarn:CS1591 \
  -out:"$OUT/MWC.Core.dll" $REFS $GEN \
  $(find src/MWC.Core -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*') > "$OUT/core.log" 2>&1
if [ $? -ne 0 ]; then
  echo "MWC.Core does not compile; run tools/typecheck-core.sh first"; head -5 "$OUT/core.log"; exit 1
fi

EXCLUDE='using System\.Windows|using CommunityToolkit|ObservableProperty|RelayCommand|System\.Windows\.|using Serilog|\bApp\.Version\b'
FILES=""
for f in $(find src/MWC.App -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -not -name '*.xaml.cs'); do
  grep -qE "$EXCLUDE" "$f" || FILES="$FILES $f"
done

# WPF を使うが tools/stubs/WpfMinimal.Stub.cs の**ごく小さな**面だけで足りる 3 件。
# 追加の可否は実測で決めた: 他の WPF 依存ファイルはダイアログ/ViewModel/WinForms を
# 必要とし、スタブを書くと検査が循環する (WpfMinimal.Stub.cs のヘッダに詳細)。
# KeyboardShortcutService は Key/ModifierKeys 列挙のみを要する。
# その 2 つは WPF の**公表された定義**を WpfMinimal.Stub.cs が再現しているため
# 循環しない (コードに合わせてメンバを足していない点が重要)。
for extra in Services/SensitiveClipboard.cs Services/AsyncEventHelper.cs Services/AccessibilityService.cs \
             Services/KeyboardShortcutService.cs; do
  [ -f "src/MWC.App/$extra" ] && FILES="$FILES src/MWC.App/$extra"
done

# ViewModel 群。CommunityToolkit.Mvvm のソースジェネレータ出力を
# tools/stubs/MvvmGenerate.py が**公表された命名規約どおりに**再現する
# (循環しない理由は同スクリプトの docstring)。
# AllAdaptersOverviewViewModel だけは MWC.App.Views (XAML ダイアログ) を要するため外す。
VM=""
for f in $(grep -rlE 'ObservableProperty|RelayCommand|ObservableObject' src/MWC.App --include=*.cs \
           | grep -v '\.xaml\.cs' | grep -v AllAdaptersOverviewViewModel); do
  VM="$VM $f"
done
GENSRC=""
if [ -n "$VM" ] && command -v python3 > /dev/null 2>&1; then
  # shellcheck disable=SC2086
  if python3 tools/stubs/MvvmGenerate.py "$OUT/mvvm.g.cs" $VM > /dev/null 2>&1; then
    GENSRC="$OUT/mvvm.g.cs"
    FILES="$FILES $VM"
  fi
fi

if [ -z "$FILES" ]; then
  echo "SKIP: no WPF-free files found in src/MWC.App (the exclusion rules may need revisiting)"
  exit 2
fi

# shellcheck disable=SC2086
output=$(dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable \
  -warnaserror -nowarn:CS1591 \
  -out:"$OUT/app.dll" $REFS -r:"$OUT/MWC.Core.dll" \
  tools/stubs/ImplicitUsings.Stub.cs tools/stubs/MwcAppNotification.Stub.cs \
  tools/stubs/WpfMinimal.Stub.cs tools/stubs/Serilog.Stub.cs tools/stubs/Mvvm.Stub.cs \
  $GENSRC $FILES 2>&1)
status=$?
[ -n "$output" ] && echo "$output"

n=$(echo "$FILES" | wc -w)
total=$(find src/MWC.App -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l)
if [ $status -eq 0 ]; then
  printf '\033[32m%s of %s MWC.App files type-check clean\033[0m (the WPF-dependent remainder is NOT checked)\n' "$n" "$total"
else
  printf '\033[31mMWC.App service layer failed to type-check.\033[0m\n'
fi
exit $status
