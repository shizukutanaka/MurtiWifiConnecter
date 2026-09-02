#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/mutation-check.sh — テストスイートに**検出力があるか**を突然変異で測る。
#
# なぜ在るか:
#   tools/run-tests.sh は xunit ではなく自前のランナーで、アサーションも
#   FluentAssertions の近似である。したがって「1000 件通った」だけでは
#   **本当にテストしているのか、通るだけの張りぼてなのか区別できない**。
#   これは本サイクルで繰り返し踏んだ罠 (型検査が何も束縛していないのに
#   「エラーゼロ」と出た件、アサーションが no-op だった件) と同じ形である。
#
#   そこで製品コードに**意図的な欠陥を注入**し、失敗数が増えるかを見る。
#   増えれば「殺した (killed)」= そのコード経路を実際に検証している証拠。
#   コメントだけを変える対照実験も含め、**何でも失敗させているのではない**ことも確かめる。
#
#   2026-08 の初回測定: 実質的な変異 5 件すべてを殺し、対照は生存 (期待どおり)。
#     MAC の LAA ビット 0x02→0x04      失敗 1 → 10
#     WMM 最小長 24→20                 失敗 1 → 2
#     ビーコン Vendor 要素 221→222     失敗 1 → 5
#     EvilTwin の IsSuspect 反転       失敗 1 → 7
#     WifiUri で WPA2 を Open に        失敗 1 → 2
#     対照 (コメントのみ変更)          失敗 1 → 1  ← 増えないのが正しい
#
#   注: ベースラインの失敗 1 件は既知のテスト間状態共有
#       (NetworkHistoryService_ConcurrentWrites_ThreadSafe)。
#
# 限界: 変異は手で選んだ代表例であり、網羅的な mutation testing ではない。
#       「検出力がゼロではない」ことの証拠であって、カバレッジの指標ではない。
#
# 使い方: bash tools/mutation-check.sh
# 終了コード: 0 = 全変異を殺し対照も生存 / 1 = 生き残りあり / 2 = 環境なし
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
cd "$(dirname "$0")/.."

command -v dotnet >/dev/null 2>&1 || { echo "SKIP: no dotnet"; exit 2; }
bash tools/run-tests.sh >/dev/null 2>&1
[ $? -eq 2 ] && { echo "SKIP: run-tests.sh cannot run here"; exit 2; }

failed_count() { bash tools/run-tests.sh 2>&1 | grep -oE 'failed [0-9]+' | awk '{print $2}'; }

BASE=$(failed_count)
echo "baseline failures: $BASE"
echo

rc=0
try() {  # file  from  to  label  expect(kill|survive)
  local file="$1" from="$2" to="$3" label="$4" expect="$5"
  cp "$file" "$file.mutbak"
  if ! python3 - "$file" "$from" "$to" <<'PY'
import sys
p, a, b = sys.argv[1], sys.argv[2], sys.argv[3]
s = open(p, encoding='utf-8').read()
if a not in s: raise SystemExit(1)
open(p, 'w', encoding='utf-8').write(s.replace(a, b, 1))
PY
  then
    # **SKIP を合格として扱わない。** 対象パターンが消えていれば、その変異は
    # 一度も試されていない。それを「殺した/生存した」と報告するのは
    # 検証していないことを検証したと言うのと同じ。
    printf '  \033[33mSKIP\033[0m   %-39s pattern no longer present — this mutant was NOT tested\n' "$label"
    skipped=$((skipped + 1))
    mv "$file.mutbak" "$file"; return
  fi

  local n; n=$(failed_count)
  mv "$file.mutbak" "$file"

  if [ "$expect" = "kill" ]; then
    if [ "${n:-0}" -gt "${BASE:-0}" ]; then printf '  \033[32mkilled \033[0m %-44s failures %s -> %s\n' "$label" "$BASE" "$n"
    else printf '  \033[31mSURVIVED\033[0m %-44s failures %s -> %s\n' "$label" "$BASE" "$n"; rc=1; fi
  else
    if [ "${n:-0}" -eq "${BASE:-0}" ]; then printf '  \033[32mcontrol ok\033[0m %-41s failures unchanged (%s)\n' "$label" "$n"
    else printf '  \033[31mCONTROL FAILED\033[0m %-37s failures %s -> %s (should be unchanged)\n' "$label" "$BASE" "$n"; rc=1; fi
  fi
}

skipped=0

try src/MWC.Core/Services/MacAddressModeInference.cs \
    "LocallyAdministeredBit = 0x02" "LocallyAdministeredBit = 0x04" \
    "MAC: locally-administered bit" kill
try src/MWC.Core/Services/WmmParser.cs \
    "private const int MinParamBodyLen = 24;" "private const int MinParamBodyLen = 20;" \
    "WMM: minimum parameter length" kill
try src/MWC.Core/Services/BeaconIeParser.cs \
    "private const byte VendorSpecificId = 221;" "private const byte VendorSpecificId = 222;" \
    "Beacon: vendor-specific element id" kill
try src/MWC.Core/Services/EvilTwinDetector.cs \
    "Risk != EvilTwinRisk.None" "Risk == EvilTwinRisk.None" \
    "EvilTwin: invert IsSuspect" kill
try src/MWC.Core/Profile/WifiUri.cs \
    '"WPA2"         => AuthMethod.WPA2PSK,' '"WPA2"         => AuthMethod.Open,' \
    "WifiUri: WPA2 parses as Open" kill
try src/MWC.Core/Services/MacAddressModeInference.cs \
    "///   - オクテット 0 の bit 1 = **Locally Administered (LAA)**。" \
    "///   - (control mutant: comment only)" \
    "CONTROL: comment-only edit" survive

echo
if [ $rc -eq 0 ] && [ "${skipped:-0}" -eq 0 ]; then
  printf '\033[32mevery mutant was killed and the control survived\033[0m — the suite has real detection power\n'
elif [ $rc -eq 0 ]; then
  printf '\033[33m%s mutant(s) were skipped\033[0m — their target text has drifted, so those paths are\n' "$skipped"
  printf 'NOT known to be verified. Update the patterns in this script before trusting the result.\n'
  rc=1
else
  printf '\033[31mmutation check failed\033[0m — a surviving mutant means those paths are not actually verified\n'
fi
exit $rc
