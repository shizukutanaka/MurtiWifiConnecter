#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/typecheck-core.sh — MWC.Core を **NuGet 無しで**コンパイルして型検査する。
#
# なぜ必要か:
#   このリポジトリの CI は一度も走っておらず、`dotnet build` も
#   `api.nuget.org` が塞がれた環境では restore 段階で失敗する。
#   そのため「コンパイルが通るか」は長らく誰も確かめていなかった。
#
#   しかし MWC.Core の外部依存は 2 つだけで、いずれも .NET SDK に同梱されている:
#     - System.Text.Json                      → Microsoft.NETCore.App に含まれる
#     - Microsoft.Extensions.Logging.Abstractions → Microsoft.AspNetCore.App に含まれる
#   よって SDK の参照アセンブリと Roslyn (csc) を直接叩けば、**ネットワーク無しで
#   Core 全体を型検査できる**。2026-08 にこれを実際にやったところ、
#   静的チェック 11 種が見逃していた**ビルドを落とす欠陥 3 件**が出た:
#     1. BeaconIeParser: `using System.Linq;` 欠落 → CS1929
#     2. RegulatoryDomainService: 位置引数レコードへ camelCase の名前付き引数 → CS1739
#     3. CertificateStoreService: X509Certificate2(byte[]) が obsolete → SYSLIB0057
#        (TreatWarningsAsErrors=true なので警告ではなくエラー)
#
# 限界(正直に):
#   - 参照は SDK に入っている **net10** の参照アセンブリ。実際の対象は net9.0 なので、
#     net10 で追加された API を誤って許してしまう可能性がある。**本物の
#     `dotnet build` の代わりにはならない。**
#   - 検査できるのは MWC.Core だけ。**他プロジェクトを同じ手で検査できないことは
#     確認済み**(推測ではない):
#       * Cli — SDK に System.CommandLine.dll は入っているが `SetHandler` を持たない
#         新 API 版で、本プロジェクトが固定する 2.0.0-beta4 と非互換。参照すると
#         偽のエラーが大量に出る。
#       * App — WPF (Microsoft.WindowsDesktop.App) の参照パックが未インストール。
#       * Platform.Windows — ManagedNativeWifi (NuGet) と Windows API が要る。
#       * tests — xunit / FluentAssertions / NSubstitute が要る。
#     参照無しの構文チェックだけは実施済みで、Cli/App とも**構文エラーはゼロ**。
#     ただし Core で見つかった 3 件はいずれも**束縛エラー**(CS1929/CS1739)であり、
#     構文チェックでは捕まらない。**Cli・App・tests の型検査は CI 待ちのまま**で、
#     残存リスクはそこに集中している。
#   - それでも「型が合うか」の下限は確認できる。CI が動くまでの繋ぎとして使う。
#
# 使い方: bash tools/typecheck-core.sh
# 終了コード: 0 = 成功 / 1 = コンパイルエラー / 2 = SDK が見つからない(スキップ)
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
cd "$(dirname "$0")/.."

DOTNET_ROOT_DIR=${DOTNET_ROOT:-/usr/lib/dotnet}
SDK_DIR=$(ls -d "$DOTNET_ROOT_DIR"/sdk/*/ 2>/dev/null | sort -V | tail -1)
CSC="${SDK_DIR}Roslyn/bincore/csc.dll"

if [ ! -f "$CSC" ]; then
  echo "SKIP: no .NET SDK found under $DOTNET_ROOT_DIR (nothing to type-check with)"
  exit 2
fi

NETREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.NETCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)
ASPREF=$(ls -d "$DOTNET_ROOT_DIR"/packs/Microsoft.AspNetCore.App.Ref/*/ref/net*/ 2>/dev/null | sort -V | tail -1)

if [ -z "$NETREF" ] || [ -z "$ASPREF" ]; then
  echo "SKIP: reference packs not installed (need Microsoft.NETCore.App.Ref and Microsoft.AspNetCore.App.Ref)"
  exit 2
fi

REFS=""
for f in "$NETREF"*.dll; do REFS="$REFS -r:$f"; done
REFS="$REFS -r:${ASPREF}Microsoft.Extensions.Logging.Abstractions.dll"

# ソースジェネレータ。無いと [LoggerMessage] / [GeneratedRegex] の partial が
# 実装無しとみなされ CS8795 が大量に出る(実際の欠陥ではない)。
GEN=""
for name in Microsoft.Extensions.Logging.Generators.dll System.Text.RegularExpressions.Generator.dll; do
  # packs/*/analyzers/dotnet/cs/ に置かれている。バージョンは複数あり得るので最新を採る。
  g=$(find "$DOTNET_ROOT_DIR/packs" -path '*/analyzers/dotnet/cs/*' -name "$name" 2>/dev/null | sort -V | tail -1)
  if [ -n "$g" ]; then
    GEN="$GEN -analyzer:$g"
  else
    echo "SKIP: source generator $name not found; [LoggerMessage]/[GeneratedRegex] partials would" >&2
    echo "      report CS8795 spuriously, so the result would be meaningless." >&2
    exit 2
  fi
done

OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT

# -warnaserror は Directory.Build.props の TreatWarningsAsErrors=true に合わせる。
# CS1591 (XML コメント欠落) だけは同様に除外する。
# shellcheck disable=SC2086
dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable \
  -warnaserror -nowarn:CS1591 \
  -out:"$OUT/MWC.Core.dll" $REFS $GEN \
  $(find src/MWC.Core -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*')
status=$?

if [ $status -eq 0 ]; then
  printf '\033[32mMWC.Core type-checks clean\033[0m (net10 reference assemblies; not a substitute for dotnet build)\n'
else
  printf '\033[31mMWC.Core failed to compile.\033[0m\n'
fi
exit $status
