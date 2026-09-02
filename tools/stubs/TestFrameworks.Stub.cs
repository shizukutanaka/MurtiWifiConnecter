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
//  ★ 2026-08 追記: アサーションは **実際に検証する** ようになった (以前は no-op)。
//    これにより tools/run-tests.sh がテストを**本当に実行**できる。型検査時の
//    シグネチャは変えていないので typecheck-tests.sh の動作は不変。
//    ただし意味論は FluentAssertions の**近似**であり、特に BeEquivalentTo は
//    本物の構造比較ではなく順序付き列挙比較にすぎない。差異が出たら本物が正。
//
//  使い方: tools/typecheck-tests.sh と tools/run-tests.sh から参照される。
//          製品ビルドに混ぜないこと。
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
        public object?[] Data { get; }
        public InlineDataAttribute(params object?[] data) { Data = data; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MemberDataAttribute : Attribute
    {
        public string MemberName { get; }
        public object?[] Parameters { get; }
        public MemberDataAttribute(string memberName, params object?[] parameters)
        { MemberName = memberName; Parameters = parameters; }
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
        /// <summary>
        /// 本物の NSubstitute は動的プロキシを生成する。このハーネスには再現できないため、
        /// **null を返して後段で NullReferenceException にする代わりに**、専用の例外で
        /// 「この harness では実行できない」と明示する。MiniRunner はこれを skip として扱う。
        /// 黙って null を返すと、原因不明の NRE として failure に混ざってしまう。
        /// </summary>
        public static T For<T>(params object?[] constructorArguments) where T : class
            => throw new SubstituteUnavailableException(typeof(T).Name);
    }

    public static class SubstituteExtensions
    {
        public static ConfiguredCall Returns<T>(this T value, T returnThis, params T[] returnThese)
            => new();
        public static ConfiguredCall Returns<T>(this T value, Func<object, T> returnThis) => new();

        // NSubstitute は Task/ValueTask を返すメンバ向けに専用オーバーロードを持つ
        // (`svc.GetAsync().Returns(list)` と書けるのはこれのため)。公表された API。
        public static ConfiguredCall Returns<T>(this System.Threading.Tasks.Task<T> value,
                                                T returnThis, params T[] returnThese) => new();
        public static ConfiguredCall Returns<T>(this System.Threading.Tasks.Task<T> value,
                                                Func<object, T> returnThis) => new();
        public static ConfiguredCall Returns<T>(this System.Threading.Tasks.ValueTask<T> value,
                                                T returnThis, params T[] returnThese) => new();
        public static T Received<T>(this T substitute) where T : class => substitute;
        public static T Received<T>(this T substitute, int requiredNumberOfCalls) where T : class => substitute;
        public static T DidNotReceive<T>(this T substitute) where T : class => substitute;
    }

    /// <summary>NSubstitute が使えないことを示す。MiniRunner が skip 判定に使う。</summary>
    public sealed class SubstituteUnavailableException : Exception
    {
        public SubstituteUnavailableException(string typeName)
            : base($"NSubstitute is unavailable in this harness (Substitute.For<{typeName}>)") { }
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
    using System.Collections;
    using System.Linq;

    /// <summary>アサーション失敗。xunit の例外に相当する。</summary>
    public sealed class AssertionFailedException : Exception
    {
        public AssertionFailedException(string message) : base(message) { }
    }

    internal static class Cmp
    {
        internal static bool IsNum(object o) =>
            o is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

        internal static bool Eq(object? a, object? b)
        {
            if (a is null || b is null) return a is null && b is null;
            if (a.Equals(b)) return true;
            if (IsNum(a) && IsNum(b)) return Convert.ToDecimal(a) == Convert.ToDecimal(b);
            if (a is string || b is string) return false;
            if (a is IEnumerable ea && b is IEnumerable eb) return SeqEq(ea, eb);
            return false;
        }

        internal static bool SeqEq(IEnumerable a, IEnumerable b)
        {
            var ia = a.GetEnumerator(); var ib = b.GetEnumerator();
            while (true)
            {
                bool ha = ia.MoveNext(), hb = ib.MoveNext();
                if (ha != hb) return false;
                if (!ha) return true;
                if (!Eq(ia.Current, ib.Current)) return false;
            }
        }

        internal static int Compare(object? a, object? b)
        {
            if (a is null || b is null) throw new AssertionFailedException("cannot compare null");
            if (IsNum(a) && IsNum(b)) return Convert.ToDecimal(a).CompareTo(Convert.ToDecimal(b));
            if (a is IComparable ca) return ca.CompareTo(b);
            throw new AssertionFailedException($"{a.GetType().Name} is not comparable");
        }

        /// <summary>公開プロパティを再帰的に比較する。深さ上限で循環を防ぐ。</summary>
        internal static bool Structural(object? a, object? b, int depth)
        {
            if (depth > 6) return true;
            if (a is null || b is null) return a is null && b is null;
            if (Eq(a, b)) return true;
            if (a is string || b is string) return false;

            if (a is IEnumerable ea && b is IEnumerable eb)
            {
                var la = ea.Cast<object?>().ToList(); var lb = eb.Cast<object?>().ToList();
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++) if (!Structural(la[i], lb[i], depth + 1)) return false;
                return true;
            }

            var ta = a.GetType(); if (ta != b.GetType()) return false;
            var props = ta.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                          .Where(p => p.GetIndexParameters().Length == 0).ToList();
            if (props.Count == 0) return false;
            foreach (var p in props)
            {
                object? va, vb;
                try { va = p.GetValue(a); vb = p.GetValue(b); } catch { continue; }
                if (!Structural(va, vb, depth + 1)) return false;
            }
            return true;
        }

        internal static string Show(object? o) => o switch
        {
            null => "<null>",
            string s => $"\"{s}\"",
            IEnumerable e when o is not string => "[" + string.Join(", ", e.Cast<object?>().Select(Show)) + "]",
            _ => o.ToString() ?? "<null>",
        };

        internal static void Fail(string what, string? because)
            => throw new AssertionFailedException(because is null ? what : $"{what} because {because}");
    }

    /// <summary>スカラー用のアサーション連鎖。型は緩いが**検証は実際に行う**。</summary>
    public class Chain
    {
        protected readonly object? S;
        public Chain(object? subject) { S = subject; }

        public Chain And => this;
        public Chain Which => this;
        public object? Subject => S;

        public Chain Be(object? e, string? because = null, params object?[] args)
        { if (!Cmp.Eq(S, e)) Cmp.Fail($"expected {Cmp.Show(e)} but found {Cmp.Show(S)}", because); return this; }
        public Chain NotBe(object? e, string? because = null, params object?[] args)
        { if (Cmp.Eq(S, e)) Cmp.Fail($"did not expect {Cmp.Show(e)}", because); return this; }
        public Chain BeTrue(string? because = null, params object?[] args)
        { if (!(S is bool t && t)) Cmp.Fail($"expected true but found {Cmp.Show(S)}", because); return this; }
        public Chain BeFalse(string? because = null, params object?[] args)
        { if (!(S is bool f && !f)) Cmp.Fail($"expected false but found {Cmp.Show(S)}", because); return this; }
        public Chain BeNull(string? because = null, params object?[] args)
        { if (S is not null) Cmp.Fail($"expected null but found {Cmp.Show(S)}", because); return this; }
        public Chain NotBeNull(string? because = null, params object?[] args)
        { if (S is null) Cmp.Fail("expected non-null", because); return this; }
        public Chain BeSameAs(object? e, string? because = null, params object?[] args)
        { if (!ReferenceEquals(S, e)) Cmp.Fail("expected the same reference", because); return this; }
        public Chain NotBeSameAs(object? e, string? because = null, params object?[] args)
        { if (ReferenceEquals(S, e)) Cmp.Fail("expected a different reference", because); return this; }

        private static int Count(object? o) => o switch
        {
            null => 0,
            string s => s.Length,
            IEnumerable e => e.Cast<object?>().Count(),
            _ => throw new AssertionFailedException($"{o.GetType().Name} has no count"),
        };

        public Chain BeEmpty(string? because = null, params object?[] args)
        { if (Count(S) != 0) Cmp.Fail($"expected empty but found {Count(S)} item(s)", because); return this; }
        public Chain NotBeEmpty(string? because = null, params object?[] args)
        { if (Count(S) == 0) Cmp.Fail("expected non-empty", because); return this; }
        public Chain HaveCount(int e, string? because = null, params object?[] args)
        { if (Count(S) != e) Cmp.Fail($"expected {e} item(s) but found {Count(S)}", because); return this; }
        public Chain HaveCountGreaterThan(int e, string? because = null, params object?[] args)
        { if (Count(S) <= e) Cmp.Fail($"expected more than {e} but found {Count(S)}", because); return this; }
        public Chain HaveCountGreaterOrEqualTo(int e, string? because = null, params object?[] args)
        { if (Count(S) < e) Cmp.Fail($"expected at least {e} but found {Count(S)}", because); return this; }
        public Chain HaveCountGreaterThanOrEqualTo(int e, string? because = null, params object?[] args)
            => HaveCountGreaterOrEqualTo(e, because, args);
        public Chain HaveLength(int e, string? because = null, params object?[] args)
        { if (Count(S) != e) Cmp.Fail($"expected length {e} but found {Count(S)}", because); return this; }
        public Chain BeNullOrEmpty(string? because = null, params object?[] args)
        { if (S is not null && Count(S) != 0) Cmp.Fail("expected null or empty", because); return this; }
        public Chain NotBeNullOrEmpty(string? because = null, params object?[] args)
        { if (S is null || Count(S) == 0) Cmp.Fail("expected non-null and non-empty", because); return this; }
        /// <summary>ArgumentException の ParamName を検査する (FluentAssertions の API)。</summary>
        public Chain WithParameterName(string expected, string? because = null, params object?[] args)
        {
            if (S is ArgumentException ae && ae.ParamName != expected)
                Cmp.Fail($"expected parameter name {expected} but found {ae.ParamName}", because);
            return this;
        }

        public Chain NotContainAny(params object?[] unexpected)
        {
            var hay = S?.ToString() ?? "";
            foreach (var u in unexpected)
            {
                var n = u?.ToString();
                if (!string.IsNullOrEmpty(n) && hay.Contains(n, StringComparison.Ordinal))
                    Cmp.Fail($"did not expect {Cmp.Show(u)} to appear in {Cmp.Show(hay)}", null);
            }
            return this;
        }

        public Chain NotBeNullOrWhiteSpace(string? because = null, params object?[] args)
        { if (S is not string s || string.IsNullOrWhiteSpace(s)) Cmp.Fail("expected non-blank text", because); return this; }

        public Chain BeGreaterThan(object? e, string? because = null, params object?[] args)
        { if (Cmp.Compare(S, e) <= 0) Cmp.Fail($"expected > {Cmp.Show(e)} but found {Cmp.Show(S)}", because); return this; }
        public Chain BeLessThan(object? e, string? because = null, params object?[] args)
        { if (Cmp.Compare(S, e) >= 0) Cmp.Fail($"expected < {Cmp.Show(e)} but found {Cmp.Show(S)}", because); return this; }
        public Chain BeGreaterThanOrEqualTo(object? e, string? because = null, params object?[] args)
        { if (Cmp.Compare(S, e) < 0) Cmp.Fail($"expected >= {Cmp.Show(e)} but found {Cmp.Show(S)}", because); return this; }
        public Chain BeLessThanOrEqualTo(object? e, string? because = null, params object?[] args)
        { if (Cmp.Compare(S, e) > 0) Cmp.Fail($"expected <= {Cmp.Show(e)} but found {Cmp.Show(S)}", because); return this; }
        public Chain BeGreaterOrEqualTo(object? e, string? because = null, params object?[] args) => BeGreaterThanOrEqualTo(e, because, args);
        public Chain BeLessOrEqualTo(object? e, string? because = null, params object?[] args) => BeLessThanOrEqualTo(e, because, args);
        public Chain BeAfter(object? e, string? because = null, params object?[] args) => BeGreaterThan(e, because, args);
        public Chain BeBefore(object? e, string? because = null, params object?[] args) => BeLessThan(e, because, args);
        public Chain BeOnOrAfter(object? e, string? because = null, params object?[] args) => BeGreaterThanOrEqualTo(e, because, args);
        public Chain BeOnOrBefore(object? e, string? because = null, params object?[] args) => BeLessThanOrEqualTo(e, because, args);
        public Chain BePositive(string? because = null, params object?[] args)
        { if (Cmp.Compare(S, 0) <= 0) Cmp.Fail($"expected positive but found {Cmp.Show(S)}", because); return this; }
        public Chain BeInRange(object? lo, object? hi, string? because = null, params object?[] args)
        { if (Cmp.Compare(S, lo) < 0 || Cmp.Compare(S, hi) > 0)
              Cmp.Fail($"expected between {Cmp.Show(lo)} and {Cmp.Show(hi)} but found {Cmp.Show(S)}", because); return this; }
        public Chain BeApproximately(object? e, object? precision, string? because = null, params object?[] args)
        { var d = Math.Abs(Convert.ToDouble(S) - Convert.ToDouble(e));
          if (d > Convert.ToDouble(precision)) Cmp.Fail($"expected {Cmp.Show(e)} +/- {Cmp.Show(precision)} but found {Cmp.Show(S)}", because);
          return this; }
        public Chain BeOneOf(params object?[] valid)
        { if (!valid.Any(v => Cmp.Eq(S, v))) Cmp.Fail($"expected one of {Cmp.Show(valid)} but found {Cmp.Show(S)}", null); return this; }

        public Chain StartWith(object? e, string? because = null, params object?[] args)
        { if (S is not string s || e is not string p || !s.StartsWith(p, StringComparison.Ordinal))
              Cmp.Fail($"expected to start with {Cmp.Show(e)} but found {Cmp.Show(S)}", because); return this; }
        public Chain EndWith(object? e, string? because = null, params object?[] args)
        { if (S is not string s || e is not string p || !s.EndsWith(p, StringComparison.Ordinal))
              Cmp.Fail($"expected to end with {Cmp.Show(e)} but found {Cmp.Show(S)}", because); return this; }
        public Chain Match(object? pattern, string? because = null, params object?[] args)
        { if (S is not string s || pattern is not string p) { Cmp.Fail("Match needs strings", because); return this; }
          var rx = "^" + System.Text.RegularExpressions.Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
          if (!System.Text.RegularExpressions.Regex.IsMatch(s, rx)) Cmp.Fail($"{Cmp.Show(S)} does not match {Cmp.Show(pattern)}", because);
          return this; }
        public Chain MatchRegex(string pattern, string? because = null, params object?[] args)
        { if (S is not string s || !System.Text.RegularExpressions.Regex.IsMatch(s, pattern))
              Cmp.Fail($"{Cmp.Show(S)} does not match /{pattern}/", because); return this; }

        private static bool Has(object? subject, object? item)
            => subject switch
            {
                string s when item is string t => s.Contains(t, StringComparison.Ordinal),
                IEnumerable e => e.Cast<object?>().Any(x => Cmp.Eq(x, item)),
                _ => false,
            };

        public Chain Contain(object? e, string? because = null, params object?[] args)
        {
            // FluentAssertions では Contain(IEnumerable) は「その全要素を含む」。
            // 単一要素の包含と混同すると、集合を渡したときに必ず失敗する。
            if (S is IEnumerable && S is not string && e is IEnumerable ee && e is not string)
            {
                foreach (var x in ee) if (!Has(S, x)) Cmp.Fail($"expected {Cmp.Show(S)} to contain {Cmp.Show(x)}", because);
                return this;
            }
            if (!Has(S, e)) Cmp.Fail($"expected {Cmp.Show(S)} to contain {Cmp.Show(e)}", because); return this;
        }
        public Chain NotContain(object? e, string? because = null, params object?[] args)
        { if (Has(S, e)) Cmp.Fail($"expected {Cmp.Show(S)} not to contain {Cmp.Show(e)}", because); return this; }
        public Chain ContainSingle(string? because = null, params object?[] args)
        { if (Count(S) != 1) Cmp.Fail($"expected exactly one item but found {Count(S)}", because); return this; }
        public Chain ContainInOrder(params object?[] expected)
        { var items = (S as IEnumerable)?.Cast<object?>().ToList() ?? new List<object?>();
          int i = 0; foreach (var x in items) if (i < expected.Length && Cmp.Eq(x, expected[i])) i++;
          if (i != expected.Length) Cmp.Fail($"expected {Cmp.Show(S)} to contain {Cmp.Show(expected)} in order", null);
          return this; }
        public Chain BeSubsetOf(object? super, string? because = null, params object?[] args)
        { var sup = (super as IEnumerable)?.Cast<object?>().ToList() ?? new List<object?>();
          foreach (var x in (S as IEnumerable)?.Cast<object?>() ?? Enumerable.Empty<object?>())
              if (!sup.Any(y => Cmp.Eq(x, y))) Cmp.Fail($"{Cmp.Show(x)} is not in the superset", because);
          return this; }
        public Chain AllBe(object? e, string? because = null, params object?[] args)
        { foreach (var x in (S as IEnumerable)?.Cast<object?>() ?? Enumerable.Empty<object?>())
              if (!Cmp.Eq(x, e)) Cmp.Fail($"expected all to be {Cmp.Show(e)} but found {Cmp.Show(x)}", because);
          return this; }
        public Chain Equal(params object?[] expected)
        { var exp = expected.Length == 1 && expected[0] is IEnumerable en && expected[0] is not string
                  ? en.Cast<object?>().ToArray() : expected;
          if (!Cmp.SeqEq((S as IEnumerable) ?? Array.Empty<object?>(), exp))
              Cmp.Fail($"expected {Cmp.Show(exp)} but found {Cmp.Show(S)}", null); return this; }
        public Chain BeEquivalentTo(object? e, string? because = null, params object?[] args)
        { // 本物は構造比較。レコードの配列プロパティは Equals が参照比較になるため、
          // 単純な Equals では「同じ内容なのに不一致」になる (実際に踏んだ)。
          if (!Cmp.Structural(S, e, 0)) Cmp.Fail($"expected {Cmp.Show(e)} but found {Cmp.Show(S)}", because);
          return this; }

        public Chain BeOfType<T>(string? because = null, params object?[] args)
        { if (S is not T) Cmp.Fail($"expected {typeof(T).Name} but found {S?.GetType().Name ?? "<null>"}", because); return this; }
        public Chain BeAssignableTo<T>(string? because = null, params object?[] args)
        { if (S is not T) Cmp.Fail($"expected assignable to {typeof(T).Name}", because); return this; }

        public Chain Throw<TException>(string? because = null, params object?[] args) where TException : Exception
        { var ex = Capture(); if (ex is null) Cmp.Fail($"expected {typeof(TException).Name} but nothing was thrown", because);
          else if (ex is not TException) Cmp.Fail($"expected {typeof(TException).Name} but found {ex.GetType().Name}: {ex.Message}", because);
          return this; }
        public Chain NotThrow(string? because = null, params object?[] args)
        { var ex = Capture(); if (ex is not null) Cmp.Fail($"did not expect {ex.GetType().Name}: {ex.Message}", because); return this; }
        public Chain ThrowAsync<TException>(string? because = null, params object?[] args) where TException : Exception
            => Throw<TException>(because, args);
        public Chain NotThrowAsync(string? because = null, params object?[] args) => NotThrow(because, args);
        public Chain WithMessage(string? e, string? because = null, params object?[] args) => this;

        private Exception? Capture()
        {
            try
            {
                switch (S)
                {
                    case Action act: act(); return null;
                    case Func<Task> ft: ft().GetAwaiter().GetResult(); return null;
                    // `var act = () => svc.Returns值();` は Func<T> に推論される。
                    // Action / Func<object?> だけを見ていると**呼び出されず**、
                    // 「何も投げられなかった」と誤報告する (実際に踏んだ)。
                    // 任意のデリゲートを DynamicInvoke で呼ぶ。
                    case Delegate d:
                        try { d.DynamicInvoke(); }
                        catch (System.Reflection.TargetInvocationException tie)
                            when (tie.InnerException is not null) { return tie.InnerException; }
                        return null;
                    default: return null;
                }
            }
            catch (Exception ex) { return ex is AggregateException ag && ag.InnerException is not null ? ag.InnerException : ex; }
        }

        public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => Task.CompletedTask.GetAwaiter();
    }

    /// <summary>コレクション用。述語ラムダを受けるために要素型を保持する。</summary>
    public class CollectionChain<T> : Chain
    {
        private readonly IEnumerable<T> _items;
        public CollectionChain(IEnumerable<T>? items) : base(items)
            => _items = items ?? Enumerable.Empty<T>();

        public new CollectionChain<T> And => this;
        /// <summary>直前の ContainSingle 等で絞り込まれた単一要素。FluentAssertions と同じ意味。</summary>
        public new T Which => _items.Single();

        public CollectionChain<T> Contain(Func<T, bool> p, string? because = null, params object?[] args)
        { if (!_items.Any(p)) Cmp.Fail("no item matched the predicate", because); return this; }
        public CollectionChain<T> NotContain(Func<T, bool> p, string? because = null, params object?[] args)
        { if (_items.Any(p)) Cmp.Fail("an item matched the predicate but none was expected", because); return this; }
        public CollectionChain<T> OnlyContain(Func<T, bool> p, string? because = null, params object?[] args)
        { if (!_items.All(p)) Cmp.Fail("not every item matched the predicate", because); return this; }
        public new CollectionChain<T> ContainSingle(string? because = null, params object?[] args)
        { var n = _items.Count(); if (n != 1) Cmp.Fail($"expected exactly one item but found {n}", because); return this; }
        public CollectionChain<T> ContainSingle(Func<T, bool> p, string? because = null, params object?[] args)
        { var n = _items.Count(p); if (n != 1) Cmp.Fail($"expected exactly one match but found {n}", because); return this; }
        public CollectionChain<T> AllSatisfy(Action<T> inspect, string? because = null, params object?[] args)
        { foreach (var x in _items) inspect(x); return this; }
        public CollectionChain<T> BeInAscendingOrder(string? because = null, params object?[] args)
        { var l = _items.ToList(); for (int i = 1; i < l.Count; i++) if (Cmp.Compare(l[i-1], l[i]) > 0) Cmp.Fail("not ascending", because); return this; }
        public CollectionChain<T> BeInDescendingOrder(string? because = null, params object?[] args)
        { var l = _items.ToList(); for (int i = 1; i < l.Count; i++) if (Cmp.Compare(l[i-1], l[i]) < 0) Cmp.Fail("not descending", because); return this; }
        public CollectionChain<T> Equal(IEnumerable<T> e, string? because = null, params object?[] args)
        { if (!Cmp.SeqEq(_items, e)) Cmp.Fail($"expected {Cmp.Show(e)} but found {Cmp.Show(_items)}", because); return this; }
        public CollectionChain<T> OnlyHaveUniqueItems(string? because = null, params object?[] args)
        {
            var seen = new List<T>();
            foreach (var x in _items)
            {
                if (seen.Any(y => Cmp.Eq(x, y)))
                    Cmp.Fail($"expected only unique items but {Cmp.Show(x)} appears more than once", because);
                seen.Add(x);
            }
            return this;
        }
    }

    public static class AssertionExtensions
    {
        /// <summary>`obj.Invoking(o => o.M()).Should().Throw&lt;T&gt;()` の形を通すための拡張。
        /// 公表された FluentAssertions の API。</summary>
        public static Action Invoking<T>(this T subject, Action<T> action)
            => () => action(subject);
        public static Func<Task> InvokingAsync<T>(this T subject, Func<T, Task> action)
            => () => action(subject);

        public static Chain Should(this object? subject) => new(subject);
        public static CollectionChain<T> Should<T>(this IEnumerable<T>? subject) => new(subject);
        public static Chain Should(this Action action) => new(action);
        public static Chain Should(this Func<Task> action) => new(action);
    }
}
