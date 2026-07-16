using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MWC.App.ViewModels;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  ProfileManagerViewModel — silent-failure fix (2026-07 quality pass)
//
//  LoadAsync/DeleteAsync had `finally { IsBusy = false; }` with no
//  `catch` at all, and the class had no ILogger -- any IWifiService
//  exception was swallowed by the AsyncRelayCommand's ExecutionTask,
//  same bug class as AdapterViewModel.RefreshAsync fixed earlier.
//  This uses a minimal throwing IWifiService double rather than
//  extending the shared FakeWifiService, since no other test needs an
//  exception-simulating IWifiService.
// ══════════════════════════════════════════════════════════════
public class ProfileManagerViewModelErrorHandlingTests
{
    private sealed class ThrowingWifiService : IWifiService
    {
        public Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("adapter enumeration failed");
        public Task<IReadOnlyList<WifiNetwork>> ScanAsync(Guid adapterId, CancellationToken ct = default)
            => throw new InvalidOperationException("scan failed");
        public Task<bool> RegisterProfileAsync(Guid adapterId, string profileXml, bool overwrite, CancellationToken ct = default)
            => throw new InvalidOperationException("register failed");
        public Task<ConnectionResult> ConnectAsync(Guid adapterId, string profileName, string ssid,
            CancellationToken ct = default)
            => throw new InvalidOperationException("connect failed");
        public Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
            => throw new InvalidOperationException("disconnect failed");
        public Task<bool> DeleteProfileAsync(Guid adapterId, string profileName, CancellationToken ct = default)
            => throw new InvalidOperationException("delete failed: simulated WLAN API fault");
        public Task<IReadOnlyList<string>> ListProfilesAsync(Guid adapterId, CancellationToken ct = default)
            => throw new InvalidOperationException("list failed: simulated WLAN API fault");
        public async IAsyncEnumerable<WifiEvent> SubscribeEventsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [Fact]
    public async Task LoadAsync_WifiServiceThrows_SetsErrorStatusInsteadOfSwallowing()
    {
        var vm = new ProfileManagerViewModel(
            new ThrowingWifiService(),
            new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance),
            NullLogger<ProfileManagerViewModel>.Instance);

        await vm.LoadAsync(Guid.NewGuid());

        vm.IsBusy.Should().BeFalse("finally must still reset IsBusy even on failure");
        vm.StatusMessage.Should().NotBeNullOrEmpty(
            because: "the exception must surface to the user, not be silently swallowed by the AsyncRelayCommand");
        vm.StatusMessage.Should().Contain("list failed");
    }

    [Fact]
    public async Task DeleteAsync_WifiServiceThrows_SetsErrorStatusInsteadOfSwallowing()
    {
        var vm = new ProfileManagerViewModel(
            new ThrowingWifiService(),
            new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance),
            NullLogger<ProfileManagerViewModel>.Instance);
        vm.Selected = new ProfileItem("SomeNetwork");

        await vm.DeleteAsync();

        vm.IsBusy.Should().BeFalse();
        vm.StatusMessage.Should().NotBeNullOrEmpty();
        vm.StatusMessage.Should().Contain("delete failed");
    }
}
