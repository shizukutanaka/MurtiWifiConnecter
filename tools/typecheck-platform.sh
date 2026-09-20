#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/typecheck-platform.sh — MWC.Platform.Windows の全ファイルを検査する。
#
# 構成 (2026-09 実測):
#   HttpConnectivityChecker … BCL のみ (HttpClient)。スタブ不要。
#   DpapiSecretProtector    … ProtectedData のみ。公開された安定 BCL API で、
#                             署名を検査対象から逆算していないため循環しない。
#   WlanBssIeProvider       … 自前 P/Invoke (wlanapi.dll DllImport + 手書き構造体)。
#                             外部依存ゼロ。
#   WindowsWifiService      … ManagedNativeWifi 3.0.2 → 実ソース (github.com/
#                             emoacht/ManagedNativeWifi。csproj で Version 一致
#                             確認済み)から直接書き写した
#                             tools/stubs/ManagedNativeWifi.Stub.cs で検査。
#                             「検査対象から逆算」ではなく実ソースの引き写しなので
#                             循環しない。旧 ConnectionWaiter /
#                             NetworkStateChangedEventHandlerBridge は削除済み —
#                             実在しない `NativeWifi.NetworkStateChanged` static
#                             イベントへの依存が原因で検査できなかったため、
#                             実 API (ConnectNetworkAsync / NativeWifiPlayer) で
#                             再実装した。
#
# ★ 検査しないこと: WLAN API の実挙動、DPAPI のユーザーバウンド性、ネイティブ
#   構造体マーシャリングの正しさ (レイアウト一致・オフセット計算)。
#   いずれも Windows 実機でしか確かめられない。
#
# 使い方: bash tools/typecheck-platform.sh
#         bash tools/typecheck-platform.sh --selftest  … スタブの検出力を確認してから検査
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

# ── --selftest: ManagedNativeWifi スタブに実欠陥を検出する力が本当にあるか確認 ──
# (実 API の enum メンバー名を 1 つだけ壊して、WindowsWifiService.cs の検査が
#  ちゃんと落ちることを確かめてから戻す。third-party スタブは検査対象コードから
#  逆算していないため妥当だが、検出力そのものは別途実測しないと信用できない。)
if [ "${1:-}" = "--selftest" ]; then
  STUBFILE="tools/stubs/ManagedNativeWifi.Stub.cs"
  cp "$STUBFILE" "$OUT/mnw-backup.cs"
  # macOS の sed -i はバックアップ拡張子を必須とするため portable に .bak 経由で行う。
  sed -i.bak 's/RSNA_PSK,/RSNA_PSK_INJECTED_BREAK,/' "$STUBFILE" && rm -f "$STUBFILE.bak"
  # shellcheck disable=SC2086
  dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable -warnaserror -nowarn:CS1591 \
    -out:"$OUT/selftest.dll" $REFS -r:"$OUT/MWC.Core.dll" \
    tools/stubs/ImplicitUsings.Stub.cs "$STUBFILE" \
    src/MWC.Platform.Windows/WindowsWifiService.cs > "$OUT/selftest.log" 2>&1
  selftest_status=$?
  cp "$OUT/mnw-backup.cs" "$STUBFILE"
  if [ $selftest_status -eq 0 ]; then
    echo "SELFTEST FAILED: injected a broken enum member and the type check still passed."
    echo "The ManagedNativeWifi stub has no detection power — do not trust its results."
    exit 1
  fi
  echo "selftest OK: injected defect was caught (exit $selftest_status), stub restored."
fi

FILES=$(find src/MWC.Platform.Windows -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*')
total=$(echo "$FILES" | wc -l | tr -d ' ')

# shellcheck disable=SC2086
output=$(dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable \
  -warnaserror -nowarn:CS1591 \
  -out:"$OUT/plat.dll" $REFS -r:"$OUT/MWC.Core.dll" \
  tools/stubs/ImplicitUsings.Stub.cs tools/stubs/ProtectedData.Stub.cs \
  tools/stubs/ManagedNativeWifi.Stub.cs $FILES 2>&1)
status=$?
[ -n "$output" ] && echo "$output"

if [ $status -eq 0 ]; then
  printf '\033[32m%s of %s MWC.Platform.Windows files type-check clean\033[0m\n' "$total" "$total"
  exit 0
else
  printf '\033[31mMWC.Platform.Windows failed to type-check.\033[0m\n'
  exit 1
fi
