using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;

namespace MWC.Cli;

public static partial class Program
{
    private static Command BuildPasspointConnect(ServiceProvider sp)
    {
        var carrier = new Option<string>("--carrier", "Carrier name from passpoint --carriers") { IsRequired = true };
        var username = new Option<string?>("--username", "Your carrier account username");
        var adapter = new Option<string?>("--adapter", "Adapter GUID or name (default: first)");
        var timeout = new Option<int>("--timeout", () => 30, "Connection timeout in seconds");
        var dryRun = new Option<bool>("--dry-run", "Validate the profile without connecting");
        var json = new Option<bool>("--json", "Output JSON");
        var cmd = new Command("connect", "Provision an automatic carrier profile and connect (SIM/AKA is unsupported)");
        cmd.AddOption(carrier); cmd.AddOption(username); cmd.AddOption(adapter);
        cmd.AddOption(timeout); cmd.AddOption(dryRun); cmd.AddOption(json);
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            try
            {
                var to = ctx.ParseResult.GetValueForOption(timeout);
                if (to <= 0) { Err("--timeout must be a positive number of seconds"); ctx.ExitCode = ExitCode.InvalidInput; return; }
                var preset = Hotspot20Service.KnownCarriers.FirstOrDefault(p =>
                    string.Equals(p.CarrierName, ctx.ParseResult.GetValueForOption(carrier), StringComparison.OrdinalIgnoreCase));
                if (preset is null) { Err("unknown carrier; use passpoint --carriers"); ctx.ExitCode = ExitCode.InvalidInput; return; }
                var spec = new Hotspot20Service().BuildCarrierProfile(preset) with
                {
                    Username = ctx.ParseResult.GetValueForOption(username),
                };
                if (spec.EapType == EapType.EAP_AKA)
                { Err("EAP-AKA (SIM-based) is not supported"); ctx.ExitCode = ExitCode.InvalidInput; return; }
                using var password = new SecureString();
                if (spec.EapType is EapType.PEAP_MSCHAPv2 or EapType.EAP_TTLS)
                {
                    if (string.IsNullOrWhiteSpace(spec.Username))
                    { Err("--username is required"); ctx.ExitCode = ExitCode.InvalidInput; return; }
                    if (Console.IsInputRedirected)
                    { Err("an interactive terminal is required for the password prompt"); ctx.ExitCode = ExitCode.InvalidInput; return; }
                    Console.Error.Write("Password: ");
                    while (true)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Enter) break;
                        if (key.Key == ConsoleKey.Backspace)
                        {
                            if (password.Length > 0) password.RemoveAt(password.Length - 1);
                        }
                        else if (!char.IsControl(key.KeyChar)) password.AppendChar(key.KeyChar);
                    }
                    Console.Error.WriteLine();
                }
                password.MakeReadOnly();
                var ptr = Marshal.SecureStringToGlobalAllocUnicode(password);
                try
                {
                    spec = spec with { Password = Marshal.PtrToStringUni(ptr) };
                    var validation = spec.Validate();
                    if (!validation.IsValid)
                    { Err(validation.Error ?? "invalid profile"); ctx.ExitCode = ExitCode.InvalidInput; return; }
                    try { ProfileXmlBuilder.Build(spec); }
                    catch (ArgumentException)
                    { Err("invalid carrier profile"); ctx.ExitCode = ExitCode.InvalidInput; return; }
                    if (ctx.ParseResult.GetValueForOption(dryRun))
                    {
                        Print(new { carrier = preset.CarrierName, ssid = spec.Ssid, eapType = spec.EapType.ToString(), dryRun = true });
                        return;
                    }
                    var svc = sp.GetRequiredService<IWifiService>();
                    var ad = await Resolve(svc, ctx.ParseResult.GetValueForOption(adapter));
                    if (ad is null) { Err("adapter not found"); ctx.ExitCode = ExitCode.InvalidInput; return; }
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds((double)to + 5));
                    ConnectionResult result;
                    try
                    {
                        result = await sp.GetRequiredService<ConnectionExecutor>()
                            .ConnectAsync(ad.Id, spec, TimeSpan.FromSeconds(to), cts.Token);
                    }
                    catch (OperationCanceledException)
                    { Err("connection timed out"); ctx.ExitCode = ExitCode.ConnectionFailed; return; }
                    if (result.Success)
                    {
                        if (ctx.ParseResult.GetValueForOption(json))
                            Print(new { ssid = result.ConnectedSsid, internet = result.HasInternet, captive = result.BehindCaptivePortal });
                        else Console.WriteLine($"Connected: {result.ConnectedSsid}");
                    }
                    else
                    {
                        var advice = TroubleshootingHelper.GetAdvice(result.Failure ?? ConnectionFailure.Unknown, spec.Auth);
                        Err($"failed: {result.Failure} — {advice.Reason}");
                        foreach (var step in advice.Steps) Console.Error.WriteLine($"  • {step}");
                        ctx.ExitCode = ExitCode.ConnectionFailed;
                    }
                }
                finally
                {
                    spec = spec with { Password = null };
                    Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not ThreadAbortException)
            { Err("passpoint connect failed"); ctx.ExitCode = ExitCode.GeneralError; }
        });
        return cmd;
    }
}
