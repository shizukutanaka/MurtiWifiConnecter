// ─────────────────────────────────────────────────────────────────────────────
//  System.CommandLine 2.0.0-beta4 の **型検査専用スタブ**。製品には一切含めない。
//
//  なぜ在るか:
//    `api.nuget.org` がエグレスポリシーで拒否されているため、AI セッションでは
//    System.CommandLine を入手できず、`MWC.Cli` を一切コンパイルできなかった。
//    本物の Core を参照して Cli をコンパイルする近道は**効かない**ことが実験で判明済み:
//    SetHandler のデリゲート型が解決できないと Roslyn はラムダ本体を束縛せず、
//    Core の API 名を間違えていてもエラーが出ない(= 何も検査していない)。
//
//    このスタブはその 1 点だけを解く。デリゲート型が解決できれば**ラムダ本体が束縛され**、
//    その中の `MWC.Core` 呼び出しと BCL 利用が**本物に対して**型検査される。
//
//  ★ 何を信用してよいか(重要)
//    信用してよい : ハンドラ本体の中身 — Core の API 名・引数・null 許容・BCL 利用。
//                   これらは本物の MWC.Core.dll と本物の参照アセンブリで検査される。
//                   2026-09 に SetHandler / ParseResult.GetValueForOption・GetValueForArgument /
//                   Option(T) のコンストラクタ群を実ソース
//                   (`github.com/dotnet/command-line-api`、タグ `2.0.0-beta4.22272.1` —
//                   `Directory.Packages.props` のピン留めと一致)と突き合わせ、
//                   引数ごとに型付けされた記述子(`IValueDescriptor<T1> s1, ...`)に
//                   書き直した。以前は `params IValueDescriptor[]` で受けており、
//                   記述子の型も順序も一切検査していなかった。
//    信用しては×  : SetHandler の呼び出し側が記述子を**正しい順序**で渡しているかは
//                   型が一致する限り検査できる(型が同じ隣接オプションの取り違えは
//                   これでも検出できない — 実行時テストが要る)。それ以外の細かな
//                   Option/Argument API(ParseArgument<T> オーバーロード等、
//                   このプロジェクトが使っていないもの)は未収録。
//
//  使い方: tools/typecheck-cli.sh から参照される。手で製品ビルドに混ぜないこと。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace System.CommandLine
{
    public interface IValueDescriptor { string Name { get; } }
    public interface IValueDescriptor<T> : IValueDescriptor { }

    public class Symbol
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
    }

    public class Option : Symbol { }

    public class Option<T> : Option, IValueDescriptor<T>
    {
        public Option(string name, string? description = null) { Name = name; Description = description; }
        public Option(string[] aliases, string? description = null) { Name = aliases[0]; Description = description; }
        public Option(string name, Func<T> getDefaultValue, string? description = null)
        { Name = name; Description = description; }
        public bool IsRequired { get; set; }
        public bool AllowMultipleArgumentsPerToken { get; set; }
        public ArgumentArity Arity { get; set; } = ArgumentArity.ZeroOrOne;
        public void AddAlias(string alias) { }
        public void AddCompletions(params string[] values) { }
        public void SetDefaultValue(object? value) { }
    }

    public class ArgumentArity
    {
        public static ArgumentArity Zero => new();
        public static ArgumentArity ZeroOrOne => new();
        public static ArgumentArity ExactlyOne => new();
        public static ArgumentArity ZeroOrMore => new();
        public static ArgumentArity OneOrMore => new();
    }

    public class Argument : Symbol
    {
        public ArgumentArity Arity { get; set; } = ArgumentArity.ZeroOrOne;
    }

    public class Argument<T> : Argument, IValueDescriptor<T>
    {
        public Argument(string name, string? description = null) { Name = name; Description = description; }
        public Argument() { }
        public void AddCompletions(params string[] values) { }
        public void SetDefaultValue(object? value) { }
    }

    public class ParseResult
    {
        // 2026-09 修正: 実ソース(Parsing/ParseResult.cs)では GetValueForOption<T> は
        // `T?`(Nullable)だが GetValueForArgument<T> は `T`(non-nullable)——
        // 非対称。以前のスタブは両方 `T?` にしていた。
        public T? GetValueForOption<T>(Option<T> option) => default;
        public T GetValueForArgument<T>(Argument<T> argument) => default!;
    }

    public class Command : Symbol
    {
        public Command(string name, string? description = null) { Name = name; Description = description; }
        public void AddOption(Option option) { }
        public void AddArgument(Argument argument) { }
        public void AddCommand(Command command) { }
        public void AddAlias(string alias) { }
    }

    public class RootCommand : Command
    {
        public RootCommand(string? description = null) : base("root", description) { }
        public Task<int> InvokeAsync(string[] args) => Task.FromResult(0);
    }
}

namespace System.CommandLine.Invocation
{
    public class InvocationContext
    {
        public System.CommandLine.ParseResult ParseResult { get; } = new();
        public int ExitCode { get; set; }
    }
}

namespace System.CommandLine
{
    using System.CommandLine.Invocation;

    /// <summary>
    /// SetHandler のスタブ。2026-09 に実ソース(`dotnet/command-line-api`、タグ
    /// `2.0.0-beta4.22272.1` — ピン留めと一致)の `Handler.Action.cs`/`Handler.Func.cs`
    /// と突き合わせて書き直した。
    ///
    /// ★ 以前の版との重要な違い: 以前は可変長の `params IValueDescriptor[] s` を
    /// 受けており、渡す記述子の**型も順序も一切検査しなかった**(=呼び出し側が
    /// オプションを取り違えて渡しても検出できない)。実 API は
    /// `IValueDescriptor&lt;T1&gt; symbol1, IValueDescriptor&lt;T2&gt; symbol2, ...` という
    /// **引数ごとに型付けされた個別パラメータ**であり、ラムダの型と記述子の型・順序が
    /// 食い違えば CS1503 で落ちる。この違いは検査力に直結するため、実 API の形に
    /// 合わせて書き直した。Action は実 API も T1〜T8 まであるが、以前のスタブは
    /// T1〜T4 までしか無かった(Func は T1〜T8 まであったのに非対称だった)。
    /// </summary>
    public static class Handler
    {
        public static void SetHandler(this Command command, Action handle) { }
        public static void SetHandler(this Command command, Func<Task> handle) { }

        public static void SetHandler(this Command command, Action<InvocationContext> handle) { }
        public static void SetHandler(this Command command, Func<InvocationContext, Task> handle) { }

        public static void SetHandler<T1>(this Command c, Func<T1, Task> h, IValueDescriptor<T1> s1) { }
        public static void SetHandler<T1, T2>(this Command c, Func<T1, T2, Task> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2) { }
        public static void SetHandler<T1, T2, T3>(this Command c, Func<T1, T2, T3, Task> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3) { }
        public static void SetHandler<T1, T2, T3, T4>(this Command c, Func<T1, T2, T3, T4, Task> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4) { }
        public static void SetHandler<T1, T2, T3, T4, T5>(this Command c, Func<T1, T2, T3, T4, T5, Task> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4, IValueDescriptor<T5> s5) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6>(this Command c, Func<T1, T2, T3, T4, T5, T6, Task> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4, IValueDescriptor<T5> s5, IValueDescriptor<T6> s6) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6, T7>(this Command c, Func<T1, T2, T3, T4, T5, T6, T7, Task> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4, IValueDescriptor<T5> s5, IValueDescriptor<T6> s6, IValueDescriptor<T7> s7) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6, T7, T8>(this Command c, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4, IValueDescriptor<T5> s5, IValueDescriptor<T6> s6, IValueDescriptor<T7> s7, IValueDescriptor<T8> s8) { }

        public static void SetHandler<T1>(this Command c, Action<T1> h, IValueDescriptor<T1> s1) { }
        public static void SetHandler<T1, T2>(this Command c, Action<T1, T2> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2) { }
        public static void SetHandler<T1, T2, T3>(this Command c, Action<T1, T2, T3> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3) { }
        public static void SetHandler<T1, T2, T3, T4>(this Command c, Action<T1, T2, T3, T4> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4) { }
        public static void SetHandler<T1, T2, T3, T4, T5>(this Command c, Action<T1, T2, T3, T4, T5> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4, IValueDescriptor<T5> s5) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6>(this Command c, Action<T1, T2, T3, T4, T5, T6> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4, IValueDescriptor<T5> s5, IValueDescriptor<T6> s6) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6, T7>(this Command c, Action<T1, T2, T3, T4, T5, T6, T7> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4, IValueDescriptor<T5> s5, IValueDescriptor<T6> s6, IValueDescriptor<T7> s7) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6, T7, T8>(this Command c, Action<T1, T2, T3, T4, T5, T6, T7, T8> h, IValueDescriptor<T1> s1, IValueDescriptor<T2> s2, IValueDescriptor<T3> s3, IValueDescriptor<T4> s4, IValueDescriptor<T5> s5, IValueDescriptor<T6> s6, IValueDescriptor<T7> s7, IValueDescriptor<T8> s8) { }
    }
}
