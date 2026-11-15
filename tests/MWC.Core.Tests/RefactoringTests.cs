using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.App.Services;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════
//  SafeFireAndForget
// ═══════════════════════════════════════════════
public class SafeFireAndForgetTests
{
    [Fact]
    public async Task ForgetTaskCompletesSuccessfullyNoException()
    {
        var task = Task.CompletedTask;
        task.Forget();  // 例外なし
        await Task.Yield();
        task.IsCompletedSuccessfully.Should().BeTrue();
        task.Exception.Should().BeNull();
    }

    [Fact]
    public async Task ForgetTaskFaultsDoesNotPropagate()
    {
        // 例外を投げる Task を fire-and-forget しても上に伝わらない
        var task = Task.Run(() => throw new InvalidOperationException("test"));
        task.Forget();  // 握り潰される
        await Task.Delay(50);
        // ここまで例外なく到達できれば成功
        true.Should().BeTrue("Forget must not propagate faulted task exception");
    }

    [Fact]
    public async Task RunReturnsTask()
    {
        var t = SafeFireAndForget.Run(async () =>
        {
            await Task.Delay(10);
        });
        await t;
        t.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task RunWithFallbackOnExceptionReturnsFallback()
    {
        var result = await SafeFireAndForget.RunWithFallback<string>(
            () => throw new InvalidOperationException("fail"),
            "fallback");
        result.Should().Be("fallback");
    }

    [Fact]
    public async Task RunWithFallbackOnSuccessReturnsValue()
    {
        var result = await SafeFireAndForget.RunWithFallback<string>(
            () => Task.FromResult("ok"), "fallback");
        result.Should().Be("ok");
    }
}

// ═══════════════════════════════════════════════
//  L (Localization)
// ═══════════════════════════════════════════════
public class LocalizationTests
{
    [Fact]
    public void GetKnownKeyReturnsTranslation()
    {
        var s = MWC.App.Resources.L.Get("Action_Refresh");
        s.Should().NotBe("Action_Refresh");  // フォールバックでない=翻訳成功
        s.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetUnknownKeyReturnsKeyAsFallback()
    {
        var s = MWC.App.Resources.L.Get("NonExistent_Key_12345");
        s.Should().Be("NonExistent_Key_12345");
    }

    [Fact]
    public void StaticPropertiesAllReturnNonEmpty()
    {
        MWC.App.Resources.L.AppTitle.Should().NotBeNullOrEmpty();
        MWC.App.Resources.L.ActionRefresh.Should().NotBeNullOrEmpty();
        MWC.App.Resources.L.ActionConnect.Should().NotBeNullOrEmpty();
        MWC.App.Resources.L.TabDetail.Should().NotBeNullOrEmpty();
        MWC.App.Resources.L.TabSignal.Should().NotBeNullOrEmpty();
        MWC.App.Resources.L.TabChannel.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FormatWithArgumentsSubstitutes()
    {
        var s = MWC.App.Resources.L.Format("Status_AdapterCount", 3);
        s.Should().Contain("3");
    }

    [Fact]
    public void FormatBadArgumentsDoesNotThrow()
    {
        // 引数が足りない場合でも例外を投げず、テンプレートをそのまま返す
        var act = () => MWC.App.Resources.L.Format("Status_AdapterCount" /* no args */);
        act.Should().NotThrow();
    }
}
