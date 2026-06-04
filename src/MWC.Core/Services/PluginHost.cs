using System;
using System.Collections.Generic;
using System.Composition;
using System.Composition.Hosting;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using MWC.Core.Models;

namespace MWC.Core.Services;

// ══ プラグイン コントラクト ════════════════════════════════════════════

/// <summary>
/// MWC プラグインの基本インターフェース。
/// 外部 DLL に実装して MWC のプラグインフォルダに配置するだけで動作する。
///
/// 最小実装例:
/// <code>
/// [Export(typeof(IMwcPlugin))]
/// public class MyPlugin : IMwcPlugin
/// {
///     public string Name        => "MyPlugin";
///     public string Version     => "1.0.0";
///     public string Description => "My custom MWC plugin";
///     public Task OnNetworkScannedAsync(IReadOnlyList&lt;WifiNetwork&gt; networks) => Task.CompletedTask;
/// }
/// </code>
/// </summary>
public interface IMwcPlugin
{
    /// <summary>プラグイン名(一意であること)</summary>
    string Name        { get; }
    /// <summary>バージョン (semver)</summary>
    string Version     { get; }
    /// <summary>説明</summary>
    string Description { get; }

    /// <summary>スキャン完了時コールバック</summary>
    Task OnNetworkScannedAsync(IReadOnlyList<WifiNetwork> networks)
        => Task.CompletedTask;

    /// <summary>接続成功時コールバック</summary>
    Task OnConnectedAsync(string ssid, bool hasInternet)
        => Task.CompletedTask;

    /// <summary>切断時コールバック</summary>
    Task OnDisconnectedAsync(string? lastSsid)
        => Task.CompletedTask;

    /// <summary>スタートアップ。DI コンテナからサービスを受け取る機会</summary>
    Task InitializeAsync(IServiceProvider services)
        => Task.CompletedTask;

    /// <summary>シャットダウン</summary>
    Task ShutdownAsync()
        => Task.CompletedTask;
}

/// <summary>カスタムエクスポート属性</summary>
[MetadataAttribute]
[AttributeUsage(AttributeTargets.Class)]
public sealed class MwcPluginAttribute : ExportAttribute
{
    public MwcPluginAttribute() : base(typeof(IMwcPlugin)) { }
}

// ══ プラグインホスト ══════════════════════════════════════════════════

/// <summary>
/// MEF ベースのプラグインホスト。
/// PluginsDir (デフォルト: AppData/MWC/plugins/) の DLL を自動ロードする。
///
/// 設計原則:
///   - ゼロ外部依存 (System.Composition は .NET 標準ライブラリ)
///   - 各プラグインを個別 AssemblyLoadContext でロード(分離)
///   - 例外を握り潰さずログに流す
///   - プラグインが存在しない場合はノーオペレーション
/// </summary>
public sealed class PluginHost : IAsyncDisposable
{
    private readonly string              _pluginsDir;
    private readonly List<IMwcPlugin>    _plugins = new();
    private readonly List<AssemblyLoadContext> _contexts = new();
    private          bool                _initialized;
    private readonly Action<string>?     _log;

    public static string DefaultPluginsDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MWC", "plugins");

    /// <summary>コンストラクタ。プラグインディレクトリを指定する (デフォルト: AppData/MWC/plugins/)。</summary>
    /// <summary>コンストラクタ。プラグインディレクトリと任意のログ出力を指定する。</summary>
    public PluginHost(string? pluginsDir = null, Action<string>? log = null)
    {
        _pluginsDir = pluginsDir ?? DefaultPluginsDir;
        _log        = log;
    }

    /// <summary>ロード済みプラグイン一覧</summary>
    public IReadOnlyList<IMwcPlugin> Plugins => _plugins;

    /// <summary>
    /// プラグインディレクトリから全 DLL をロード。
    /// </summary>
    public void LoadPlugins()
    {
        if (!Directory.Exists(_pluginsDir)) return;

        foreach (var dll in Directory.GetFiles(_pluginsDir, "*.dll"))
        {
            LoadAssembly(dll);
        }
    }

    private void LoadAssembly(string dllPath)
    {
        try
        {
            var ctx      = new PluginLoadContext(dllPath);
            var asm      = ctx.LoadFromAssemblyPath(dllPath);
            _contexts.Add(ctx);

            // MEF でエクスポート解決
            var config  = new ContainerConfiguration().WithAssembly(asm);
            using var container = config.CreateContainer();
            var loaded  = container.GetExports<IMwcPlugin>();
            _plugins.AddRange(loaded);
        }
        catch (Exception ex)
        {
            // プラグインのロード失敗はホストを止めない
            System.Diagnostics.Debug.WriteLine(
                $"[PluginHost] Failed to load {Path.GetFileName(dllPath)}: {ex.Message}");
        }
    }

    /// <summary>全プラグインを初期化。</summary>
    public async Task InitializeAllAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var p in _plugins)
        {
            try   { await p.InitializeAsync(services).ConfigureAwait(false); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PluginHost] {p.Name} InitializeAsync failed: {ex.Message}");
            }
        }
    }

    /// <summary>スキャン完了を全プラグインに通知。</summary>
    public async Task NotifyScanAsync(IReadOnlyList<WifiNetwork> networks, CancellationToken ct = default)
    {
        foreach (var p in _plugins)
        {
            try   { await p.OnNetworkScannedAsync(networks).ConfigureAwait(false); }
            catch (Exception ex) { _log?.Invoke($"[PluginHost] {p.Name}.OnNetworkScannedAsync: {ex.Message}"); }
        }
    }

    /// <summary>接続完了を全プラグインに通知。</summary>
    public async Task NotifyConnectedAsync(string ssid, bool hasInternet, CancellationToken ct = default)
    {
        foreach (var p in _plugins)
        {
            try   { await p.OnConnectedAsync(ssid, hasInternet).ConfigureAwait(false); }
            catch (Exception ex) { _log?.Invoke($"[PluginHost] {p.Name}.OnConnectedAsync: {ex.Message}"); }
        }
    }

    /// <summary>切断を全プラグインに通知。</summary>
    public async Task NotifyDisconnectedAsync(string? lastSsid, CancellationToken ct = default)
    {
        foreach (var p in _plugins)
        {
            try   { await p.OnDisconnectedAsync(lastSsid).ConfigureAwait(false); }
            catch (Exception ex) { _log?.Invoke($"[PluginHost] {p.Name}.OnDisconnectedAsync: {ex.Message}"); }
        }
    }

    /// <summary>全プラグインをシャットダウンして DLL ロードコンテキストを解放する。</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var p in _plugins)
        {
            try { await p.ShutdownAsync().ConfigureAwait(false); }
            catch (Exception ex) { _log?.Invoke($"[PluginHost] {p.Name}.ShutdownAsync: {ex.Message}"); }
        }
        _plugins.Clear();
        foreach (var ctx in _contexts)
        {
            try { ctx.Unload(); }
            catch (Exception ex) { _log?.Invoke($"[PluginHost] Context.Unload: {ex.Message}"); }
        }
        _contexts.Clear();
    }
}

/// <summary>プラグインDLL を分離ロードするためのカスタムコンテキスト</summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginPath), isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
