#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/typecheck-platform.sh — MWC.Platform.Windows のうち、**循環せずに検査できる分**。
#
# なぜ 6 ファイル中 2 件だけなのか (2026-08 の実測。範囲を広げる前に読むこと):
#   ManagedNativeWifi (NuGet) を要するもの … 4 件
#     ConnectionWaiter / WindowsWifiService / WlanBssIeProvider /
#     NetworkStateChangedEventHandlerBridge
#     → スタブを書くと、**第三者ライブラリの API を検査対象のコードから逆算して
#       定義する**ことになる。そのスタブに対して通っても「私の推測どおりに
#       呼んでいる」ことしか分からず、本物の ManagedNativeWifi と合っている保証は無い。
#       WLAN の型は数十あり、誤りは静かに false negative になる。よって手を出さない。
#
#   検査できる 2 件:
#     HttpConnectivityChecker … BCL のみ (HttpClient)。スタブ不要。
#     DpapiSecretProtector    … ProtectedData のみ。これは**公開された安定した BCL API** で、
#                               署名を検査対象から逆算していないため循環しない
#                               (tools/stubs/ProtectedData.Stub.cs のヘッダ参照)。
#
#   この線引きは「スタブが検査対象のコードから導かれるか否か」で引いている。
#   導かれるなら検査は空になる。導かれないなら意味がある。
#
# ★ 検査しないこと: WLAN API の実挙動、DPAPI のユーザーバウンド性、Windows 固有の動作。
#   いずれも Windows 実機でしか確かめられない。
#
# 使い方: bash tools/typecheck-platform.sh
# 終了コード: 0 = 成功 / 1 = 型エラー / 2 = SDK 等が無くスキップ
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
cd "$(dirname "$0")/.."

. "$(dirname "$0")/lib/dotnet-env.sh"

OUT=$(mktemp -d); trap 'rm -rf "$OUT"' EXIT

# shellcheck disable=SC2086
dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable -nowarn:CS1591 \
  -out:"$OUT/MWC.Core.dll" $REFS $GEN \
  $(find src/MWC.Core -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*') > "$OUT/core.log" 2>&1 \
  || { echo "MWC.Core does not compile; run tools/typecheck-core.sh first"; head -5 "$OUT/core.log"; exit 1; }

FILES=""
for f in HttpConnectivityChecker.cs DpapiSecretProtector.cs; do
  [ -f "src/MWC.Platform.Windows/$f" ] && FILES="$FILES src/MWC.Platform.Windows/$f"
done
total=$(find src/MWC.Platform.Windows -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l)

# shellcheck disable=SC2086
output=$(dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable \
  -warnaserror -nowarn:CS1591 \
  -out:"$OUT/plat.dll" $REFS -r:"$OUT/MWC.Core.dll" \
  tools/stubs/ImplicitUsings.Stub.cs tools/stubs/ProtectedData.Stub.cs $FILES 2>&1)
status=$?
[ -n "$output" ] && echo "$output"

n=$(echo "$FILES" | wc -w)
if [ $status -eq 0 ]; then
  printf '\033[32m%s of %s MWC.Platform.Windows files type-check clean\033[0m (the ManagedNativeWifi-dependent remainder is NOT checked)\n' "$n" "$total"
else
  printf '\033[31mMWC.Platform.Windows failed to type-check.\033[0m\n'
fi
exit $status
