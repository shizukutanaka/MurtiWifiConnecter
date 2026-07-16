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
    private readonly int _maxSsids;
    private readonly ConcurrentDictionary<string, SignalRingBuffer> _buffers = new();

    // 10s間隔×360 = 1時間。maxSsids は移動端末が生涯に見る全 SSID 分の
    // バッファ蓄積(1件約5KB)を防ぐ上限。
    public SignalHistoryService(int maxSamples = 360, int maxSsids = 256)
    {
        if (maxSamples < 2) throw new ArgumentOutOfRangeException(nameof(maxSamples));
        if (maxSsids  < 1) throw new ArgumentOutOfRangeException(nameof(maxSsids));
        _maxSamples = maxSamples;
        _maxSsids   = maxSsids;
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
        EvictIfOverCapacity();
    }

    /// <summary>SSID バッファ数が上限を超えたら、最終更新が最も古いものから退去させる。</summary>
    private void EvictIfOverCapacity()
    {
        while (_buffers.Count > _maxSsids)
        {
            string? oldestKey = null;
            var oldestAt = DateTimeOffset.MaxValue;
            foreach (var kv in _buffers)
            {
                var last = kv.Value.LastAt;
                if (last < oldestAt) { oldestAt = last; oldestKey = kv.Key; }
            }
            if (oldestKey is null) break;            // 競合で空になった等
            if (!_buffers.TryRemove(oldestKey, out _)) break;  // 他スレッドが先に削除
        }
    }

    /// <summary>指定 SSID の時系列を降順(新しい順)で返す。</summary>
    public IReadOnlyList<SignalSample> GetHistory(string ssid)
        => _buffers.TryGetValue(ssid, out var buf) ? buf.ToList() : Array.Empty<SignalSample>();

    /// <summary>指定時間より古いサンプルを全 SSID から削除(定期クリーンアップ)。
    /// 全サンプルが期限切れで空になったバッファは辞書からも除去する。</summary>
    public void Prune(TimeSpan olderThan)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        foreach (var kv in _buffers)
        {
            kv.Value.Prune(cutoff);
            if (kv.Value.IsEmpty) _buffers.TryRemove(kv.Key, out _);
        }
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

    /// <summary>最新サンプルの記録時刻。空なら MinValue (退去判定で最優先)。</summary>
    internal DateTimeOffset LastAt
    {
        get { lock (_lock) { return _count == 0 ? DateTimeOffset.MinValue
                                   : _ring[(_head - 1 + _ring.Length) % _ring.Length].At; } }
    }

    internal bool IsEmpty { get { lock (_lock) { return _count == 0; } } }

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
