#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tools/verify.sh — dotnet SDK なしで実行できる静的検証をまとめて走らせる。
#
# なぜ必要か:
#   このリポジトリの CI は一度も実走していない (docs/FEATURE-AUDIT.md §0)。
#   加えて作業環境に dotnet SDK が無いことが多く、コンパイルもテストもできない。
#   その状況でも「壊したかどうか」を判定できる検証は実際にはかなりある —
#   XML の整形性、ロケールキーの一致、ソリューションの整合性、波括弧対応など。
#   本スクリプトはそれらを1コマンドに集約し、毎回手で打ち直す手間と打ち漏らしを無くす。
#
#   これは CI の代替ではない。CI が設置されるまでの下限を保証するものであり、
#   dotnet がある環境では `dotnet build` / `dotnet test` を必ず併用すること。
#
# 使い方:
#   bash tools/verify.sh          # 全チェック
#   bash tools/verify.sh --quiet  # 失敗のみ表示
#
# 終了コード: 0 = 全通過 / 1 = 1件以上失敗
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
cd "$(dirname "$0")/.."

QUIET=0
[ "${1:-}" = "--quiet" ] && QUIET=1

FAILED=0
pass() { [ "$QUIET" -eq 1 ] || printf '  \033[32mOK\033[0m   %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m %s\n' "$1"; FAILED=1; }
head() { [ "$QUIET" -eq 1 ] || printf '\n\033[1m%s\033[0m\n' "$1"; }

# ── 1. XML 整形性 (resx / XAML / csproj) ────────────────────────────────────
head "XML well-formedness"
if python3 - <<'PY'
import glob, sys, xml.etree.ElementTree as ET
bad = []
for pat in ('src/**/*.resx', 'src/**/*.xaml', '**/*.csproj', 'MWC.sln.DotSettings'):
    for p in glob.glob(pat, recursive=True):
        try: ET.parse(p)
        except Exception as e: bad.append(f'{p}: {e}')
if bad:
    print('\n'.join(bad)); sys.exit(1)
PY
then pass "all resx / xaml / csproj parse"
else fail "malformed XML (see above)"; fi

# ── 2. ロケールキーの一致 ────────────────────────────────────────────────────
# 翻訳漏れは実行時に例外ではなく「英語のまま表示」になるため、静的検査が要る。
head "Locale key consistency"
if python3 - <<'PY'
import glob, sys, xml.etree.ElementTree as ET
base_path = 'src/MWC.App/Resources/Strings.resx'
keys = lambda p: {e.get('name') for e in ET.parse(p).findall('.//data')}
base = keys(base_path)
bad = []
for p in sorted(glob.glob('src/MWC.App/Resources/Strings.*.resx')):
    missing, extra = base - keys(p), keys(p) - base
    if missing or extra:
        bad.append(f'{p}: missing={sorted(missing)[:5]} extra={sorted(extra)[:5]}')
if bad:
    print('\n'.join(bad)); sys.exit(1)
print(f'{len(base)} keys x {len(glob.glob("src/MWC.App/Resources/Strings.*.resx"))} locales')
PY
then pass "every locale matches the base resx"
else fail "locale key mismatch (see above)"; fi

# ── 3. ソリューション整合性 ─────────────────────────────────────────────────
# プロジェクト削除時に GUID 参照が残ると Visual Studio / dotnet build が壊れる。
head "Solution integrity"
if python3 - <<'PY'
import os, re, sys
sln = open('MWC.sln', encoding='utf-8-sig').read()
errs = []
if sln.count('\nProject(') + sln.startswith('Project(') != sln.count('\nEndProject'):
    errs.append('Project / EndProject count mismatch')
declared = set(re.findall(r'Project\("\{[^}]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+)",\s*"\{([^}]+)\}"', sln))
for path, guid in declared:
    p = path.replace('\\', os.sep)
    if p.endswith('.csproj') and not os.path.exists(p):
        errs.append(f'declared project missing on disk: {path}')
guids = {g for _, g in declared}
for g in set(re.findall(r'\{([0-9A-Fa-f-]{36})\}\.(?:Debug|Release)', sln)):
    if g not in guids:
        errs.append(f'config entry references unknown project GUID: {g}')
if errs:
    print('\n'.join(errs)); sys.exit(1)
print(f'{len(declared)} projects, all present, no dangling GUIDs')
PY
then pass "MWC.sln is internally consistent"
else fail "solution file problem (see above)"; fi

# ── 4. C# の波括弧対応 (警告のみ) ────────────────────────────────────────────
# 機械編集(sed / python での一括置換)による構造破壊を捕捉するための目安。
#
# **これは失敗にしない。** 正規表現で C# を字句解析することは原理的にできず、
# 補間文字列に文字列リテラルが入れ子になった形
#   $"[{i:D3}] {n.Ssid}{(cond ? "  x" : "")}"
# のような正当なコードで必ず誤検知する(実測: 196 ファイル中 1 件)。
# 誤検知でゲートすると「また誤検知だ」と無視する習慣がつき、チェック自体が無価値になる。
# 本当の構造検証はコンパイラの仕事なので、dotnet がある環境では build を必ず走らせること。
head "Brace balance (C#, advisory only)"
BRACE_OUT=$(python3 - <<'PY'
import glob, re
bad = []
for p in glob.glob('src/**/*.cs', recursive=True) + glob.glob('tests/**/*.cs', recursive=True):
    if '/obj/' in p or '/bin/' in p: continue
    s = open(p, encoding='utf-8').read()
    # 改行を跨がない文字列/文字リテラルと行コメントのみ除去。
    # @"..." 逐語文字列は改行を跨ぐため意図的に触らない。
    s = re.sub(r'"(?:\\.|[^"\\\n])*"', '""', s)
    s = re.sub(r"'(?:\\.|[^'\\\n])*'", "''", s)
    s = re.sub(r'//[^\n]*', '', s)
    if s.count('{') != s.count('}'):
        bad.append(f'{p}: {{={s.count("{")} }}={s.count("}")}')
print('\n'.join(bad))
PY
)
if [ -z "$BRACE_OUT" ]; then
  pass "all .cs files balance"
else
  [ "$QUIET" -eq 1 ] || printf '  \033[33mWARN\033[0m %s\n' "brace mismatch — verify by hand, may be a false positive:"
  [ "$QUIET" -eq 1 ] || echo "$BRACE_OUT" | sed 's/^/         /'
fi

# ── 5. シェル補完スクリプトの構文 ────────────────────────────────────────────
head "Shell completion syntax"
if bash -n completions/mwc.bash 2>/dev/null; then pass "completions/mwc.bash parses"
else fail "completions/mwc.bash has a syntax error"; fi

# ── 6. 孤立サービスの検出 ────────────────────────────────────────────────────
# 「実装されているが製品から到達できない」コードの再発防止。
# 既知の意図的な保持は docs/FEATURE-AUDIT.md §1a に理由付きで記載されている。
head "Orphaned Core services (informational)"
KNOWN="AccessibilityAuditService CaptivePortalService CatImportService Hotspot20Service"
NEW=""
for f in src/MWC.Core/Services/*.cs; do
  n=$(basename "$f" .cs)
  if [ "$(grep -rl "\b$n\b" src/ 2>/dev/null | grep -v "/$n.cs" | wc -l)" -eq 0 ]; then
    case " $KNOWN " in *" $n "*) ;; *) NEW="$NEW $n";; esac
  fi
done
if [ -z "$NEW" ]; then pass "no unexplained orphans (4 documented ones ignored)"
else fail "new orphaned service(s):$NEW — wire them, delete them, or document why in FEATURE-AUDIT §1a"; fi

# ── 結果 ─────────────────────────────────────────────────────────────────────
echo
if [ "$FAILED" -eq 0 ]; then
  printf '\033[32mAll static checks passed.\033[0m  (This is a floor, not a substitute for dotnet build/test.)\n'
else
  printf '\033[31mStatic checks failed.\033[0m\n'
fi
exit $FAILED
