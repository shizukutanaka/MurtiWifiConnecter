using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// SSID 毎の信号品質時系列を保持。
/// inSSIDer / NetSpot が持つ「リアルタイム信号グラフ」に相当。
///
/// 設計:
///  - リングバッファ(最大 N サンプル) で無制限成長を防ぐ
///  - スレッドセーフ(ConcurrentDictionary + 内部ロック)
///  - Core 層。プラットフォーム依存なし
/// </summary>
public sealed class SignalHistoryService
{
    private readonly int _maxSamples;
    private readonly ConcurrentDictionary<string, SignalRingBuffer> _buffers = new();

    public SignalHistoryService(int maxSamples = 360)  // 10s間隔×360 = 1時間
    {
        if (maxSamples < 2) throw new ArgumentOutOfRangeException(nameof(maxSamples));
        _maxSamples = maxSamples;
    }

    /// <summary>スキャン結果を全 SSID 分まとめて記録。</summary>
    public void Record(IEnumerable<WifiNetwork> networks)
    {
        var at = DateTimeOffset.UtcNow;
        foreach (var n in networks)
        {
            var buf = _buffers.GetOrAdd(n.Ssid, _ => new SignalRingBuffer(_maxSamples));
            buf.Push(at, n.SignalQuality, n.Rssi);
        }
    }

    /// <summary>指定 SSID の時系列を降順(新しい順)で返す。</summary>
    public IReadOnlyList<SignalSample> GetHistory(string ssid)
        => _buffers.TryGetValue(ssid, out var buf) ? buf.ToList() : Array.Empty<SignalSample>();

    /// <summary>指定時間より古いサンプルを全 SSID から削除(定期クリーンアップ)。</summary>
    public void Prune(TimeSpan olderThan)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        foreach (var buf in _buffers.Values)
            buf.Prune(cutoff);
    }

    /// <summary>指定 SSID の履歴を消去。</summary>
    public void Clear(string ssid) => _buffers.TryRemove(ssid, out _);

    public void ClearAll() => _buffers.Clear();
}

public readonly record struct SignalSample(
    DateTimeOffset At,
    int Quality,    // 0-100
    int? Rssi       // dBm
);

/// <summary>固定サイズリングバッファ。SSID 毎に 1 インスタンス。</summary>
internal sealed class SignalRingBuffer
{
    private readonly SignalSample[] _ring;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    internal SignalRingBuffer(int capacity) { _ring = new SignalSample[capacity]; }

    internal void Push(DateTimeOffset at, int quality, int? rssi)
    {
        lock (_lock)
        {
            _ring[_head % _ring.Length] = new SignalSample(at, quality, rssi);
            _head++;
            if (_count < _ring.Length) _count++;
        }
    }

    internal IReadOnlyList<SignalSample> ToList()
    {
        lock (_lock)
        {
            if (_count == 0) return Array.Empty<SignalSample>();
            var result = new SignalSample[_count];
            int start = (_head - _count + _ring.Length) % _ring.Length;
            for (int i = 0; i < _count; i++)
                result[i] = _ring[(start + i) % _ring.Length];
            // 新しい順(降順)
            Array.Reverse(result);
            return result;
        }
    }

    internal void Prune(DateTimeOffset cutoff)
    {
        // リングバッファの仕組み上、古いものは自然に上書きされるため
        // 厳密な削除は不要。一応サンプルをゼロクリアして容量を返す。
        lock (_lock)
        {
            int pruned = 0;
            int start = (_head - _count + _ring.Length) % _ring.Length;
            for (int i = 0; i < _count; i++)
            {
                var s = _ring[(start + i) % _ring.Length];
                if (s.At < cutoff) pruned++;
                else break;
            }
            _count -= pruned;
        }
    }
}
