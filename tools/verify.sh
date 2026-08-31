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
#
# 参照判定には 2 つの落とし穴があり、両方とも実際に踏んだ:
#
#   (a) コメントを参照と数えていた。ドキュメントコメントで名前に言及しているだけの
#       ファイルを「配線済み」と誤判定する。これに隠れて、WMM デコードが 2 箇所に
#       重複していた事実が見えなくなっていた。行コメントは参照に数えない。
#       (除外の正規表現は行頭に錨を打つこと。`://` に一致して正当な行を巻き込む)
#
#   (b) 型名だけを探していた。**拡張メソッドを収めた static クラスは型名が
#       呼び出し側に現れない** — `BeaconIeApplier` は `net.WithBeaconIe(...)` と
#       書かれるので、型名 grep では永久に「孤立」に見える。static クラスに限り、
#       宣言している public メンバ名でも参照とみなす(`Parse` のような短い名前は
#       どこにでも出るため 6 文字以上に限る)。
#
# 許可リストは**両方向**に検査する。片方向だと、配線済みになったサービスが
# 許可リストに残り続け、後で配線を外されても黙って通る。実際にそうなっていた:
# `CatImportService`(`mwc import-cat`)と `Hotspot20Service`(`mwc passpoint`)は
# 配線されたのに許可リストに残っており、再び孤立させても検出できない状態だった。
head "Orphaned Core services"
if python3 - <<'PY'
import glob, os, re, sys

KNOWN = {'AccessibilityAuditService', 'CaptivePortalService'}

def code_lines(path):
    return [l for l in open(path, encoding='utf-8', errors='replace')
            if not l.lstrip().startswith('//')]

sources = {p: code_lines(p) for p in glob.glob('src/**/*.cs', recursive=True)
           if '/obj/' not in p and '/bin/' not in p}

MEMBER_RE = re.compile(r'^\s*public\s+(?:static\s+)?[\w<>,\[\]\?\. ]+?\s(\w+)\s*[\(=]', re.M)

def referenced(name, own_path):
    body = ''.join(''.join(v) for k, v in sources.items() if k != own_path)
    if re.search(r'\b%s\b' % re.escape(name), body):
        return True
    src = ''.join(sources[own_path])
    if not re.search(r'\bstatic\s+class\s+%s\b' % re.escape(name), src):
        return False
    for member in MEMBER_RE.findall(src):
        if len(member) >= 6 and re.search(r'\b%s\b' % re.escape(member), body):
            return True
    return False

orphans, stale = [], []
for path in sorted(glob.glob('src/MWC.Core/Services/*.cs')):
    name = os.path.basename(path)[:-3]
    if referenced(name, path):
        if name in KNOWN:
            stale.append(name)
    else:
        orphans.append(name)

fresh = [n for n in orphans if n not in KNOWN]
if fresh:
    print('new orphaned service(s): ' + ' '.join(fresh))
    print('  -> wire them, delete them, or document why in FEATURE-AUDIT 1a')
    sys.exit(1)
if stale:
    print('allowlist is stale - now referenced: ' + ' '.join(stale))
    print('  -> drop from KNOWN in tools/verify.sh and update FEATURE-AUDIT 1a')
    sys.exit(1)
print(f'{len(orphans)} documented orphan(s) ignored, no new ones')
PY
then pass "orphan set matches the documented allowlist"
else fail "orphan check (see above)"; fi

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
    cnt = re.search(r'(\d+)\s*チェック', cl)
    if cnt:
        errs.append(
            f'COMPLETION-CHECKLIST.md hardcodes a verify.sh check count ({cnt.group(1)}); '
            'checks are added often — describe it without a number')
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

# ADR 件数も README が主張する数値。ファイルを数えれば実測できるので固定する。
adrs = len([q for q in glob.glob('docs/adr/*.md')
            if not os.path.basename(q).lower().startswith('readme')])
m = re.search(r'アーキテクチャ決定記録\s*\((\d+)\s*件\)', r)
if m and int(m.group(1)) != adrs:
    errs.append(f'README claims {m.group(1)} ADRs, actual {adrs}')

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

# ── 8. ショートカット: ヘルプの一覧と実装が一致するか ──────────────────────
# F1 ヘルプが出す一覧 (KeyboardShortcutService.BuildDefinitions) と、実際にキーを
# 処理するスイッチ (MainWindow.OnKeyDown) は**別々に書かれた 2 つの表**であり、
# 片方だけ編集すると黙って食い違う。実際に食い違っていた:
#   - Ctrl+Tab / Ctrl+Shift+Tab … ヘルプにはあるがハンドラが無く、押しても無反応
#     (アダプタータブは TabControl ではなく ListBox なので WPF も面倒を見ない)
#   - Ctrl+Shift+A … 動作するのにヘルプに載っていない
# README は「キーボードのみで完全操作可能」「WCAG 2.1 AAA」を掲げているので、
# 案内したキーが動かないのは単なる不備ではなく、主張の裏切りにあたる。
#
# 例外: Up / Down は ListBox の標準操作で WPF 自身が処理する。ハンドラに現れないのが
# 正しいので、ここで明示的に除外する(除外理由をコードに残すこと自体が目的)。
head "Keyboard shortcuts: help list matches handler"
if python3 - <<'PY'
import re, sys

sv = open('src/MWC.App/Services/KeyboardShortcutService.cs', encoding='utf-8').read()
mw = open('src/MWC.App/MainWindow.xaml.cs', encoding='utf-8').read()

def norm(mods):
    return ' | '.join(sorted(p.strip() for p in mods.split('|')))

advertised = {(k, norm(m)) for k, m in
              re.findall(r'new\(Category\.\w+,\s*Key\.(\w+),\s*((?:ModifierKeys\.\w+\s*\|?\s*)+),', sv)}
implemented = {(k, norm(m)) for k, m in
               re.findall(r'case\s*\(Key\.(\w+),\s*((?:ModifierKeys\.\w+\s*\|?\s*)+)\)', mw)}

# WPF の ListBox が標準で処理するキー。ハンドラに無いのが正しい。
NATIVE = {('Up', 'ModifierKeys.None'), ('Down', 'ModifierKeys.None')}

missing = advertised - implemented - NATIVE
extra   = implemented - advertised

errs = []
for k, m in sorted(missing):
    errs.append(f'advertised in F1 help but no handler: {m.replace("ModifierKeys.","")}+{k}')
for k, m in sorted(extra):
    errs.append(f'handled but missing from F1 help: {m.replace("ModifierKeys.","")}+{k}')

if not advertised or not implemented:
    errs.append('parsed 0 shortcuts from one of the two files — the check itself is broken')

if errs:
    print('\n'.join(errs)); sys.exit(1)
print(f'{len(advertised)} advertised, {len(implemented)} handled, {len(NATIVE)} native — consistent')
PY
then pass "every advertised shortcut has a handler, and vice versa"
else fail "shortcut help and handler disagree (see above)"; fi

# ── 9. CLI コマンド: 実装と補完スクリプトが一致するか ──────────────────────
# 実装 (Program.cs の root.AddCommand)、bash 補完、PowerShell 補完 ─ 同じ事実を
# 宣言する表が 3 つある。コマンドを足すとき補完を忘れても何も壊れないので、
# 「動くのに Tab で出てこない」状態が静かに生まれる。実際にそうなっていた:
# `eap-stats` と `vpn-advice` は実装も README への記載もあるのに、
# 両方の補完スクリプトから漏れていた。
head "CLI commands match both completion scripts"
if python3 - <<'PY'
import glob, re, sys
files = {p: open(p, encoding='utf-8').read() for p in glob.glob('src/MWC.Cli/*.cs')}
prog = files['src/MWC.Cli/Program.cs']

def locate(meth, cls=None):
    # Program は partial class なので、メソッドは CLI 内のどのファイルにもあり得る
    for path, body in files.items():
        if cls and f'class {cls}' not in body: continue
        d = re.search(r'\b(?:private|public|internal)[\w\s]*\b%s\s*\(' % re.escape(meth), body)
        if not d: continue
        rest = body[d.end():]
        nxt = re.search(r'\n    (?:private|public|internal)\s', rest)
        return path, (rest[:nxt.start()] if nxt else rest)
    return None, None

impl = set()
impl_opts = {}
unresolved = []
for c in re.findall(r'root\.AddCommand\(\s*([\w\.]+)\s*\(', prog):
    cls, meth = (c.split('.', 1) if '.' in c else (None, c))
    path, seg = locate(meth, cls)
    name = None
    if seg:
        n = re.search(r'new Command\(\s*"([\w-]+)"', seg)
        if n:
            name = n.group(1)
            opts = set(re.findall(r'"(--[a-z][\w-]*)"', seg))
            # サブコマンドを別メソッドに分けているコンテナは定義ファイル全体を見る
            if path != 'src/MWC.Cli/Program.cs':
                opts |= set(re.findall(r'"(--[a-z][\w-]*)"', files[path]))
            impl_opts[name] = opts
    (impl.add(name) if name else unresolved.append(c))

errs = []
if unresolved:
    errs.append('could not resolve command name for: ' + ' '.join(unresolved)
                + ' — fix this check, do not ignore it')
if len(impl) < 5:
    errs.append(f'only {len(impl)} commands parsed — the check itself is broken')

b = open('completions/mwc.bash', encoding='utf-8').read()
m = re.search(r'local commands="([^"]+)"', b)
bash_l = set(m.group(1).split()) if m else set()
if not m: errs.append('completions/mwc.bash: could not find the command list')

ps = open('completions/mwc.ps1', encoding='utf-8').read()
try:
    seg = ps[ps.index('$commands = @('):]
    ps_l = set(re.findall(r"'([\w-]+)'", seg[:seg.index(')')]))
except ValueError:
    ps_l = set(); errs.append('completions/mwc.ps1: could not find the command list')

for label, have in (('mwc.bash', bash_l), ('mwc.ps1', ps_l)):
    for n in sorted(impl - have):
        errs.append(f'{label}: command implemented but not completable: {n}')
    for n in sorted(have - impl - {'help'}):
        errs.append(f'{label}: completes a command that does not exist: {n}')

# オプションは **一方向だけ** 検査する。存在しないフラグを Tab で勧めるのは
# 利用者を直接誤らせる (実測: `mwc list --adapter` を両スクリプトが勧めていたが
# list の実装は --json / --status しか持たない = 補完に従うとパースエラー)。
# 逆向き (実装にあるが補完に無い) は不便なだけで害が無く、サブコマンドを持つ
# コンテナコマンドで誤検知が出るため、あえて見ない。
bash_opts = {m.group(1): set(re.findall(r'(--[a-z][\w-]*)', m.group(2)))
             for m in re.finditer(r'^\s{8}([\w-]+)\)\n(.*?)\n\s{12};;', b, re.S | re.M)}
ps_opts = {m.group(1): set(re.findall(r"'(--[a-z][\w-]*)'", m.group(2)))
           for m in re.finditer(r"'([\w-]+)'\s*\{([^}]*)\}", ps)}
for cmd in sorted(impl_opts):
    for label, tbl in (('mwc.bash', bash_opts), ('mwc.ps1', ps_opts)):
        for o in sorted(tbl.get(cmd, set()) - impl_opts[cmd]):
            errs.append(f'{label}: `mwc {cmd} {o}` is completable but no such option exists')

if errs:
    print('\n'.join(errs)); sys.exit(1)
print(f'{len(impl)} commands, both completion scripts agree')
PY
then pass "every CLI command is completable, and vice versa"
else fail "CLI commands and completions disagree (see above)"; fi

# ── 10. resx: 定義されたキーが実際に使われているか ─────────────────────────
# 参照→定義の向き(存在しないキーを L.Get する typo)は起きれば英語フォールバックで
# 露見するが、**定義→参照の向きは誰も見ていなかった**。使われないキーは 15 ロケール分の
# 死んだエントリになり、翻訳者は永遠にそれを訳し続ける。実測で 18 キー × 15 = 270
# エントリが死んでいた (Auth_* は SecurityBadgeService の人間語ラベルに、Label_* の
# 詳細ペイン系は Detail* 群に置き換えられた取り残し)。
#
# **動的プレフィックスの罠**: 単純な grep では L.cs の GetTroubleshootingAdvice が
# Get($"{prefix}_Title") の形で組み立てる Trouble_*_{Title,Reason,Steps} 21 キーを
# 死骸と誤判定する。suffix を剥がした裸プレフィックスがコードにあれば使用中とみなす。
head "resx keys are all reachable"
if python3 - <<'PY'
import glob, re, sys, xml.etree.ElementTree as ET
keys = {e.get('name') for e in ET.parse('src/MWC.App/Resources/Strings.resx').findall('.//data')}
blob = ''
for p in glob.glob('src/**/*.cs', recursive=True) + glob.glob('src/**/*.xaml', recursive=True):
    if '/obj/' in p or '/bin/' in p: continue
    blob += open(p, encoding='utf-8', errors='replace').read()

def used(k):
    if f'"{k}"' in blob:
        return True
    # 動的プレフィックス: Get($"{prefix}_Title") 形式 (L.cs の Troubleshooting)
    for sfx in ('_Title', '_Reason', '_Steps'):
        if k.endswith(sfx) and f'"{k[:-len(sfx)]}"' in blob:
            return True
    return False

dead = sorted(k for k in keys if not used(k))
lit = len(re.findall(r'L\.(?:Get|Format)\(\s*"[\w\.]+"', blob))
if lit < 100:
    print(f'only {lit} L.Get/Format literals found — the check itself looks broken')
    sys.exit(1)
if dead:
    print('defined but never referenced (wire them or delete them from ALL 15 resx files):')
    for k in dead: print(f'  {k}')
    sys.exit(1)
print(f'{len(keys)} keys, every one reachable (incl. dynamic Trouble_* prefixes)')
PY
then pass "no dead translation keys"
else fail "resx contains dead keys (see above)"; fi

# ── 結果 ─────────────────────────────────────────────────────────────────────
echo
if [ "$FAILED" -eq 0 ]; then
  printf '\033[32mAll static checks passed.\033[0m  (This is a floor, not a substitute for dotnet build/test.)\n'
else
  printf '\033[31mStatic checks failed.\033[0m\n'
fi
exit $FAILED
