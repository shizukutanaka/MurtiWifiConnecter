// ─────────────────────────────────────────────────────────────────────────────
//  xunit / FluentAssertions / NSubstitute の **型検査専用スタブ**。製品には含めない。
//
//  なぜ在るか:
//    テストプロジェクトは 79 ファイル・約 900 のテストメソッドを持ち、Core の API を
//    最も広く使っている。しかし xunit も FluentAssertions も NuGet 経由でしか入らず
//    (`api.nuget.org` はエグレス拒否)、この環境では**一度もコンパイルされていない**。
//    テストが Core の API 名を間違えていれば CI は即赤くなるが、それが分からなかった。
//
//  ★ 何を検査できて、何を検査していないか(ここを誤解すると危険)
//    検査できる : テストが呼ぶ **Core の API 名・引数・型・enum メンバ・レコード構築**。
//                 アサーションの「外側」— つまりテスト本体のほぼ全て。
//                 例えば `MacAddressModeInference.FromAddress(mac)` の綴りや引数、
//                 `MacModeEvidence.LocallyAdministeredBitSet` の実在はここで落ちる。
//    検査しない : **アサーションの意味**。`Be(object?)` は何でも受けるので、
//                 `x.Should().Be("文字列")` のような型の食い違いは通ってしまう。
//                 FluentAssertions の本物はここを型で縛るが、スタブは縛らない。
//                 **「通った = テストが正しい」ではない。「型が合っている」だけ。**
//    実行しない : テストは 1 つも走らない。結果の正しさは CI (dotnet test) の担当。
//
//  使い方: tools/typecheck-tests.sh から参照される。製品ビルドに混ぜないこと。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class FactAttribute : Attribute
    {
        public string? DisplayName { get; set; }
        public string? Skip { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TheoryAttribute : Attribute
    {
        public string? DisplayName { get; set; }
        public string? Skip { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataAttribute : Attribute
    {
        public InlineDataAttribute(params object?[] data) { }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MemberDataAttribute : Attribute
    {
        public MemberDataAttribute(string memberName, params object?[] parameters) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class TraitAttribute : Attribute
    {
        public TraitAttribute(string name, string value) { }
    }
}

namespace NSubstitute
{
    public static class Substitute
    {
        public static T For<T>(params object?[] constructorArguments) where T : class => default!;
    }

    public static class SubstituteExtensions
    {
        public static ConfiguredCall Returns<T>(this T value, T returnThis, params T[] returnThese)
            => new();
        public static ConfiguredCall Returns<T>(this T value, Func<object, T> returnThis) => new();
        public static T Received<T>(this T substitute) where T : class => substitute;
        public static T Received<T>(this T substitute, int requiredNumberOfCalls) where T : class => substitute;
        public static T DidNotReceive<T>(this T substitute) where T : class => substitute;
    }

    public class ConfiguredCall
    {
        public ConfiguredCall AndDoes(Action<object> callback) => this;
    }

    public static class Arg
    {
        public static T Any<T>() => default!;
        public static T Is<T>(T value) => default!;
        public static T Is<T>(Func<T, bool> predicate) => default!;
    }
}

namespace FluentAssertions
{
    /// <summary>
    /// スカラー用の緩いアサーション連鎖。**型を縛らない** — 目的は
    /// アサーションの外側(Core API 呼び出し)を束縛させることだけ。
    /// </summary>
    public class Chain
    {
        public Chain And => this;
        public Chain Which => this;
        public object? Subject => null;

        public Chain Be(object? expected, string? because = null, params object?[] args) => this;
        public Chain NotBe(object? unexpected, string? because = null, params object?[] args) => this;
        public Chain BeTrue(string? because = null, params object?[] args) => this;
        public Chain BeFalse(string? because = null, params object?[] args) => this;
        public Chain BeNull(string? because = null, params object?[] args) => this;
        public Chain NotBeNull(string? because = null, params object?[] args) => this;
        public Chain BeEmpty(string? because = null, params object?[] args) => this;
        public Chain NotBeEmpty(string? because = null, params object?[] args) => this;
        public Chain BeGreaterThan(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeLessThan(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeGreaterThanOrEqualTo(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeLessThanOrEqualTo(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeInRange(object? min, object? max, string? because = null, params object?[] args) => this;
        public Chain BePositive(string? because = null, params object?[] args) => this;
        public Chain StartWith(object? expected, string? because = null, params object?[] args) => this;
        public Chain EndWith(object? expected, string? because = null, params object?[] args) => this;
        public Chain Match(object? pattern, string? because = null, params object?[] args) => this;
        public Chain Contain(object? expected, string? because = null, params object?[] args) => this;
        public Chain NotContain(object? unexpected, string? because = null, params object?[] args) => this;
        public Chain HaveCount(int expected, string? because = null, params object?[] args) => this;
        public Chain BeEquivalentTo(object? expectation, string? because = null, params object?[] args) => this;
        public Chain Equal(params object?[] expected) => this;
        public Chain BeSameAs(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeOfType<T>(string? because = null, params object?[] args) => this;
        public Chain BeAssignableTo<T>(string? because = null, params object?[] args) => this;
        public Chain ContainSingle(string? because = null, params object?[] args) => this;
        public Chain Throw<TException>(string? because = null, params object?[] args) where TException : Exception => this;
        public Chain NotThrow(string? because = null, params object?[] args) => this;
        public Chain ThrowAsync<TException>(string? because = null, params object?[] args) where TException : Exception => this;
        public Chain NotThrowAsync(string? because = null, params object?[] args) => this;
        public Chain WithMessage(string? expected, string? because = null, params object?[] args) => this;
        public Chain BeNullOrEmpty(string? because = null, params object?[] args) => this;
        public Chain NotBeNullOrEmpty(string? because = null, params object?[] args) => this;
        public Chain NotBeNullOrWhiteSpace(string? because = null, params object?[] args) => this;
        public Chain NotBeSameAs(object? unexpected, string? because = null, params object?[] args) => this;
        public Chain BeOneOf(params object?[] validValues) => this;
        public Chain BeApproximately(object? expected, object? precision, string? because = null, params object?[] args) => this;
        public Chain BeGreaterOrEqualTo(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeLessOrEqualTo(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeOnOrAfter(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeAfter(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeBefore(object? expected, string? because = null, params object?[] args) => this;
        public Chain BeOnOrBefore(object? expected, string? because = null, params object?[] args) => this;
        public Chain HaveLength(int expected, string? because = null, params object?[] args) => this;
        public Chain MatchRegex(string pattern, string? because = null, params object?[] args) => this;
        public Chain HaveCountGreaterThan(int expected, string? because = null, params object?[] args) => this;
        public Chain HaveCountGreaterOrEqualTo(int expected, string? because = null, params object?[] args) => this;
        public Chain HaveCountGreaterThanOrEqualTo(int expected, string? because = null, params object?[] args) => this;
        public Chain BeSubsetOf(object? expectedSuperset, string? because = null, params object?[] args) => this;
        public Chain ContainInOrder(params object?[] expected) => this;
        public Chain AllBe(object? expected, string? because = null, params object?[] args) => this;

        // `await x.Should().ThrowAsync<T>()` を束縛させるためだけの await 対応。
        public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter()
            => Task.CompletedTask.GetAwaiter();
    }

    /// <summary>
    /// コレクション用。述語ラムダ (`Contain(a => ...)`) の T を推論させるために
    /// 要素型を保持する。これが無いとラムダに対象型が付かず束縛できない。
    /// </summary>
    public class CollectionChain<T> : Chain
    {
        public new CollectionChain<T> And => this;
        public new CollectionChain<T> Which => this;

        public CollectionChain<T> Contain(Func<T, bool> predicate, string? because = null, params object?[] args) => this;
        public CollectionChain<T> NotContain(Func<T, bool> predicate, string? because = null, params object?[] args) => this;
        public CollectionChain<T> OnlyContain(Func<T, bool> predicate, string? because = null, params object?[] args) => this;
        public CollectionChain<T> ContainSingle(Func<T, bool> predicate, string? because = null, params object?[] args) => this;
        public new CollectionChain<T> Contain(object? expected, string? because = null, params object?[] args) => this;
        public new CollectionChain<T> NotContain(object? unexpected, string? because = null, params object?[] args) => this;
        public new CollectionChain<T> ContainSingle(string? because = null, params object?[] args) => this;
        public CollectionChain<T> Equal(IEnumerable<T> expected, string? because = null, params object?[] args) => this;
        public new CollectionChain<T> Equal(params object?[] expected) => this;
        public CollectionChain<T> BeInAscendingOrder(string? because = null, params object?[] args) => this;
        public CollectionChain<T> BeInDescendingOrder(string? because = null, params object?[] args) => this;
        public CollectionChain<T> AllSatisfy(Action<T> inspector, string? because = null, params object?[] args) => this;
        public new CollectionChain<T> AllBe(object? expected, string? because = null, params object?[] args) => this;
        public CollectionChain<T> BeSubsetOf(IEnumerable<T> expectedSuperset, string? because = null, params object?[] args) => this;
        public CollectionChain<T> ContainInOrder(params T[] expected) => this;
    }

    public static class AssertionExtensions
    {
        // コレクションを先に選ばせるため、スカラー側は object? を取る。
        // (両方を generic にすると List<T> でスカラー側が勝ってしまい、
        //  述語ラムダが束縛できなくなる。)
        // 引数はすべて null 許容にする。本物の FluentAssertions も null 許容を受けるため、
        // ここを非 null にすると CS8604 が大量に出て**偽のエラー**になる
        // (実測: 「string? を IEnumerable<char> に渡している」旨の警告が多数)。
        public static Chain Should(this object? subject) => new();
        public static CollectionChain<T> Should<T>(this IEnumerable<T>? subject) => new();
        public static Chain Should(this Action? action) => new();
        public static Chain Should(this Func<Task>? action) => new();
    }
}
