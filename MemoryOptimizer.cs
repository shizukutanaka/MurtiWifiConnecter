using System;
using System.Diagnostics;
using System.Runtime;

namespace MurtiWifiConnecter
{
    public static class MemoryOptimizer
    {
        private static readonly object _optimizationLock = new();
        private static DateTime _lastCleanup = DateTime.MinValue;
        private const double MemoryThresholdMB = 100;

        public static void OptimizeMemoryIfNeeded()
        {
            lock (_optimizationLock)
            {
                if ((DateTime.Now - _lastCleanup).TotalSeconds < 30)
                    return;

                try
                {
                    var memoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
                    if (memoryMB > MemoryThresholdMB)
                    {
                        ForceGarbageCollection();
                        _lastCleanup = DateTime.Now;
                    }
                }
                catch { }
            }
        }

        public static void ForceGarbageCollection()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }

        public static void StartMemoryMonitoring()
        {
            OptimizeMemorySettings();
        }

        private static void OptimizeMemorySettings()
        {
            try
            {
                GCSettings.LatencyMode = GCLatencyMode.Interactive;
            }
            catch { }
        }

        private static void OptimizeWorkingSet()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                process.MinWorkingSet = (IntPtr)(1024 * 1024);
                process.MaxWorkingSet = (IntPtr)(50 * 1024 * 1024);
            }
            catch { }
        }

        public static MemoryInfo GetMemoryInfo()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return new MemoryInfo
                {
                    WorkingSetMB = process.WorkingSet64 / 1024 / 1024,
                    PrivateMemoryMB = process.PrivateMemorySize64 / 1024 / 1024,
                    VirtualMemoryMB = process.VirtualMemorySize64 / 1024 / 1024,
                    GCMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024,
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2)
                };
            }
            catch
            {
                return new MemoryInfo();
            }
        }

        public static MemoryOptimizationReport GenerateOptimizationReport()
        {
            var memInfo = GetMemoryInfo();
            return new MemoryOptimizationReport
            {
                GeneratedAt = DateTime.Now,
                CurrentMemoryInfo = memInfo,
                LastOptimization = _lastCleanup
            };
        }
    }

    public class MemoryInfo
    {
        public long WorkingSetMB { get; set; }
        public long PrivateMemoryMB { get; set; }
        public long VirtualMemoryMB { get; set; }
        public long GCMemoryMB { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }

        public override string ToString()
        {
            return $"WorkingSet: {WorkingSetMB}MB, Private: {PrivateMemoryMB}MB, GC: {GCMemoryMB}MB";
        }
    }

    public class MemoryOptimizationReport
    {
        public DateTime GeneratedAt { get; set; }
        public MemoryInfo CurrentMemoryInfo { get; set; }
        public DateTime LastOptimization { get; set; }
    }
}