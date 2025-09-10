using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Interfaces
{

    /// <summary>
    /// プロファイル管理サービス
    /// </summary>
    public interface IProfileService
    {
        void SaveProfile(string ssid, string password);
        string? GetSavedPassword(string ssid);
        void RemoveProfile(string ssid);
        List<string> GetSavedProfiles();
    }

    /// <summary>
    /// ログ管理サービス
    /// </summary>
    public interface ILoggingService
    {
        void LogConnection(string ssid, bool success, int signalStrength, string? errorMessage = null);
        void LogDisconnection(string ssid, string reason);
        void LogNetworkScan(int networksFound, long scanTimeMs);
        Task<List<string>> GetRecentLogsAsync(int count = 100);
    }

    /// <summary>
    /// 統計管理サービス
    /// </summary>
    public interface IStatisticsService
    {
        void RecordConnectionAttempt(string ssid, bool success);
        void RecordNetworkScan(int networksFound, long scanTimeMs);
        ConnectionStatisticsSummary GetSummary();
    }
}