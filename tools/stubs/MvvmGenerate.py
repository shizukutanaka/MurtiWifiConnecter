#!/usr/bin/env python3
"""CommunityToolkit.Mvvm のソースジェネレータを **型検査用に**再現する。

なぜ循環しないのか (tools/stubs/WpfMinimal.Stub.cs のダイアログ群との違い):
  ここで再現するのは **公表された命名規約** であって、検査対象のコードから
  逆算した署名ではない。CommunityToolkit.Mvvm は

    [ObservableProperty] private T _fooBar;   →  public T FooBar { get; set; }
                                                 partial void OnFooBarChanging(T value);
                                                 partial void OnFooBarChanged(T value);
    [RelayCommand]       private void Save()  →  public IRelayCommand SaveCommand
    [RelayCommand]       private Task LoadAsync() → public IAsyncRelayCommand LoadCommand

  と生成することがドキュメント化されている。規約どおりに生成するので、
  ViewModel 側が規約に反した名前を参照していれば**ここで落ちる** (= 検査になる)。

信用してよい範囲:
  ViewModel 本体のロジック — Core API 呼び出し、制御フロー、BCL 利用。
信用しては×:
  生成メンバそのものの正しさ (本物のジェネレータと細部が違い得る)。
  変更通知の実挙動 (PropertyChanged の発火順序など) は一切検証していない。

使い方: python3 tools/stubs/MvvmGenerate.py <出力ファイル> <入力.cs...>
"""
import re
import sys

FIELD = re.compile(
    r'\[ObservableProperty\][^\n]*\n?\s*private\s+([\w<>,\.\?\[\]]+)\s+(_?\w+)\s*(?:=[^;]*)?;',
    re.M)
# 同一行に属性と宣言が並ぶ書き方 ([ObservableProperty] private bool _x = true;) も拾う
FIELD_INLINE = re.compile(
    r'\[ObservableProperty\]\s+private\s+([\w<>,\.\?\[\]]+)\s+(_?\w+)\s*(?:=[^;]*)?;')
COMMAND = re.compile(
    r'\[RelayCommand[^\]]*\]\s*(?:private|public|internal)?\s*(?:async\s+)?'
    r'([\w<>,\.\?\[\]]+)\s+(\w+)\s*\(', re.M)
NS = re.compile(r'^namespace\s+([\w\.]+)\s*;', re.M)
USING = re.compile(r'^using\s+[^;]+;', re.M)
CLS = re.compile(r'(?:public|internal)\s+(?:sealed\s+)?partial\s+class\s+(\w+)', re.M)


def prop_name(field: str) -> str:
    """_fooBar / m_fooBar / fooBar → FooBar (CommunityToolkit の規約)."""
    n = field.lstrip('_')
    if n.startswith('m_'):
        n = n[2:]
    return n[:1].upper() + n[1:]


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    out_path, sources = sys.argv[1], sys.argv[2:]
    chunks = ['// 自動生成 (tools/stubs/MvvmGenerate.py)。編集しないこと。',
              '#nullable enable']

    for path in sources:
        try:
            text = open(path, encoding='utf-8').read()
        except OSError:
            continue
        if '[ObservableProperty]' not in text and '[RelayCommand' not in text:
            continue
        ns = NS.search(text)
        cls = CLS.search(text)
        if not cls:
            continue

        members = []
        seen = set()
        for ty, fld in list(FIELD.findall(text)) + list(FIELD_INLINE.findall(text)):
            if fld in seen:
                continue
            seen.add(fld)
            p = prop_name(fld)
            members.append(f'''
    public {ty} {p}
    {{
        get => {fld};
        set
        {{
            On{p}Changing(value);
            {fld} = value;
            On{p}Changed(value);
            OnPropertyChanged(nameof({p}));
        }}
    }}
    partial void On{p}Changing({ty} value);
    partial void On{p}Changed({ty} value);''')

        for ret, meth in COMMAND.findall(text):
            name = meth[:-5] if meth.endswith('Async') else meth
            kind = ('CommunityToolkit.Mvvm.Input.IAsyncRelayCommand'
                    if 'Task' in ret else 'CommunityToolkit.Mvvm.Input.IRelayCommand')
            members.append(f'    public {kind} {name}Command {{ get; }} = default!;')

        if not members:
            continue
        # ブロック構文の namespace を使う。ファイルスコープ (`namespace X;`) は
        # 1 ファイルに 1 つしか置けず、複数クラス分をまとめると CS8954 になる。
        name_space = ns.group(1) if ns else 'MWC.App.ViewModels'
        body = '\n'.join(members)
        # 元ファイルの using をそのまま持ち込む。生成メンバの型 (AuthMethod 等) は
        # 元ファイルの using で解決されているため、これが無いと CS0246 になる。
        usings = '\n'.join(USING.findall(text))
        chunks.append(f'\nnamespace {name_space}\n{{\n{usings}\n'
                      f'partial class {cls.group(1)}\n{{'
                      + body + '\n}\n}')

    open(out_path, 'w', encoding='utf-8').write('\n'.join(chunks) + '\n')
    print(f'generated {len(chunks) - 2} partial class(es) -> {out_path}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
