// .NET 8 ミニマルAPIによるWiFi接続ライブラリ
// 現在の複雑なC#実装に対する軽量・現代的な改善版

using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

// 軽量なJSONシリアライズ設定
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// 軽量なロガー設定
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Warning);
});

var app = builder.Build();

// グローバルエラーハンドリング
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Internal server error",
            timestamp = DateTime.UtcNow
        });
    });
});

// 軽量キャッシュ（ConcurrentDictionaryを使用）
var cache = new ConcurrentDictionary<string, (object data, DateTime expiry)>();
var cacheTimeout = TimeSpan.FromSeconds(30);

// レート制限（軽量版）
var rateLimit = new ConcurrentDictionary<string, DateTime>();
var minOperationInterval = TimeSpan.FromMilliseconds(100);

// プラットフォーム判定
var platform = DetectPlatform();

PlatformType DetectPlatform()
{
    return OperatingSystem.IsWindows() ? PlatformType.Windows :
           OperatingSystem.IsMacOS() ? PlatformType.MacOS :
           OperatingSystem.IsLinux() ? PlatformType.Linux :
           PlatformType.Unknown;
}

// モデル定義（レコードクラス使用）
public record NetworkInfo(
    string Ssid,
    int Signal,
    string Security,
    string Band,
    int? Channel = null,
    string? Bssid = null
);

public record ConnectionStatus(
    string Status,
    string? Ssid = null,
    int? Signal = null,
    string? IpAddress = null,
    string? Bssid = null,
    int? Channel = null,
    double? ReceiveRate = null,
    double? TransmitRate = null,
    DateTime CheckedAt = default
);

public enum PlatformType
{
    Windows, MacOS, Linux, Unknown
}

public enum WifiSecurityMode
{
    Open, Wep, Wpa, Wpa2, Wpa3
}

public enum WifiStandard
{
    WiFi4, WiFi5, WiFi6, WiFi7
}

// キャッシュヘルパー
T? GetCachedData<T>(string key) where T : class
{
    if (cache.TryGetValue(key, out var entry))
    {
        if (entry.expiry > DateTime.UtcNow)
        {
            return entry.data as T;
        }
        cache.TryRemove(key, out _);
    }
    return null;
}

void SetCachedData(string key, object data, TimeSpan timeout)
{
    cache[key] = (data, DateTime.UtcNow.Add(timeout));
}

void ClearCache()
{
    cache.Clear();
}

// レート制限チェック
bool CheckRateLimit(string operation)
{
    var now = DateTime.UtcNow;
    if (rateLimit.TryGetValue(operation, out var lastTime))
    {
        if (now - lastTime < minOperationInterval)
        {
            return false;
        }
    }
    rateLimit[operation] = now;
    return true;
}

// コマンド実行（シンプル版）
async Task<string> ExecuteCommandAsync(string command, string arguments, CancellationToken cancellationToken = default)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

    if (!process.Start())
        throw new InvalidOperationException("Failed to start process");

    await process.WaitForExitAsync(cancellationToken);

    var output = await outputTask;
    var error = await errorTask;

    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Command failed: {error}");

    return output;
}

async Task<string> ExecuteCommandWithRetryAsync(string command, string arguments, int maxRetries = 3, CancellationToken cancellationToken = default)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await ExecuteCommandAsync(command, arguments, cancellationToken);
        }
        catch
        {
            if (attempt == maxRetries)
                throw;
            await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
        }
    }
    throw new InvalidOperationException("Command execution failed");
}

// プラットフォームコマンド取得
void GetScanCommand(out string command, out string arguments)
{
    switch (platform)
    {
        case PlatformType.Windows:
            command = "netsh";
            arguments = "wlan show networks mode=bssid";
            break;
        case PlatformType.Linux:
            command = "nmcli";
            arguments = "-t -f SSID,SIGNAL,SECURITY,CHAN,FREQ device wifi list";
            break;
        case PlatformType.MacOS:
            command = "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";
            arguments = "-s";
            break;
        default:
            command = "netsh";
            arguments = "wlan show networks mode=bssid";
            break;
    }
}

void GetConnectCommand(string ssid, out string command, out string arguments)
{
    switch (platform)
    {
        case PlatformType.Windows:
            command = "netsh";
            arguments = $"wlan connect name=\"{ssid}\"";
            break;
        case PlatformType.Linux:
            command = "nmcli";
            arguments = $"device wifi connect \"{ssid}\"";
            break;
        case PlatformType.MacOS:
            command = "networksetup";
            arguments = $"-setairportnetwork en0 \"{ssid}\"";
            break;
        default:
            command = "netsh";
            arguments = $"wlan connect name=\"{ssid}\"";
            break;
    }
}

void GetDisconnectCommand(out string command, out string arguments)
{
    switch (platform)
    {
        case PlatformType.Windows:
            command = "netsh";
            arguments = "wlan disconnect";
            break;
        case PlatformType.Linux:
            command = "nmcli";
            arguments = "device disconnect wlan0";
            break;
        case PlatformType.MacOS:
            command = "networksetup";
            arguments = "-setairportpower en0 off";
            break;
        default:
            command = "netsh";
            arguments = "wlan disconnect";
            break;
    }
}

void GetStatusCommand(out string command, out string arguments)
{
    switch (platform)
    {
        case PlatformType.Windows:
            command = "netsh";
            arguments = "wlan show interfaces";
            break;
        case PlatformType.Linux:
            command = "nmcli";
            arguments = "-t -f STATE,CONNECTION general";
            break;
        case PlatformType.MacOS:
            command = "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";
            arguments = "-I";
            break;
        default:
            command = "netsh";
            arguments = "wlan show interfaces";
            break;
    }
}

void GetProfilesCommand(out string command, out string arguments)
{
    switch (platform)
    {
        case PlatformType.Windows:
            command = "netsh";
            arguments = "wlan show profiles";
            break;
        case PlatformType.Linux:
            command = "nmcli";
            arguments = "-t -f NAME connection show";
            break;
        case PlatformType.MacOS:
            command = "networksetup";
            arguments = "-listpreferredwirelessnetworks en0";
            break;
        default:
            command = "netsh";
            arguments = "wlan show profiles";
            break;
    }
}

void GetDeleteProfileCommand(string ssid, out string command, out string arguments)
{
    switch (platform)
    {
        case PlatformType.Windows:
            command = "netsh";
            arguments = $"wlan delete profile name=\"{ssid}\"";
            break;
        case PlatformType.Linux:
            command = "nmcli";
            arguments = $"connection delete \"{ssid}\"";
            break;
        case PlatformType.MacOS:
            command = "networksetup";
            arguments = $"-removepreferredwirelessnetwork en0 \"{ssid}\"";
            break;
        default:
            command = "netsh";
            arguments = $"wlan delete profile name=\"{ssid}\"";
            break;
    }
}

// パース関数（統合版）
List<NetworkInfo> ParseNetworkOutput(string output)
{
    return platform switch
    {
        PlatformType.Windows => ParseWindowsNetworks(output),
        PlatformType.Linux => ParseLinuxNetworks(output),
        PlatformType.MacOS => ParseMacOSNetworks(output),
        _ => ParseWindowsNetworks(output)
    };
}

List<NetworkInfo> ParseWindowsNetworks(string output)
{
    var networks = new List<NetworkInfo>();
    var lines = output.Split('\n');
    NetworkInfo? current = null;

    foreach (var line in lines)
    {
        var trimmed = line.Trim();

        if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase))
        {
            if (current is not null && !string.IsNullOrEmpty(current.Ssid))
            {
                networks.Add(current);
            }

            var parts = trimmed.Split(':');
            if (parts.Length >= 2)
            {
                var ssid = string.Join(":", parts.Skip(1)).Trim();
                if (!string.IsNullOrEmpty(ssid))
                {
                    current = new NetworkInfo(ssid, 0, "", "Unknown");
                }
            }
        }
        else if (current is not null)
        {
            if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(trimmed, @"(\d+)%");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var signal))
                {
                    current = current with { Signal = signal };
                }
            }
            else if (trimmed.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':');
                if (parts.Length >= 2)
                {
                    current = current with { Security = parts[^1].Trim() };
                }
            }
            else if (trimmed.Contains("Band", StringComparison.OrdinalIgnoreCase))
            {
                current = current with { Band = trimmed.Contains("5GHz") ? "5GHz" : "2.4GHz" };
            }
        }
    }

    if (current is not null && !string.IsNullOrEmpty(current.Ssid))
    {
        networks.Add(current);
    }

    return networks;
}

List<NetworkInfo> ParseLinuxNetworks(string output)
{
    var networks = new List<NetworkInfo>();

    foreach (var line in output.Trim().Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)))
    {
        var parts = line.Split(':');
        if (parts.Length >= 5)
        {
            var ssid = parts[0] == "--" ? "Hidden Network" : parts[0];
            var signal = int.TryParse(parts[1], out var s) ? s : 0;
            var security = parts[2];
            var band = parts[4] switch
            {
                var f when double.TryParse(f, out var freq) =>
                    freq >= 2400 && freq <= 2500 ? "2.4GHz" :
                    freq >= 5000 && freq <= 6000 ? "5GHz" : "Unknown",
                _ => "Unknown"
            };

            networks.Add(new NetworkInfo(ssid, signal, security, band));
        }
    }

    return networks;
}

List<NetworkInfo> ParseMacOSNetworks(string output)
{
    var networks = new List<NetworkInfo>();
    var lines = output.Trim().Split('\n');

    foreach (var line in lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
    {
        var parts = Regex.Split(line.Trim(), @"\s+");
        if (parts.Length >= 7)
        {
            var security = line.Contains("WPA") ? "WPA2" : "Open";
            var band = line.Contains("5GHz") ? "5GHz" : "2.4GHz";

            networks.Add(new NetworkInfo(parts[0], 80, security, band));
        }
    }

    return networks;
}

ConnectionStatus ParseConnectionStatus(string output)
{
    return platform switch
    {
        PlatformType.Windows => ParseWindowsStatus(output),
        PlatformType.Linux => ParseLinuxStatus(output),
        PlatformType.MacOS => ParseMacOSStatus(output),
        _ => ParseWindowsStatus(output)
    };
}

ConnectionStatus ParseWindowsStatus(string output)
{
    var status = new ConnectionStatus("Disconnected");
    var lines = output.Split('\n');

    foreach (var line in lines)
    {
        var trimmed = line.Trim();

        if (trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase))
        {
            status = status with
            {
                Status = trimmed.Contains("connected", StringComparison.OrdinalIgnoreCase) ? "Connected" : "Disconnected"
            };
        }
        else if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !trimmed.Contains("BSSID"))
        {
            var parts = trimmed.Split(':');
            if (parts.Length >= 2)
            {
                status = status with { Ssid = string.Join(":", parts.Skip(1)).Trim() };
            }
        }
        else if (trimmed.Contains("Signal", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(trimmed, @"(\d+)%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var signal))
            {
                status = status with { Signal = signal };
            }
        }
    }

    return status with { CheckedAt = DateTime.UtcNow };
}

ConnectionStatus ParseLinuxStatus(string output)
{
    return new ConnectionStatus(
        output.Contains("connected", StringComparison.OrdinalIgnoreCase) ? "Connected" : "Disconnected",
        CheckedAt: DateTime.UtcNow
    );
}

ConnectionStatus ParseMacOSStatus(string output)
{
    var status = new ConnectionStatus("Disconnected", CheckedAt: DateTime.UtcNow);
    var lines = output.Split('\n');

    foreach (var line in lines)
    {
        var trimmed = line.Trim();

        if (trimmed.StartsWith("SSID:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(':');
            if (parts.Length >= 2)
            {
                var ssid = string.Join(":", parts.Skip(1)).Trim();
                if (!string.IsNullOrEmpty(ssid))
                {
                    status = status with { Status = "Connected", Ssid = ssid };
                }
            }
        }
        else if (trimmed.StartsWith("agrCtlRSSI:", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(trimmed, @"(-?\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var rssi))
            {
                var signal = Math.Max(0, Math.Min(100, (rssi + 100) * 2));
                status = status with { Signal = (int)signal };
            }
        }
    }

    return status;
}

List<string> ParseProfilesOutput(string output)
{
    return platform switch
    {
        PlatformType.Windows => ParseWindowsProfiles(output),
        PlatformType.Linux => ParseLinuxProfiles(output),
        PlatformType.MacOS => ParseMacOSProfiles(output),
        _ => ParseWindowsProfiles(output)
    };
}

List<string> ParseWindowsProfiles(string output)
{
    var profiles = new List<string>();

    foreach (var line in output.Split('\n'))
    {
        if (line.Contains("All User Profile", StringComparison.OrdinalIgnoreCase))
        {
            var parts = line.Split(':');
            if (parts.Length >= 2)
            {
                var profile = parts[^1].Trim();
                if (!string.IsNullOrEmpty(profile))
                {
                    profiles.Add(profile);
                }
            }
        }
    }

    return profiles.Distinct().OrderBy(p => p).ToList();
}

List<string> ParseLinuxProfiles(string output)
{
    return output.Trim()
                .Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();
}

List<string> ParseMacOSProfiles(string output)
{
    return output.Trim()
                .Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("Preferred"))
                .Select(line => line.Trim())
                .ToList();
}

string GenerateWifiProfile(string ssid, string password)
{
    return $"""
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
            <name>{ssid}</name>
            <SSIDConfig>
                <SSID>
                    <name>{ssid}</name>
                </SSID>
            </SSIDConfig>
            <connectionType>ESS</connectionType>
            <connectionMode>auto</connectionMode>
            <MSM>
                <security>
                    <authEncryption>
                        <authentication>WPA2PSK</authentication>
                        <encryption>AES</encryption>
                        <useOneX>false</useOneX>
                    </authEncryption>
                    <sharedKey>
                        <keyType>passPhrase</keyType>
                        <protected>false</protected>
                        <keyMaterial>{password}</keyMaterial>
                    </sharedKey>
                </security>
            </MSM>
        </WLANProfile>
        """;
}

// APIエンドポイント
app.MapGet("/api/networks/scan", async (bool forceRefresh = false, CancellationToken cancellationToken = default) =>
{
    if (!CheckRateLimit("scan"))
        return Results.BadRequest("Rate limit exceeded");

    var cacheKey = "scan_networks";

    if (!forceRefresh && GetCachedData<List<NetworkInfo>>(cacheKey) is List<NetworkInfo> cached)
    {
        return Results.Ok(cached.OrderByDescending(n => n.Signal));
    }

    try
    {
        GetScanCommand(out var command, out var arguments);
        var output = await ExecuteCommandWithRetryAsync(command, arguments, 3, cancellationToken);
        var networks = ParseNetworkOutput(output);

        SetCachedData(cacheKey, networks, cacheTimeout);

        return Results.Ok(networks.OrderByDescending(n => n.Signal));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Scan failed: {ex.Message}");
    }
});

app.MapPost("/api/networks/connect", async (ConnectRequest request, CancellationToken cancellationToken = default) =>
{
    if (!CheckRateLimit("connect"))
        return Results.BadRequest("Rate limit exceeded");

    if (string.IsNullOrWhiteSpace(request.Ssid) || request.Ssid.Length > 32)
        return Results.BadRequest("Invalid SSID");

    try
    {
        GetConnectCommand(request.Ssid, out var command, out var arguments);

        if (!string.IsNullOrEmpty(request.Password))
        {
            // Windowsの場合のみプロファイル作成
            if (platform == PlatformType.Windows)
            {
                var profileXml = GenerateWifiProfile(request.Ssid, request.Password);
                var tempFile = $"wifi_profile_{request.Ssid.Replace(" ", "_")}.xml";

                await File.WriteAllTextAsync(tempFile, profileXml, cancellationToken);
                try
                {
                    await ExecuteCommandWithRetryAsync("netsh", $"wlan add profile filename=\"{tempFile}\" user=all", 3, cancellationToken);
                }
                finally
                {
                    File.Delete(tempFile);
                }
            }
        }

        var output = await ExecuteCommandWithRetryAsync(command, arguments, 3, cancellationToken);
        ClearCache();

        var success = output.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("Connection established", StringComparison.OrdinalIgnoreCase);

        return Results.Ok(new { success });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Connect failed: {ex.Message}");
    }
});

app.MapPost("/api/networks/disconnect", async (CancellationToken cancellationToken = default) =>
{
    if (!CheckRateLimit("disconnect"))
        return Results.BadRequest("Rate limit exceeded");

    try
    {
        GetDisconnectCommand(out var command, out var arguments);
        var output = await ExecuteCommandWithRetryAsync(command, arguments, 3, cancellationToken);
        ClearCache();

        var success = output.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("disconnected", StringComparison.OrdinalIgnoreCase);

        return Results.Ok(new { success });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Disconnect failed: {ex.Message}");
    }
});

app.MapGet("/api/networks/status", async (CancellationToken cancellationToken = default) =>
{
    try
    {
        GetStatusCommand(out var command, out var arguments);
        var output = await ExecuteCommandWithRetryAsync(command, arguments, 3, cancellationToken);
        var status = ParseConnectionStatus(output);

        return Results.Ok(status);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Status check failed: {ex.Message}");
    }
});

app.MapGet("/api/networks/profiles", async (CancellationToken cancellationToken = default) =>
{
    try
    {
        GetProfilesCommand(out var command, out var arguments);
        var output = await ExecuteCommandWithRetryAsync(command, arguments, 3, cancellationToken);
        var profiles = ParseProfilesOutput(output);

        return Results.Ok(profiles);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Get profiles failed: {ex.Message}");
    }
});

app.MapDelete("/api/networks/profiles/{ssid}", async (string ssid, CancellationToken cancellationToken = default) =>
{
    if (!CheckRateLimit("delete"))
        return Results.BadRequest("Rate limit exceeded");

    if (string.IsNullOrWhiteSpace(ssid))
        return Results.BadRequest("Invalid SSID");

    try
    {
        GetDeleteProfileCommand(ssid, out var command, out var arguments);
        var output = await ExecuteCommandWithRetryAsync(command, arguments, 3, cancellationToken);
        ClearCache();

        var success = output.Contains("deleted", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("successfully", StringComparison.OrdinalIgnoreCase);

        return Results.Ok(new { success });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Delete profile failed: {ex.Message}");
    }
});

// ヘルスチェック
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    platform = platform.ToString(),
    timestamp = DateTime.UtcNow
}));

// Swagger設定（開発時のみ）
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();

// リクエストモデル
public record ConnectRequest(string Ssid, string? Password = null);
