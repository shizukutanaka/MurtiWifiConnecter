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
#   - WPF / CommunityToolkit.Mvvm を使うファイル
#   - `using Serilog` を含むファイル … Serilog は NuGet で未入手
#   - `App.Version` を参照するファイル … `App` は WPF の Application 派生クラス
#
# スタブ: `NotificationService` のみ(Windows トーストに依存するが、対象ファイルは
#         型として参照するだけ)。署名は本物と一致させること。
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

if [ -z "$FILES" ]; then
  echo "SKIP: no WPF-free files found in src/MWC.App (the exclusion rules may need revisiting)"
  exit 2
fi

# shellcheck disable=SC2086
output=$(dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable \
  -warnaserror -nowarn:CS1591 \
  -out:"$OUT/app.dll" $REFS -r:"$OUT/MWC.Core.dll" \
  tools/stubs/ImplicitUsings.Stub.cs tools/stubs/MwcAppNotification.Stub.cs $FILES 2>&1)
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
