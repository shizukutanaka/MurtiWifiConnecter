#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/typecheck-platform.sh — MWC.Platform.Windows のうち、**循環せずに検査できる分**。
#
# なぜ 6 ファイル中 4 件だけなのか (2026-09 の実測。範囲を広げる前に読むこと):
#
#   ★ 以前は「ManagedNativeWifi を要するもの 4 件」とひとまとめにしていたが、
#     それは**個別に確認せず隣接ファイルから類推しただけ**だった。実際に 1 ファイルずつ
#     `using` を見たところ、`WlanBssIeProvider.cs` には `using ManagedNativeWifi` が
#     **無い** — 自前の P/Invoke (`wlanapi.dll` の DllImport + 手書きネイティブ構造体) のみで、
#     スタブすら要らずに単独でコンパイルできた(`-warnaserror` 込みで green、実測済み)。
#     この「未検証のまま隣と同じ扱いにする」は、本セッションが繰り返し戒めてきた誤りそのもの。
#
#   ★ 2026-09 追記: `WindowsWifiService.cs` は ManagedNativeWifi 3.0.2 の実ソース
#     (github.com/emoacht/ManagedNativeWifi。.csproj で Version 一致を確認済み)から
#     直接書き写した `tools/stubs/ManagedNativeWifi.Stub.cs` を使えば検査できる。
#     「検査対象のコードから逆算」ではなく実ソースの引き写しなので循環しない。
#     この検査導入で実際に 4 種の欠陥が見つかり修正済み(スタブのヘッダ参照)。
#     ConnectionWaiter / NetworkStateChangedEventHandlerBridge への依存
#     (ConnectionWaiter 型・NetworkStateChangedEventArgs 型など)は
#     `tools/stubs/PlatformWindowsLocalTypes.Stub.cs`(実ファイルからの複製、
#     同一プロジェクトの自製型なので循環しない)で隔離する — この 2 ファイル自体は
#     下記の理由で今も検査対象外のまま。
#
#   ManagedNativeWifi のイベント購読部分がまだ検査できない … 2 件
#     ConnectionWaiter / NetworkStateChangedEventHandlerBridge
#     → 実ソース確認済み: `NativeWifi.NetworkStateChanged` という static イベントは
#       存在しない(NativeWifi は 0 個の public event しか持たない static クラス)。
#       実際の通知は `NativeWifiPlayer`(instantiable, IDisposable)が公開する
#       7 種類の instance イベント。呼び出し元が期待する「1 イベント・static 購読」
#       という設計を「7 イベント・instance ライフサイクル」にどう対応させるかは
#       実機 Windows で検証できるセッションが決めるべき設計判断であり、ここで
#       スタブ側に都合よく定義してしまうと「直った」という誤った検査結果になる。
#       詳細: docs/COMPLETION-CHECKLIST.md §5。
#
#   検査できる 4 件:
#     HttpConnectivityChecker … BCL のみ (HttpClient)。スタブ不要。
#     DpapiSecretProtector    … ProtectedData のみ。これは**公開された安定した BCL API** で、
#                               署名を検査対象から逆算していないため循環しない
#                               (tools/stubs/ProtectedData.Stub.cs のヘッダ参照)。
#     WlanBssIeProvider       … 自前 P/Invoke のみ。スタブ不要、外部依存ゼロ。
#                               ⚠ ただしネイティブ構造体マーシャリングの**正しさ**
#                               (レイアウト一致・オフセット計算)自体は実機 Windows でしか
#                               確認できない。ここで確認できるのは「コンパイルが通る」まで。
#     WindowsWifiService      … 上記の 2 スタブを使って検査(2026-09 追加)。
#
#   この線引きは「スタブが検査対象のコードから逆算されているか否か」で引いている。
#   逆算なら検査は空になる。実ソースの引き写しなら意味がある。
#
# ★ 検査しないこと: WLAN API の実挙動、DPAPI のユーザーバウンド性、Windows 固有の動作、
#   ネイティブ構造体マーシャリングの正しさ、`NativeWifiPlayer` のイベント購読設計。
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
  sed -i 's/RSNA_PSK,/RSNA_PSK_INJECTED_BREAK,/' "$STUBFILE"
  # shellcheck disable=SC2086
  dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable -warnaserror -nowarn:CS1591 \
    -out:"$OUT/selftest.dll" $REFS -r:"$OUT/MWC.Core.dll" \
    tools/stubs/ImplicitUsings.Stub.cs "$STUBFILE" tools/stubs/PlatformWindowsLocalTypes.Stub.cs \
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

FILES=""
for f in HttpConnectivityChecker.cs DpapiSecretProtector.cs WlanBssIeProvider.cs; do
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

# WindowsWifiService.cs は ManagedNativeWifi + 自プロジェクトのローカル型に依存するため
# 別コンパイル単位で検査する(上の $FILES と混ぜると two スタブが無関係なファイルにも
# 効いてしまい紛らわしいため)。
# shellcheck disable=SC2086
wws_output=$(dotnet "$CSC" -nologo -nostdlib -target:library -langversion:12 -nullable:enable \
  -warnaserror -nowarn:CS1591 \
  -out:"$OUT/wws.dll" $REFS -r:"$OUT/MWC.Core.dll" \
  tools/stubs/ImplicitUsings.Stub.cs tools/stubs/ManagedNativeWifi.Stub.cs tools/stubs/PlatformWindowsLocalTypes.Stub.cs \
  src/MWC.Platform.Windows/WindowsWifiService.cs 2>&1)
wws_status=$?
[ -n "$wws_output" ] && echo "$wws_output"

if [ $status -eq 0 ] && [ $wws_status -eq 0 ]; then
  n=$(($(echo "$FILES" | wc -w) + 1))
  printf '\033[32m%s of %s MWC.Platform.Windows files type-check clean\033[0m (ConnectionWaiter / NetworkStateChangedEventHandlerBridge の 2 件は既知の理由で検査対象外)\n' "$n" "$total"
  exit 0
else
  printf '\033[31mMWC.Platform.Windows failed to type-check.\033[0m\n'
  exit 1
fi
