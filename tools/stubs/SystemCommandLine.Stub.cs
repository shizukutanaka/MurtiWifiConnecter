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
//    信用しては×  : System.CommandLine の面そのもの — SetHandler のアリティ、
//                   オーバーロード解決、Option/Argument の細かな型。
//                   **これらはスタブが「私の理解」を写しただけ**で、本物と食い違い得る。
//                   ここのエラー/無エラーを根拠に何かを主張しないこと。
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
        public T? GetValueForOption<T>(Option<T> option) => default;
        public T? GetValueForArgument<T>(Argument<T> argument) => default;
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
    /// SetHandler のスタブ。**アリティやオーバーロード解決の正しさは保証しない** —
    /// 目的はラムダ本体を束縛させ、その中身を本物の Core/BCL に対して検査させること。
    /// </summary>
    public static class Handler
    {
        public static void SetHandler(this Command command, Action handle) { }
        public static void SetHandler(this Command command, Func<Task> handle) { }

        public static void SetHandler(this Command command, Action<InvocationContext> handle) { }
        public static void SetHandler(this Command command, Func<InvocationContext, Task> handle) { }

        public static void SetHandler<T1>(this Command c, Func<T1, Task> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2>(this Command c, Func<T1, T2, Task> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2, T3>(this Command c, Func<T1, T2, T3, Task> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2, T3, T4>(this Command c, Func<T1, T2, T3, T4, Task> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2, T3, T4, T5>(this Command c, Func<T1, T2, T3, T4, T5, Task> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6>(this Command c, Func<T1, T2, T3, T4, T5, T6, Task> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6, T7>(this Command c, Func<T1, T2, T3, T4, T5, T6, T7, Task> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2, T3, T4, T5, T6, T7, T8>(this Command c, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> h, params IValueDescriptor[] s) { }

        public static void SetHandler<T1>(this Command c, Action<T1> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2>(this Command c, Action<T1, T2> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2, T3>(this Command c, Action<T1, T2, T3> h, params IValueDescriptor[] s) { }
        public static void SetHandler<T1, T2, T3, T4>(this Command c, Action<T1, T2, T3, T4> h, params IValueDescriptor[] s) { }
    }
}
