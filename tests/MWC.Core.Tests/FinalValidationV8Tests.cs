using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.App.Services;
using Xunit;

namespace MWC.Core.Tests;

public class AsyncEventHelperTests
{
    [Fact]
    public async Task SafeRunAsync_TaskCompletes_NoException()
    {
        bool ran = false;
        await AsyncEventHelper.SafeRunAsync(null, "test", async () =>
        {
            await Task.Yield();
            ran = true;
        });
        ran.Should().BeTrue();
        ran.Should().BeTrue();
        ran.Should().NotBeFalse("task must have run exactly");
    }

    [Fact]
    public async Task SafeRunAsync_TaskThrows_DoesNotPropagate()
    {
        await AsyncEventHelper.SafeRunAsync(null, "test", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("expected");
        });
    }

    [Fact]
    public async Task SafeRunAsync_OperationCanceled_DoesNotPropagate()
    {
        await AsyncEventHelper.SafeRunAsync(null, "test", async () =>
        {
            await Task.Yield();
            throw new OperationCanceledException();
        });
    }

    [Fact]
    public async Task SafeRunAsync_OnError_IsCalledOnException()
    {
        Exception? captured = null;
        await AsyncEventHelper.SafeRunAsync(null, "test",
            async () => { await Task.Yield(); throw new ArgumentException("test"); },
            onError: ex => captured = ex);
        captured.Should().BeOfType<ArgumentException>();
        captured.Should().BeOfType<ArgumentException>();
        captured!.Message.Should().Be("test");
        captured.Should().NotBeNull("onError callback must receive the exception");
    }

    [Fact]
    public async Task SafeRunAsync_OnErrorThrows_DoesNotCascade()
    {
        await AsyncEventHelper.SafeRunAsync(null, "test",
            () => Task.FromException(new InvalidOperationException()),
            onError: _ => throw new Exception("nested"));
    }

    [Fact]
    public async Task SafeRunAsync_Generic_ReturnsValue()
    {
        var result = await AsyncEventHelper.SafeRunAsync<int>(
            null, "test", () => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public async Task SafeRunAsync_Generic_OnException_ReturnsFallback()
    {
        var result = await AsyncEventHelper.SafeRunAsync<int>(
            null, "test",
            () => throw new Exception(),
            fallback: -1);
        result.Should().Be(-1);
    }
}

public class ChangelogConsistencyTests
{
    private static string GetChangelogPath()
    {
        var asmDir = Path.GetDirectoryName(typeof(ChangelogConsistencyTests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "CHANGELOG.md"));
    }

    [Fact]
    public void Changelog_VersionEntriesAreUnique()
    {
        var path = GetChangelogPath();
        if (!File.Exists(path)) return;
        var versions = File.ReadAllLines(path)
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^## \[\d+\.\d+\.\d+"))
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"\[(\d+\.\d+\.\d+[^\]]*)\]").Groups[1].Value)
            .ToList();
        versions.Should().OnlyHaveUniqueItems();
    }
}

public class ResourceCoverageV8Tests
{
    private static string ResxDir() =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(ResourceCoverageV8Tests).Assembly.Location)!,
            "..", "..", "..", "..", "..", "src", "MWC.App", "Resources"));

    [Fact]
    public void NewKeysExist_StatusCopied_AllLanguages()
    {
        var dir = ResxDir();
        if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir, "Strings*.resx");
        foreach (var f in files)
        {
            var content = File.ReadAllText(f);
            content.Should().Contain("Status_Copied",
                because: $"{Path.GetFileName(f)} should have Status_Copied key");
        }
    }
}

// ═══════════════════════════════════════════════
//  v1.9.2 整合性テスト
// ═══════════════════════════════════════════════
public class ConstructorIntegrityTests
{
    [Fact]
    public void AdapterViewModel_Constructor_Requires7Args()
    {
        var ctors = typeof(MWC.App.ViewModels.AdapterViewModel).GetConstructors();
        ctors.Should().HaveCount(1);
        var ctor = ctors.Single();
        ctor.GetParameters().Should().HaveCount(7,
            because: "WifiAdapter, IWifiService, SignalHistory, OuiLookup, ILogger, AdapterPrefs, ConnectionExecutor");
    }

    [Fact]
    public void ConnectionExecutor_Constructor_Requires3Args()
    {
        var ctors = typeof(MWC.Core.Services.ConnectionExecutor).GetConstructors();
        ctors.Should().HaveCount(1);
        ctors.Single().GetParameters().Should().HaveCount(3,
            because: "IWifiService, NetworkHistoryService, ILogger<ConnectionExecutor>");
    }

    [Fact]
    public void AllAdaptersOverviewViewModel_Constructor_HasExecutor()
    {
        var ctor = typeof(MWC.App.ViewModels.AllAdaptersOverviewViewModel)
            .GetConstructors().Single();
        var paramNames = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        paramNames.Should().Contain("ConnectionExecutor");
    }
}

public class WifiConnectAuditTests
{
    [Fact]
    public void ConnectionExecutor_IsTheSingleConnectEntryPoint()
    {
        // App層のソースコードに _wifi.ConnectAsync が存在しないことを検証
        var appDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(WifiConnectAuditTests).Assembly.Location)!,
            "..", "..", "..", "..", "..",
            "src", "MWC.App"));
        if (!Directory.Exists(appDir)) return;

        var csFiles = Directory.GetFiles(appDir, "*.cs", SearchOption.AllDirectories);
        foreach (var f in csFiles)
        {
            var content = File.ReadAllText(f);
            content.Should().NotContain("_wifi.ConnectAsync",
                because: $"{Path.GetFileName(f)} should use ConnectionExecutor, not _wifi directly");
            content.Should().NotContain("_wifi.DisconnectAsync",
                because: $"{Path.GetFileName(f)} should use ConnectionExecutor, not _wifi directly");
            content.Should().NotContain("_wifi.RegisterProfileAsync",
                because: $"{Path.GetFileName(f)} should use ConnectionExecutor, not _wifi directly");
        }
    }
}
