using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;

namespace MWC.Cli;

/// <summary>
/// mwc import-cat — eduroam CAT (Configuration Assistant Tool) の
/// eap-config XML を読み込んで接続する。
///
/// CAT XML が持つのは「組織側で決まる情報」だけである — SSID・EAP 種別・
/// RADIUS サーバ名・信頼ルート CA・匿名 (外部) アイデンティティ。
/// **利用者のユーザー名とパスワードは含まれない**。各利用者が自分の学内アカウントを
/// 後から入力する、というのが eduroam の設計そのものだからである。
/// したがって PEAP / EAP-TTLS では --username と -p が必須になる
/// (EAP-TLS はクライアント証明書認証なので不要)。
///
/// これは長らく「配線できない」とされていた機能で、理由は GUI にも CLI にも
/// Enterprise の資格情報入力が存在しなかったこと (docs/FEATURE-AUDIT.md §2a)。
/// 2026-07 に `mwc connect` の Enterprise 対応が入ったことで前提が解消した。
/// </summary>
public static partial class Program
{
    private static Command BuildImportCat(ServiceProvider sp)
    {
        var fileArg  = new Argument<string>("file", "Path to a CAT eap-config XML file");
        var userOpt  = new Option<string?>("--username",
            "Your account at the institution (required for PEAP / EAP-TTLS)");
        var pwOpt    = new Option<string?>(new[] { "-p", "--password" },
            "Your password. Omit to read from the MWC_PASSWORD environment variable.");
        var adapterOpt = new Option<string?>("--adapter", "Adapter GUID or name (default: first)");
        var timeoutOpt = new Option<int>("--timeout", () => 30, "Connection timeout in seconds");
        var dryRunOpt  = new Option<bool>("--dry-run",
            "Parse and show what would be used, without connecting");
        var jsonOpt    = new Option<bool>("--json", "Output JSON");

        var cmd = new Command("import-cat",
            "Connect using an eduroam CAT eap-config file " +
            "(the file supplies the institution's settings; you supply your own credentials)");
        cmd.AddArgument(fileArg);
        cmd.AddOption(userOpt); cmd.AddOption(pwOpt); cmd.AddOption(adapterOpt);
        cmd.AddOption(timeoutOpt); cmd.AddOption(dryRunOpt); cmd.AddOption(jsonOpt);

        // オプション数が SetHandler のジェネリック上限を超えるため InvocationContext 束縛
        // (BuildConnect と同じ理由・同じ方式)。
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var file = ctx.ParseResult.GetValueForArgument(fileArg);
            var user = ctx.ParseResult.GetValueForOption(userOpt);
            var pw   = ctx.ParseResult.GetValueForOption(pwOpt)
                       ?? Environment.GetEnvironmentVariable("MWC_PASSWORD");
            var af   = ctx.ParseResult.GetValueForOption(adapterOpt);
            var to   = ctx.ParseResult.GetValueForOption(timeoutOpt);
            var dry  = ctx.ParseResult.GetValueForOption(dryRunOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);

            try
            {
                if (to <= 0) { Err("--timeout must be a positive number of seconds"); Environment.Exit(ExitCode.InvalidInput); return; }
                if (!File.Exists(file)) { Err($"file not found: {file}"); Environment.Exit(ExitCode.InvalidInput); return; }

                string xml;
                try { xml = File.ReadAllText(file); }
                catch (IOException ex)                { Err($"cannot read {file}: {ex.Message}"); Environment.Exit(ExitCode.InvalidInput); return; }
                catch (UnauthorizedAccessException ex){ Err($"cannot read {file}: {ex.Message}"); Environment.Exit(ExitCode.InvalidInput); return; }

                var cat = new CatImportService();
                System.Collections.Generic.IReadOnlyList<CatProfile> profiles;
                try { profiles = cat.ParseEapConfig(xml); }
                catch (Exception ex) { Err($"not a valid CAT eap-config file: {ex.Message}"); Environment.Exit(ExitCode.InvalidInput); return; }

                var profile = profiles.FirstOrDefault(p => p.IsValid);
                if (profile is null)
                {
                    Err(profiles.Count == 0
                        ? "no identity providers found in the file"
                        : "the file contains no usable profile (needs an SSID, a RADIUS server name, and a supported EAP type)");
                    Environment.Exit(ExitCode.InvalidInput); return;
                }

                // 組織側の設定 + 利用者の資格情報 = 接続可能な spec。
                // CAT は資格情報を持たないので、ここで補うのが本コマンドの仕事。
                var spec = cat.BuildEduroamSpec(profile) with
                {
                    Username = string.IsNullOrWhiteSpace(user) ? null : user,
                    Password = string.IsNullOrEmpty(pw) ? null : pw,
                };

                var validation = spec.Validate();
                if (!validation.IsValid)
                {
                    Err($"{validation.Error} — CAT files never contain your credentials; " +
                        "pass --username and -p (or set MWC_PASSWORD)");
                    Environment.Exit(ExitCode.InvalidInput); return;
                }

                if (json || dry)
                {
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        organization = profile.OrganizationName,
                        ssid         = spec.Ssid,
                        eapType      = spec.EapType?.ToString(),
                        serverNames  = spec.ServerNames,
                        trustedCas   = spec.TrustedRootCaThumbprints,
                        outerIdentity = spec.Domain,
                        username     = spec.Username,
                        dryRun       = dry,
                    }));
                    if (dry) { Environment.Exit(ExitCode.Success); return; }
                }

                // spec が実際にプロファイル XML になることを接続前に確認する
                // (失敗を OsError に埋もれさせない — BuildConnect と同じ方針)。
                try { ProfileXmlBuilder.Build(spec); }
                catch (Exception ex) { Err($"profile: {ex.Message}"); Environment.Exit(ExitCode.InvalidInput); return; }

                var svc      = sp.GetRequiredService<IWifiService>();
                var executor = sp.GetRequiredService<ConnectionExecutor>();
                var ad       = await Resolve(svc, af);
                if (ad is null) { Err("adapter not found"); Environment.Exit(ExitCode.InvalidInput); return; }

                ConnectionResult res;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(to + 5));
                try { res = await executor.ConnectAsync(ad.Id, spec, TimeSpan.FromSeconds(to), cts.Token); }
                catch (OperationCanceledException) { Err("connection timed out"); Environment.Exit(ExitCode.ConnectionFailed); return; }

                if (res.Success)
                {
                    if (!json)
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            ssid     = res.ConnectedSsid,
                            internet = res.HasInternet,
                            captive  = res.BehindCaptivePortal,
                        }));
                    Environment.Exit(ExitCode.Success);
                }
                else
                {
                    var advice = TroubleshootingHelper.GetAdvice(
                        res.Failure ?? ConnectionFailure.Unknown, spec.Auth);
                    Err($"failed: {res.Failure} — {advice.Reason}");
                    foreach (var step in advice.Steps)
                        Console.Error.WriteLine($"  • {step}");
                    Environment.Exit(ExitCode.ConnectionFailed);
                }
            }
            catch (Exception ex) { Err($"import-cat failed: {ex.Message}"); Environment.Exit(ExitCode.GeneralError); }
        });

        return cmd;
    }
}
