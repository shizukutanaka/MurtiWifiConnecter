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

# ── 3b. ソリューションフィルタ (*.slnf) ─────────────────────────────────────
# CI は .slnf 経由で restore/build する (docs/ci/ci.yml)。プロジェクトを削除したとき
# .sln だけ直して .slnf を忘れると、**CI を設置した瞬間に dotnet restore が失敗する**。
# 2026-07 第4パスで実際に踏んだ (Android/iOS 削除時に 2 つの .slnf に参照が残った)。
head "Solution filters (*.slnf)"
if python3 - <<'PY'
import glob, json, os, sys
errs = []
for p in sorted(glob.glob('*.slnf')):
    try: d = json.load(open(p))
    except Exception as e: errs.append(f'{p}: invalid JSON: {e}'); continue
    sol = d.get('solution', {})
    if not os.path.exists(sol.get('path', '').replace('\\', os.sep)):
        errs.append(f"{p}: solution path missing: {sol.get('path')}")
    for proj in sol.get('projects', []):
        if not os.path.exists(proj.replace('\\', os.sep)):
            errs.append(f'{p}: references missing project: {proj}')
if errs:
    print('\n'.join(errs)); sys.exit(1)
print(f'{len(glob.glob("*.slnf"))} filter(s), every referenced project exists')
PY
then pass "all .slnf reference existing projects"
else fail "solution filter references a missing project — CI would fail on restore"; fi

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

# ── 7. README に書かれた数値が実測と一致するか ──────────────────────────────
# README は製品の顔であり、古い数値は「検証されていない主張」になる。
# 2026-07 に実際に 3 箇所ずれていた (テスト数・キー数・総エントリ数)。
head "README claims match reality"
if python3 - <<'PY'
import glob, os, re, sys, xml.etree.ElementTree as ET
r = open('README.md', encoding='utf-8').read()
keys = len(ET.parse('src/MWC.App/Resources/Strings.resx').findall('.//data'))
files = len(glob.glob('src/MWC.App/Resources/Strings*.resx'))
locales = files - 1                      # 中立ベースを除いた名前付きロケール数
errs = []

m = re.search(r'1ファイル\s*([\d,]+)\s*キー', r)
if m and int(m.group(1).replace(',', '')) != keys:
    errs.append(f'README claims {m.group(1)} resx keys, actual {keys}')

m = re.search(r'=\s*([\d,]+)\s*エントリ', r)
if m and int(m.group(1).replace(',', '')) != keys * files:
    errs.append(f'README claims {m.group(1)} total entries, actual {keys*files} ({keys}x{files})')

m = re.search(r'i18n-(\d+)%20langs', r)
if m and int(m.group(1)) != locales:
    errs.append(f'i18n badge claims {m.group(1)} langs, actual {locales} named locales')

# チェックリストにテスト数をハードコードすると、README と別々に腐る。
# 実際に 2026-07 に「881」が取り残された(実測は 887)。数値の単一の真実の源は
# README(このチェックが実測と突き合わせる場所)に一本化し、複製を禁じる。
cl_path = 'docs/COMPLETION-CHECKLIST.md'
if os.path.exists(cl_path):
    cl = open(cl_path, encoding='utf-8').read()
    dup = re.search(r'(\d{3,})\s*(?:の)?テストメソッド', cl)
    if dup:
        errs.append(
            f'COMPLETION-CHECKLIST.md hardcodes a test count ({dup.group(1)}); '
            'reference the README badge instead so it cannot drift')

# i18n の数値は README だけを守っても腐る。バッジのキー数はこれまで無検査で、
# 実測 532 に対し 526 のまま取り残されていた。ハンドブックと architecture.md にも
# 複製された古い値(526 / 171キー / 2,052エントリ)が残っていた。
# ドキュメント全体を走査する。ただし **日付を含む行は過去の作業記録**であり
# 現在値と一致する必要がないので除外する(例: 「2026-07 に 274キー×3言語を補完」)。
m = re.search(r'i18n-\d+%20langs%20%C2%B7%20(\d+)%20keys', r)
if m and int(m.group(1)) != keys:
    errs.append(f'i18n badge claims {m.group(1)} keys, actual {keys}')

for p in ['README.md'] + sorted(glob.glob('docs/*.md')):
    for i, line in enumerate(open(p, encoding='utf-8'), 1):
        if re.search(r'20\d\d-\d\d', line):
            continue
        for mm in re.finditer(r'([\d,]{2,})\s*キー', line):
            if int(mm.group(1).replace(',', '')) != keys:
                errs.append(f'{p}:{i} claims {mm.group(1)} resx keys, actual {keys}')
        for mm in re.finditer(r'([\d,]{2,})\s*エントリ', line):
            if int(mm.group(1).replace(',', '')) != keys * files:
                errs.append(f'{p}:{i} claims {mm.group(1)} entries, actual {keys*files}')

# DI 登録数も architecture.md の見出しに書かれており、同様に腐っていた (29 → 実測 31)。
app = open('src/MWC.App/App.xaml.cs', encoding='utf-8').read()
di = len(re.findall(r'Add(?:Singleton|Transient|Scoped)<', app))
arch = open('docs/architecture.md', encoding='utf-8').read()
m = re.search(r'##\s*DI\s*\((\d+)\s*サービス\)', arch)
if m and int(m.group(1)) != di:
    errs.append(f'architecture.md claims {m.group(1)} DI services, actual {di}')

m = re.search(r'tests-(\d+)%20', r)
if m:
    n = sum(open(p, encoding='utf-8').read().count(t)
            for p in glob.glob('tests/**/*.cs', recursive=True)
            for t in ('[Fact]', '[Theory]'))
    if int(m.group(1)) != n:
        errs.append(f'tests badge claims {m.group(1)} methods, actual {n}')

if errs:
    print('\n'.join(errs)); sys.exit(1)
print(f'{keys} keys x {files} files, {locales} named locales — README agrees')
PY
then pass "README's numbers match the repository"
else fail "README states a number that no longer holds (see above)"; fi

# ── 結果 ─────────────────────────────────────────────────────────────────────
echo
if [ "$FAILED" -eq 0 ]; then
  printf '\033[32mAll static checks passed.\033[0m  (This is a floor, not a substitute for dotnet build/test.)\n'
else
  printf '\033[31mStatic checks failed.\033[0m\n'
fi
exit $FAILED
