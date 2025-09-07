using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter
{
    public class SignalStrengthMonitor : IDisposable
    {
        private readonly Timer _monitorTimer;
        private readonly ConnectionLogger _logger;
        private bool _disposed;
        private string _currentSSID = string.Empty;
        private int _previousSignalStrength = 100;
        private DateTime _lastAlertTime = DateTime.MinValue;
        private const int WeakSignalThreshold = 30;
        private const int CriticalSignalThreshold = 15;
        private const int AlertCooldownMinutes = 2;

        public event EventHandler<SignalAlertEventArgs>? SignalAlert;

        public SignalStrengthMonitor(ConnectionLogger logger)
        {
            _logger = logger;
            _monitorTimer = new Timer(CheckSignalStrength, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void StartMonitoring(string ssid, int currentSignal)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;

            _currentSSID = ssid;
            _previousSignalStrength = currentSignal;
            
            // 15秒ごとに信号強度をチェック
            _monitorTimer.Change(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }

        public void StopMonitoring()
        {
            _monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _currentSSID = string.Empty;
        }

        private async void CheckSignalStrength(object? state)
        {
            if (_disposed || string.IsNullOrEmpty(_currentSSID)) return;

            try
            {
                var currentSignal = await GetCurrentSignalStrengthAsync();
                if (currentSignal <= 0) return; // 取得失敗

                var signalChange = currentSignal - _previousSignalStrength;
                var alertType = DetermineAlertType(currentSignal, signalChange);

                if (alertType != SignalAlertType.None && ShouldSendAlert())
                {
                    var alert = new SignalAlertEventArgs
                    {
                        SSID = _currentSSID,
                        CurrentSignal = currentSignal,
                        PreviousSignal = _previousSignalStrength,
                        SignalChange = signalChange,
                        AlertType = alertType,
                        Timestamp = DateTime.Now
                    };

                    SignalAlert?.Invoke(this, alert);
                    _logger.Log(GetLogLevel(alertType), "SignalMonitor", GetAlertMessage(alert));
                    _lastAlertTime = DateTime.Now;
                }

                _previousSignalStrength = currentSignal;
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("SignalStrengthMonitor.CheckSignalStrength", ex, _logger);
            }
        }

        private async Task<int> GetCurrentSignalStrengthAsync()
        {
            try
            {
                var output = await NetworkUtils.ExecuteNetshCommandAsync("wlan show interfaces", 3000);
                if (string.IsNullOrEmpty(output)) return 0;

                var lines = output.Split('\n');
                
                // 現在接続中のSSIDか確認
                var connectedSSID = string.Empty;
                var signalStrength = 0;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                        !trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIndex = trimmed.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            connectedSSID = trimmed.Substring(colonIndex + 1).Trim();
                        }
                    }
                    else if (trimmed.StartsWith("Signal", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('%'))
                    {
                        var percentIndex = trimmed.IndexOf('%');
                        if (percentIndex > 0)
                        {
                            var signalStr = trimmed.Substring(0, percentIndex);
                            var spaceIndex = signalStr.LastIndexOf(' ');
                            if (spaceIndex > 0 && int.TryParse(signalStr.Substring(spaceIndex + 1), out signalStrength))
                            {
                                break;
                            }
                        }
                    }
                }

                // 現在のSSIDと一致する場合のみ信号強度を返す
                return string.Equals(connectedSSID, _currentSSID, StringComparison.OrdinalIgnoreCase) 
                    ? signalStrength : 0;
            }
            catch
            {
                return 0;
            }
        }

        private SignalAlertType DetermineAlertType(int currentSignal, int signalChange)
        {
            // 臨界的に弱い信号
            if (currentSignal <= CriticalSignalThreshold)
                return SignalAlertType.CriticalWeak;

            // 弱い信号
            if (currentSignal <= WeakSignalThreshold)
                return SignalAlertType.Weak;

            // 急激な信号低下（20%以上）
            if (signalChange <= -20 && _previousSignalStrength > WeakSignalThreshold)
                return SignalAlertType.RapidDegradation;

            // 大幅改善（30%以上向上）
            if (signalChange >= 30 && _previousSignalStrength <= WeakSignalThreshold)
                return SignalAlertType.SignificantImprovement;

            return SignalAlertType.None;
        }

        private bool ShouldSendAlert()
        {
            // クールダウン時間内は連続アラートを避ける
            return DateTime.Now - _lastAlertTime > TimeSpan.FromMinutes(AlertCooldownMinutes);
        }

        private ConnectionLogger.LogLevel GetLogLevel(SignalAlertType alertType)
        {
            return alertType switch
            {
                SignalAlertType.CriticalWeak => ConnectionLogger.LogLevel.Warning,
                SignalAlertType.Weak => ConnectionLogger.LogLevel.Warning,
                SignalAlertType.RapidDegradation => ConnectionLogger.LogLevel.Warning,
                SignalAlertType.SignificantImprovement => ConnectionLogger.LogLevel.Info,
                _ => ConnectionLogger.LogLevel.Debug
            };
        }

        private string GetAlertMessage(SignalAlertEventArgs alert)
        {
            return alert.AlertType switch
            {
                SignalAlertType.CriticalWeak => 
                    $"'{alert.SSID}' の信号が非常に弱いです ({alert.CurrentSignal}%)",
                SignalAlertType.Weak => 
                    $"'{alert.SSID}' の信号が弱くなっています ({alert.CurrentSignal}%)",
                SignalAlertType.RapidDegradation => 
                    $"'{alert.SSID}' の信号が急激に低下しました ({alert.PreviousSignal}% → {alert.CurrentSignal}%)",
                SignalAlertType.SignificantImprovement => 
                    $"'{alert.SSID}' の信号が大幅に改善されました ({alert.PreviousSignal}% → {alert.CurrentSignal}%)",
                _ => $"'{alert.SSID}' 信号強度: {alert.CurrentSignal}%"
            };
        }

        public void UpdateCurrentNetwork(string ssid, int signalStrength)
        {
            if (!string.Equals(_currentSSID, ssid, StringComparison.OrdinalIgnoreCase))
            {
                // 新しいネットワークに切り替わった
                _currentSSID = ssid;
                _previousSignalStrength = signalStrength;
                _lastAlertTime = DateTime.MinValue; // アラート履歴をリセット
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _monitorTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public enum SignalAlertType
    {
        None,
        Weak,
        CriticalWeak,
        RapidDegradation,
        SignificantImprovement
    }

    public class SignalAlertEventArgs : EventArgs
    {
        public string SSID { get; set; } = string.Empty;
        public int CurrentSignal { get; set; }
        public int PreviousSignal { get; set; }
        public int SignalChange { get; set; }
        public SignalAlertType AlertType { get; set; }
        public DateTime Timestamp { get; set; }

        public string GetUserFriendlyMessage()
        {
            return AlertType switch
            {
                SignalAlertType.CriticalWeak => $"⚠️ {SSID} の信号が非常に弱いです ({CurrentSignal}%)",
                SignalAlertType.Weak => $"📶 {SSID} の信号が弱くなっています ({CurrentSignal}%)",
                SignalAlertType.RapidDegradation => $"📉 {SSID} の信号が急激に低下しました",
                SignalAlertType.SignificantImprovement => $"📈 {SSID} の信号が改善されました",
                _ => $"{SSID}: {CurrentSignal}%"
            };
        }
    }
}