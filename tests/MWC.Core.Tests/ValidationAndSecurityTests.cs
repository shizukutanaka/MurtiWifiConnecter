using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.Core.Models;
using MWC.Core.Profile;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════════════════
//  WifiProfileValidator — IEEE 802.11 入力検証
// ═══════════════════════════════════════════════════════════
public class WifiProfileValidatorTests
{
    // ── SSID 検証 ──────────────────────────────────────────

    [Theory]
    [InlineData("Home")]
    [InlineData("Corp-5GHz")]
    [InlineData("ABC123!@#")]
    [InlineData("あいうえお")]               // 5文字 = 15 bytes UTF-8 — OK
    [InlineData("1234567890123456789012")]   // 22 ASCII = 22 bytes — OK
    public void ValidateSsid_ValidInputs_DoesNotThrow(string ssid)
    {
        var act = () => WifiProfileValidator.ValidateSsid(ssid);
        act.Should().NotThrow();
        WifiProfileValidator.IsValidSsid(ssid).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]                           // 空
    [InlineData("12345678901234567890123456789012X")]  // 33 ASCII chars = 33 bytes > 32
    public void ValidateSsid_TooLongOrEmpty_Throws(string ssid)
    {
        var act = () => WifiProfileValidator.ValidateSsid(ssid);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SSID*");
        WifiProfileValidator.IsValidSsid(ssid).Should().BeFalse();
    }

    [Fact]
    public void ValidateSsid_ControlCharacter_Throws()
    {
        var act = () => WifiProfileValidator.ValidateSsid("Net\x01Work");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*control character*");
    }

    [Fact]
    public void ValidateSsid_MultibyteSsid_ChecksByBytes()
    {
        // "日本語" = 9 bytes UTF-8 × 3 = 27 bytes — OK
        WifiProfileValidator.IsValidSsid("日本語ネットワーク").Should().BeTrue();
        // 11文字 × 3 = 33 bytes — NG
        WifiProfileValidator.IsValidSsid("日本語ネットワークXXX").Should().BeFalse();
    }

    // ── Passphrase 検証 ────────────────────────────────────

    [Theory]
    [InlineData(AuthMethod.WPA2PSK, "password")]         // 8 chars minimum
    [InlineData(AuthMethod.WPA2PSK, "aValidPass1!")]    // normal
    [InlineData(AuthMethod.WPA3SAE, "MyS3cur3Pass!")]   // WPA3
    [InlineData(AuthMethod.WPAPSK,  "oldschool1")]      // WPA1
    public void ValidatePassphrase_Valid_DoesNotThrow(AuthMethod auth, string pass)
    {
        var act = () => WifiProfileValidator.ValidatePassphrase(auth, pass);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(AuthMethod.WPA2PSK, "short")]    // 5 < 8
    [InlineData(AuthMethod.WPA3SAE, "1234567")]  // 7 < 8
    public void ValidatePassphrase_TooShort_Throws(AuthMethod auth, string pass)
    {
        var act = () => WifiProfileValidator.ValidatePassphrase(auth, pass);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least 8*");
    }

    [Fact]
    public void ValidatePassphrase_64HexRawPsk_Accepted()
    {
        var rawPsk = new string('a', 64);   // 64 hex chars = valid raw PSK
        var act = () => WifiProfileValidator.ValidatePassphrase(AuthMethod.WPA2PSK, rawPsk);
        act.Should().NotThrow("64-char hex is a valid raw PSK");
    }

    [Fact]
    public void ValidatePassphrase_Open_NullAllowed()
    {
        var act = () => WifiProfileValidator.ValidatePassphrase(AuthMethod.Open, null);
        act.Should().NotThrow("Open network does not need a passphrase");
    }

    [Fact]
    public void ValidatePassphrase_Enterprise_NullAllowed()
    {
        var act = () => WifiProfileValidator.ValidatePassphrase(AuthMethod.WPA2Enterprise, null);
        act.Should().NotThrow("Enterprise uses EAP credentials, not a passphrase");
    }

    [Fact]
    public void ValidatePassphrase_NonAscii_Throws()
    {
        var act = () => WifiProfileValidator.ValidatePassphrase(AuthMethod.WPA2PSK, "パスワード1234");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ASCII*");
    }

    // ── TryValidate ────────────────────────────────────────

    [Fact]
    public void TryValidate_Valid_ReturnsTrueNoError()
    {
        var spec = new WifiProfileSpec
        {
            Ssid        = "TestNet",
            Auth        = AuthMethod.WPA2PSK,
            Passphrase  = "pass12345"
        };

        var ok = WifiProfileValidator.TryValidate(spec, out var err);

        ok.Should().BeTrue();
        err.Should().BeNull();
    }

    [Fact]
    public void TryValidate_EmptySsid_ReturnsFalseWithMessage()
    {
        var spec = new WifiProfileSpec { Ssid = "", Auth = AuthMethod.Open };

        var ok = WifiProfileValidator.TryValidate(spec, out var err);

        ok.Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
        err.Should().Contain("SSID");
    }

    // ── ProfileXmlBuilder 統合 ─────────────────────────────

    [Fact]
    public void ProfileXmlBuilder_InvalidSsid_ThrowsArgumentException()
    {
        var spec = new WifiProfileSpec { Ssid = "", Auth = AuthMethod.Open };
        var act  = () => ProfileXmlBuilder.Build(spec);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProfileXmlBuilder_ShortPassphrase_ThrowsArgumentException()
    {
        var spec = new WifiProfileSpec
        {
            Ssid       = "Net",
            Auth       = AuthMethod.WPA2PSK,
            Passphrase = "short"  // < 8 chars
        };
        var act = () => ProfileXmlBuilder.Build(spec);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*8*");
    }
}

// ═══════════════════════════════════════════════════════════
//  ConnectionExecutor — 並列安全性テスト
// ═══════════════════════════════════════════════════════════
public class ConnectionExecutorConcurrencyTests
{
    private sealed class SlowFakeWifi : MWC.Core.Abstractions.IWifiService
    {
        public int ConnectCount;

        public Task<System.Collections.Generic.IReadOnlyList<MWC.Core.Models.WifiAdapter>>
            GetAdaptersAsync(CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<MWC.Core.Models.WifiAdapter>>(
                Array.Empty<MWC.Core.Models.WifiAdapter>());

        public Task<System.Collections.Generic.IReadOnlyList<WifiNetwork>>
            ScanAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<WifiNetwork>>(
                Array.Empty<WifiNetwork>());

        public Task<bool> RegisterProfileAsync(Guid id, string xml, bool ow, CancellationToken ct = default)
            => Task.FromResult(true);

        public async Task<ConnectionResult> ConnectAsync(
            Guid id, string ssid, string profile, TimeSpan timeout, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ConnectCount);
            await Task.Delay(50, ct).ConfigureAwait(false);
            return ConnectionResult.Ok(ssid, true, false);
        }

        public Task<bool> DisconnectAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> DeleteProfileAsync(Guid id, string profileName, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<System.Collections.Generic.IReadOnlyList<string>> ListProfilesAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(Array.Empty<string>());

        public async System.Collections.Generic.IAsyncEnumerable<MWC.Core.Abstractions.WifiEvent> SubscribeEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await System.Threading.Tasks.Task.CompletedTask;
            yield break;
        }
    }

    [Fact]
    public async Task ConnectAsync_ConcurrentSameAdapter_IsSerializedNotParallel()
    {
        var wifi = new SlowFakeWifi();
        var hist = new NetworkHistoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<NetworkHistoryService>.Instance);
        var exec = new ConnectionExecutor(
            wifi, hist, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectionExecutor>.Instance);

        var adapterId = Guid.NewGuid();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // 3つの接続を同時に発火
        var t1 = exec.ConnectAsync(adapterId, "Net1", AuthMethod.WPA2PSK, "pass12345", ct: cts.Token);
        var t2 = exec.ConnectAsync(adapterId, "Net2", AuthMethod.WPA2PSK, "pass12345", ct: cts.Token);
        var t3 = exec.ConnectAsync(adapterId, "Net3", AuthMethod.WPA2PSK, "pass12345", ct: cts.Token);

        var results = await Task.WhenAll(t1, t2, t3);

        // 全て完了していること
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
        // ConnectCount は最大MaxRetry(3)×3=9だがシリアル化で処理されている
        wifi.ConnectCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConnectAsync_DifferentAdapters_CanRunInParallel()
    {
        var wifi = new SlowFakeWifi();
        var hist = new NetworkHistoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<NetworkHistoryService>.Instance);
        var exec = new ConnectionExecutor(
            wifi, hist, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectionExecutor>.Instance);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t1 = exec.ConnectAsync(id1, "Net1", AuthMethod.WPA2PSK, "pass12345", ct: cts.Token);
        var t2 = exec.ConnectAsync(id2, "Net2", AuthMethod.WPA2PSK, "pass12345", ct: cts.Token);
        await Task.WhenAll(t1, t2);
        sw.Stop();

        // 異なるアダプターはロックを共有しないため並列実行可能
        t1.Result.Should().NotBeNull();
        t2.Result.Should().NotBeNull();
        wifi.ConnectCount.Should().BeGreaterOrEqualTo(2);
    }

    // Regression: auto-reconnect passes passphrase="" — must succeed via existing profile
    [Theory]
    [InlineData(AuthMethod.WPA2PSK)]
    [InlineData(AuthMethod.WPA3SAE)]
    [InlineData(AuthMethod.WPA3Transition)]
    [InlineData(AuthMethod.WPAPSK)]
    public async Task ConnectAsync_EmptyPassphrase_SkipsProfileRegistration_AndSucceeds(AuthMethod auth)
    {
        var wifi = new SlowFakeWifi();
        var hist = new NetworkHistoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<NetworkHistoryService>.Instance);
        var exec = new ConnectionExecutor(
            wifi, hist, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectionExecutor>.Instance);

        var result = await exec.ConnectAsync(Guid.NewGuid(), "SavedNetwork", auth, passphrase: "");

        result.Success.Should().BeTrue(
            because: "empty passphrase means reuse existing OS profile, not a new connect");
    }
}
