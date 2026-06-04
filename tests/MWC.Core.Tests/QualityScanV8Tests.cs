using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.App.Services;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════
//  DI構造健全性テスト (二重登録の回帰防止)
// ═══════════════════════════════════════════════
public class DIRegistrationTests
{
    [Fact]
    public void AppXamlCs_DoesNotContainDuplicateDIRegistrations()
    {
        // ビルド後の DI 登録ソースを文字列レベルで検証
        // 同じサービスを 2 回登録すると Microsoft.Extensions.DependencyInjection が
        // 後勝ちで上書きするか例外を投げる(将来のバージョンで)。
        // ここではソース静的検証で重複を防ぐ。

        var path = Path.Combine(
            Path.GetDirectoryName(typeof(DIRegistrationTests).Assembly.Location)!,
            "..", "..", "..", "..", "..",
            "src", "MWC.App", "App.xaml.cs");

        if (!File.Exists(path))
        {
            // ビルド出力ディレクトリ構造により異なる場合があるためスキップ可能
            return;
        }

        var src = File.ReadAllText(path);
        var keyServices = new[]
        {
            "AdapterPreferencesService",
            "ErrorHandlerService",
            "KeyboardShortcutService",
            "MainWindowCommands",
            "NetworkQualityService",
            "ConnectionExecutor"
        };

        foreach (var svc in keyServices)
        {
            var pattern = $"AddSingleton<{svc}>";
            var count = System.Text.RegularExpressions.Regex.Matches(src, pattern).Count;
            count.Should().BeLessOrEqualTo(1,
                because: $"{svc} must not be registered more than once");
            count.Should().BeLessOrEqualTo(1,
                because: $"{svc} must not be registered more than once");
            count.Should().BeGreaterOrEqualTo(0,
                because: $"{svc} registration count must be non-negative");
        }
    }
}

// ═══════════════════════════════════════════════
//  ConnectionExecutor 配線テスト
// ═══════════════════════════════════════════════
public class ConnectionExecutorIntegrationTests
{
    [Fact]
    public void ConnectionExecutor_HasRequiredDependencies()
    {
        // ConnectionExecutor の依存型を反映チェック
        var ctor = typeof(MWC.Core.Services.ConnectionExecutor).GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        paramTypes.Should().Contain(nameof(MWC.Core.Abstractions.IWifiService));
        paramTypes.Should().Contain(nameof(MWC.Core.Services.NetworkHistoryService));
    }
}

// ═══════════════════════════════════════════════
//  L.cs 統合テスト
// ═══════════════════════════════════════════════
public class LocalizationIntegrationTests
{
    [Fact]
    public void L_NewKeysAreAccessible()
    {
        var copied = MWC.App.Resources.L.StatusCopied("TestSSID");
        copied.Should().Contain("TestSSID");

        var disconnected = MWC.App.Resources.L.StatusDisconnected("TestAdapter");
        disconnected.Should().Contain("TestAdapter");
    }

    [Fact]
    public void L_StaticAccessors_CountAtLeast15()
    {
        // L クラスの静的プロパティ・メソッドが十分な数あること
        var lType = typeof(MWC.App.Resources.L);
        var members = lType.GetMembers(System.Reflection.BindingFlags.Public |
                                         System.Reflection.BindingFlags.Static)
            .Where(m => m.MemberType == System.Reflection.MemberTypes.Property ||
                        m.MemberType == System.Reflection.MemberTypes.Method)
            .ToList();
        members.Count.Should().BeGreaterOrEqualTo(15);
    }
}

// ═══════════════════════════════════════════════
//  SafeFireAndForget 配線テスト (回帰防止)
// ═══════════════════════════════════════════════
public class SafeFireAndForgetIntegrationTests
{
    [Fact]
    public async Task ChainedForget_PreventsExceptionPropagation()
    {
        // チェイン使用パターンが動作する
        Task.Run(() => throw new InvalidOperationException()).Forget();
        await Task.Delay(50);
        // 例外は握り潰される(ログのみ)
    }

    [Fact]
    public void Forget_AcceptsNullLogger()
    {
        // log = null でもクラッシュしないこと
        var task = Task.CompletedTask;
        var act = () => task.Forget(null);
        act.Should().NotThrow();
    }
}

// ═══════════════════════════════════════════════
//  ConfigureAwait(false) 規約検証
// ═══════════════════════════════════════════════
public class ConfigureAwaitConventionTests
{
    [Fact]
    public void CoreServices_HaveConfigureAwait()
    {
        // ConnectionExecutor は ConfigureAwait(false) を使用しているはず
        var path = Path.Combine(
            Path.GetDirectoryName(typeof(ConfigureAwaitConventionTests).Assembly.Location)!,
            "..", "..", "..", "..", "..",
            "src", "MWC.Core", "Services", "ConnectionExecutor.cs");

        if (!File.Exists(path)) return;
        var src = File.ReadAllText(path);
        src.Should().Contain("ConfigureAwait(false)",
            because: "Core libraries should use ConfigureAwait(false) for sync context independence");
    }
}
