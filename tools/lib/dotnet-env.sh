# shellcheck shell=bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/lib/dotnet-env.sh — 型検査・テスト実行スクリプトが共有する .NET 環境の探索。
#
# なぜ在るか:
#   typecheck-{core,cli,app-services,tests,platform}.sh と run-tests.sh の 6 本が
#   まったく同じ前置き(SDK・Roslyn・参照パック・ソースジェネレータの探索)を
#   各自で持っていた。合計 200 行あまりの重複で、SDK の配置が変わったら 6 箇所を
#   直すことになる。本セッションが繰り返し潰してきた「同じ事実を N 箇所が宣言する」
#   欠陥そのものであり、しかも**同じセッションで自分が作り込んだもの**。
#   CLAUDE.md の「同じ機能を 2 通りで書けるなら単純な方」に従って 1 箇所へ集約する。
#
# 使い方: スクリプト冒頭で
#     . "$(dirname "$0")/lib/dotnet-env.sh"
#   すると次が定義される。見つからなければ exit 2 (= SKIP) で抜ける。
#     $CSC     … Roslyn コンパイラ (dotnet $CSC で起動)
#     $REFS    … -r: 参照の並び (BCL + Microsoft.Extensions.*)
#     $GEN     … -analyzer: ソースジェネレータの並び
#     $NETREF / $ASPREF … 参照パックのディレクトリ
#
# 注意: `dotnet build` は使わない。global.json が SDK 9 を要求する一方でこの環境には
#   SDK 10 しか無く、SDK 解決の時点で失敗する。csc を直接叩くことでそれを回避している。
#   (`dotnet workload` 等の SDK コマンドが動かないのも同じ理由。)
# ─────────────────────────────────────────────────────────────────────────────

DOTNET_ROOT_DIR=${DOTNET_ROOT:-/usr/lib/dotnet}
SDK_DIR=$(ls -d "$DOTNET_ROOT_DIR"/sdk/*/ 2>/dev/null | sort -V | tail -1)
CSC="${SDK_DIR}Roslyn/bincore/csc.dll"
NETREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.NETCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)
ASPREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.AspNetCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)
# 実行時の共有フレームワーク。run-tests.sh がビルド済みアセンブリの隣へ
# 実体 DLL を置くのに要る (参照アセンブリは実行できないため)。
SHARED=$(ls -d "$DOTNET_ROOT_DIR"/shared/Microsoft.AspNetCore.App/*/ 2>/dev/null | sort -V | tail -1)
RUNTIME_VER=$(basename "$(ls -d "$DOTNET_ROOT_DIR"/shared/Microsoft.NETCore.App/*/ 2>/dev/null | sort -V | tail -1)")

if [ ! -f "$CSC" ] || [ -z "$NETREF" ] || [ -z "$ASPREF" ]; then
  echo "SKIP: .NET SDK or reference packs not available under $DOTNET_ROOT_DIR"
  exit 2
fi

REFS=""
for _f in "$NETREF"*.dll; do REFS="$REFS -r:$_f"; done
# Logging.Console は MWC.Cli の AddSimpleConsole が要る。集約時に落として
# CLI の型検査が壊れた (統合直後の全数実行で検出)。和集合を持つこと。
for _d in Microsoft.Extensions.Logging.Abstractions Microsoft.Extensions.DependencyInjection.Abstractions \
          Microsoft.Extensions.DependencyInjection Microsoft.Extensions.Logging \
          Microsoft.Extensions.Logging.Console Microsoft.Extensions.Options; do
  [ -f "$ASPREF$_d.dll" ] && REFS="$REFS -r:$ASPREF$_d.dll"
done

# ソースジェネレータ。無いと [LoggerMessage] / [GeneratedRegex] の partial が
# 実装無しとみなされ CS8795 が大量に出る(実際の欠陥ではない)。
GEN=""
for _name in Microsoft.Extensions.Logging.Generators.dll System.Text.RegularExpressions.Generator.dll; do
  _g=$(find "$DOTNET_ROOT_DIR/packs" -path '*/analyzers/dotnet/cs/*' -name "$_name" 2>/dev/null | sort -V | tail -1)
  [ -n "$_g" ] && GEN="$GEN -analyzer:$_g"
done
unset _f _d _g _name
