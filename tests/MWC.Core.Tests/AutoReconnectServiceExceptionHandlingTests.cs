using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MWC.App.Services;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  AutoReconnectService.WatchAsync — silent-fault fix (2026-07 quality pass)
//
//  Previously, Task.Delay(3000, ct) and the `await foreach` header itself
//  sat outside the method's only try/catch, so a non-cancellation exception
//  from IWifiService.SubscribeEventsAsync's enumeration would escape
//  WatchAsync entirely, leaving the Task stored in Start()'s _watchLoop
//  field faulted for the app's lifetime with nothing ever observing it.
//  WatchAsync now wraps the whole await-foreach in an outer try/catch, so
//  the background task always completes (successfully, from the runtime's
//  point of view) even when the underlying event subscription throws.
// ══════════════════════════════════════════════════════════════
public class AutoReconnectServiceExceptionHandlingTests
{
    // Throws a non-cancellation exception as soon as the first MoveNextAsync
    // is awaited, simulating IWifiService.SubscribeEventsAsync's enumeration
    // failing outright (not a graceful cancellation).
    private sealed class ThrowingSubscribeWifiService : IWifiService
    {
        public Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WifiAdapter>>(Array.Empty<WifiAdapter>());
        public Task<IReadOnlyList<WifiNetwork>> ScanAsync(Guid adapterId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WifiNetwork>>(Array.Empty<WifiNetwork>());
        public Task<bool> RegisterProfileAsync(Guid adapterId, string profileXml, bool overwrite, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<ConnectionResult> ConnectAsync(Guid adapterId, string profileName, string ssid,
            TimeSpan timeout, CancellationToken ct = default)
            => Task.FromResult(ConnectionResult.Fail(ConnectionFailure.Unknown));
        public Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<bool> DeleteProfileAsync(Guid adapterId, string profileName, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<IReadOnlyList<string>> ListProfilesAsync(Guid adapterId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public async IAsyncEnumerable<WifiEvent> SubscribeEventsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("simulated WLAN notification subscription failure");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    [Fact]
    public async Task WatchAsync_SubscribeEventsThrows_DoesNotFaultTheBackgroundTask()
    {
        var svc = new AutoReconnectService(
            new ThrowingSubscribeWifiService(),
            new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance),
            new NotificationService(NullLogger<NotificationService>.Instance),
            new AdapterPreferencesService(),
            new ConnectionExecutor(
                new ThrowingSubscribeWifiService(),
                new NetworkHistoryService(NullLogger<NetworkHistoryService>.Instance),
                NullLogger<ConnectionExecutor>.Instance),
            NullLogger<AutoReconnectService>.Instance);

        svc.Start();

        // DisposeAsync awaits the internal watch-loop Task. Before the fix, that Task
        // would be faulted (an unhandled InvalidOperationException from the enumeration),
        // and awaiting a faulted Task rethrows -- so this call would throw. After the fix,
        // WatchAsync's outer catch means the Task always completes without fault, so
        // DisposeAsync must complete cleanly regardless of what SubscribeEventsAsync did.
        var act = async () => await svc.DisposeAsync();
        await act.Should().NotThrowAsync(
            because: "WatchAsync's outer try/catch must prevent a non-cancellation " +
                     "exception from SubscribeEventsAsync's enumeration from faulting " +
                     "the background task stored in _watchLoop");
    }
}
