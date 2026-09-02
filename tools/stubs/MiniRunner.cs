// ─────────────────────────────────────────────────────────────────────────────
//  最小テストランナー。**tools/run-tests.sh 専用**で、製品には含めない。
//
//  なぜ在るか:
//    xunit のランナーは NuGet 経由でしか入らない (`api.nuget.org` はエグレス拒否)。
//    しかし [Fact] / [Theory] を反射で拾って呼ぶだけなら数十行で足り、
//    アサーションは TestFrameworks.Stub.cs が**実際に検証する**ようになったので、
//    **テストを本当に実行して合否を出せる**。
//
//  本物の xunit との差 (承知の上で使うこと):
//    - 並列実行なし。テストごとの分離もクラス単位の new のみ。
//    - IClassFixture / IAsyncLifetime / ITestOutputHelper は未対応 (該当は失敗として出る)。
//    - [MemberData] は静的メンバから読むが、複雑な生成器は未対応。
//    - Skip 属性は尊重する。
//    - アサーション意味論は近似 (BeEquivalentTo が構造比較でない等)。
//      **失敗したら、まず本物との差を疑うこと。** 断定する前に理由を確かめる。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MwcMiniRunner;

public static class Program
{
    private static bool Has(MemberInfo m, string attrName)
        => m.GetCustomAttributes().Any(a => a.GetType().Name == attrName);

    private static string? SkipReason(MethodInfo m)
        => m.GetCustomAttributes()
            .Where(a => a.GetType().Name is "FactAttribute" or "TheoryAttribute")
            .Select(a => a.GetType().GetProperty("Skip")?.GetValue(a) as string)
            .FirstOrDefault(s => !string.IsNullOrEmpty(s));

    private static IEnumerable<object?[]> Cases(MethodInfo m, Type owner)
    {
        var inline = m.GetCustomAttributes()
            .Where(a => a.GetType().Name == "InlineDataAttribute")
            // [InlineData(null)] は params の性質上 Data 自体が null になる。
            // 「引数なし」ではなく「null 1 個」として扱わないと引数個数が合わない。
            .Select(a => (object?[])(a.GetType().GetProperty("Data")?.GetValue(a) ?? new object?[] { null }))
            .ToList();
        if (inline.Count > 0) { foreach (var c in inline) yield return c; yield break; }

        foreach (var a in m.GetCustomAttributes().Where(x => x.GetType().Name == "MemberDataAttribute"))
        {
            var name = a.GetType().GetProperty("MemberName")?.GetValue(a) as string;
            if (name is null) continue;
            object? data = owner.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                        ?? owner.GetMethod(name, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            if (data is IEnumerable rows)
                foreach (var row in rows) yield return row as object?[] ?? new[] { row };
        }
    }

    public static int Main(string[] argv)
    {
        bool verbose = argv.Contains("--verbose");
        int pass = 0, fail = 0, skip = 0, error = 0;
        var failures = new List<string>();

        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsNested)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => Has(m, "FactAttribute") || Has(m, "TheoryAttribute"))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToList();
            if (methods.Count == 0) continue;

            foreach (var m in methods)
            {
                if (SkipReason(m) is not null) { skip++; continue; }

                var cases = Has(m, "TheoryAttribute") ? Cases(m, type).ToList() : new List<object?[]> { Array.Empty<object?>() };
                if (cases.Count == 0) { skip++; continue; }   // Theory but no data we can read

                foreach (var args in cases)
                {
                    object? instance;
                    try { instance = Activator.CreateInstance(type); }
                    catch (Exception ex) { error++; failures.Add($"{type.Name}: cannot construct ({ex.GetType().Name})"); break; }

                    try
                    {
                        // xunit は InlineData の値を仮引数の型へ変換する (int リテラル -> byte 等)。
                        // 変換しないと ArgumentException になり、製品の欠陥と紛らわしい。
                        var ps = m.GetParameters();
                        object?[]? call = args.Length == 0 ? null : args.Select((v, i) =>
                        {
                            if (v is null || i >= ps.Length) return v;
                            var want = Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType;
                            if (want.IsInstanceOfType(v)) return v;
                            try { return want.IsEnum ? Enum.ToObject(want, v) : Convert.ChangeType(v, want); }
                            catch { return v; }
                        }).ToArray();
                        var r = m.Invoke(instance, call);
                        if (r is Task t) t.GetAwaiter().GetResult();
                        pass++;
                        if (verbose) Console.WriteLine($"  PASS {type.Name}.{m.Name}");
                    }
                    catch (TargetInvocationException tie)
                    {
                        var ex = tie.InnerException ?? tie;
                        fail++;
                        failures.Add($"{type.Name}.{m.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        error++;
                        failures.Add($"{type.Name}.{m.Name}: harness error: {ex.GetType().Name}: {ex.Message}");
                    }
                    finally { (instance as IDisposable)?.Dispose(); }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"passed {pass}   failed {fail}   skipped {skip}   harness-errors {error}");
        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("first failures:");
            foreach (var f in failures.Take(25)) Console.WriteLine("  " + f);
            if (failures.Count > 25) Console.WriteLine($"  ... and {failures.Count - 25} more");
        }
        return fail + error == 0 ? 0 : 1;
    }
}
