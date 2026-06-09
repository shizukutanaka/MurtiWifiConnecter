using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MWC.App.Services;

/// <summary>
/// アプリ設定の読み書き。
/// Apple 原則: ユーザーが理解できる選択肢だけ。技術パラメータを隠す。
/// </summary>
public sealed class SettingsService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MWC", "settings.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<SettingsService> _log;
    private AppSettings _current;

    public AppSettings Current => _current;

    public SettingsService(ILogger<SettingsService> log)
    {
        _log     = log;
        _current = Load();
    }

    public void Save(AppSettings settings)
    {
        _current = settings;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Opts));
            File.Move(tmp, ConfigPath, overwrite: true);
        }
        catch (Exception ex) { _log.LogError(ex, "Settings save failed"); }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppSettings>(json, Opts) ?? new();
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Settings load failed, using defaults"); }
        return new AppSettings();
    }
}

/// <summary>
/// アプリ設定。Apple 流: ユーザーが理解できる粒度のみ公開。
/// 技術的な詳細(WlanAPI タイムアウト値等)はコード定数で管理。
/// </summary>
public sealed record AppSettings
{
    /// <summary>表示モード: Simple(初心者) / Expert(上級者)</summary>
    public DisplayMode DisplayMode { get; init; } = DisplayMode.Simple;

    /// <summary>テーマ: Dark / Light / System</summary>
    public AppTheme Theme { get; init; } = AppTheme.Dark;

    /// <summary>UI 言語</summary>
    public string Language { get; init; } = "ja";

    /// <summary>自動スキャン間隔(秒). 0=無効</summary>
    public int AutoScanIntervalSeconds { get; init; } = 15;

    /// <summary>起動時に自動スキャン</summary>
    public bool ScanOnStartup { get; init; } = true;

    /// <summary>接続通知を表示</summary>
    public bool ShowConnectionNotifications { get; init; } = true;

    /// <summary>初回起動済みフラグ</summary>
    public bool HasCompletedFirstRun { get; init; } = false;

    /// <summary>ピン留めネットワーク(SSID リスト)</summary>
    public System.Collections.Generic.List<string> PinnedNetworks { get; init; } = new();

    /// <summary>ブラックリストネットワーク(非表示)</summary>
    public System.Collections.Generic.List<string> HiddenNetworks { get; init; } = new();
}

public enum DisplayMode { Simple, Expert }
public enum AppTheme    { Dark, Light, System }
