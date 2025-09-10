using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Threading;

namespace MurtiWifiConnecter.Network
{
    /// <summary>
    /// ネットワークトポロジーマッパーインターフェース
    /// </summary>
    public interface INetworkTopologyMapper
    {
        Task<NetworkTopology> DiscoverNetworkTopologyAsync(string networkRange, CancellationToken cancellationToken = default);
        Task<List<NetworkDevice>> ScanForDevicesAsync(string subnet, CancellationToken cancellationToken = default);
        Task<DeviceInfo> GetDeviceInfoAsync(IPAddress ipAddress);
        Task<List<NetworkRoute>> TraceNetworkRoutesAsync(IPAddress targetAddress);
        Task<NetworkPerformanceMap> AnalyzeNetworkPerformanceAsync(NetworkTopology topology);
        Task<SecurityTopologyReport> AnalyzeSecurityTopologyAsync(NetworkTopology topology);
        Task<NetworkSegmentationReport> AnalyzeNetworkSegmentationAsync(NetworkTopology topology);
    }

    /// <summary>
    /// ネットワークトポロジーマッパーの実装
    /// </summary>
    public class NetworkTopologyMapper : INetworkTopologyMapper
    {
        private readonly int _pingTimeout;
        private readonly int _maxConcurrentPings;
        private readonly Dictionary<string, string> _vendorDatabase;

        public NetworkTopologyMapper(int pingTimeout = 1000, int maxConcurrentPings = 50)
        {
            _pingTimeout = pingTimeout;
            _maxConcurrentPings = maxConcurrentPings;
            _vendorDatabase = InitializeVendorDatabase();
        }

        /// <summary>
        /// ネットワークトポロジーを発見
        /// </summary>
        public async Task<NetworkTopology> DiscoverNetworkTopologyAsync(string networkRange, CancellationToken cancellationToken = default)
        {
            var topology = new NetworkTopology
            {
                NetworkRange = networkRange,
                DiscoveryDate = DateTime.Now,
                Devices = new List<NetworkDevice>(),
                Connections = new List<NetworkConnection>(),
                Subnets = new List<NetworkSubnet>()
            };

            try
            {
                // デバイスをスキャン
                var devices = await ScanForDevicesAsync(networkRange, cancellationToken);
                topology.Devices = devices;

                // ゲートウェイを特定
                var gateway = await DiscoverGatewayAsync();
                if (gateway != null)
                {
                    topology.Gateway = gateway;
                }

                // サブネットを分析
                topology.Subnets = AnalyzeSubnets(devices);

                // デバイス間の接続を分析
                topology.Connections = await AnalyzeDeviceConnectionsAsync(devices, cancellationToken);

                // ネットワークサービスを発見
                topology.Services = await DiscoverNetworkServicesAsync(devices, cancellationToken);

                // トポロジーメトリクスを計算
                topology.Metrics = CalculateTopologyMetrics(topology);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                topology.Errors.Add($"トポロジー発見エラー: {ex.Message}");
            }

            return topology;
        }

        /// <summary>
        /// デバイスをスキャン
        /// </summary>
        public async Task<List<NetworkDevice>> ScanForDevicesAsync(string subnet, CancellationToken cancellationToken = default)
        {
            var devices = new List<NetworkDevice>();
            var semaphore = new SemaphoreSlim(_maxConcurrentPings, _maxConcurrentPings);

            try
            {
                var ipRange = GenerateIPRange(subnet);
                var tasks = ipRange.Select(async ip =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var device = await DiscoverDeviceAsync(ip, cancellationToken);
                        if (device != null)
                        {
                            lock (devices)
                            {
                                devices.Add(device);
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            return devices.OrderBy(d => d.IPAddress.ToString()).ToList();
        }

        /// <summary>
        /// デバイス情報を取得
        /// </summary>
        public async Task<DeviceInfo> GetDeviceInfoAsync(IPAddress ipAddress)
        {
            var deviceInfo = new DeviceInfo
            {
                IPAddress = ipAddress,
                DiscoveryDate = DateTime.Now
            };

            try
            {
                // Ping テスト
                using var ping = new Ping();
                var pingReply = await ping.SendPingAsync(ipAddress, _pingTimeout);
                deviceInfo.IsReachable = pingReply.Status == IPStatus.Success;
                deviceInfo.ResponseTime = pingReply.RoundtripTime;

                if (deviceInfo.IsReachable)
                {
                    // DNS逆引き
                    try
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                        deviceInfo.HostName = hostEntry.HostName;
                    }
                    catch
                    {
                        deviceInfo.HostName = "Unknown";
                    }

                    // MACアドレス取得
                    deviceInfo.MacAddress = await GetMacAddressAsync(ipAddress);

                    // ベンダー情報
                    if (!string.IsNullOrEmpty(deviceInfo.MacAddress))
                    {
                        deviceInfo.Vendor = GetVendorFromMac(deviceInfo.MacAddress);
                    }

                    // オープンポートスキャン
                    deviceInfo.OpenPorts = await ScanCommonPortsAsync(ipAddress);

                    // OSフィンガープリンティング
                    deviceInfo.OSFingerprint = await DetectOperatingSystemAsync(ipAddress);

                    // デバイスタイプ推定
                    deviceInfo.DeviceType = EstimateDeviceType(deviceInfo);
                }
            }
            catch (Exception ex)
            {
                deviceInfo.Errors.Add($"デバイス情報取得エラー: {ex.Message}");
            }

            return deviceInfo;
        }

        /// <summary>
        /// ネットワークルートを追跡
        /// </summary>
        public async Task<List<NetworkRoute>> TraceNetworkRoutesAsync(IPAddress targetAddress)
        {
            var routes = new List<NetworkRoute>();

            try
            {
                using var ping = new Ping();
                var ttl = 1;
                var maxHops = 30;
                IPAddress currentHop = null;

                while (ttl <= maxHops && (currentHop == null || !currentHop.Equals(targetAddress)))
                {
                    var options = new PingOptions(ttl, true);
                    var reply = await ping.SendPingAsync(targetAddress, _pingTimeout, new byte[32], options);

                    var route = new NetworkRoute
                    {
                        HopNumber = ttl,
                        Address = reply.Address,
                        ResponseTime = reply.RoundtripTime,
                        Status = reply.Status
                    };

                    if (reply.Address != null)
                    {
                        currentHop = reply.Address;
                        try
                        {
                            var hostEntry = await Dns.GetHostEntryAsync(reply.Address);
                            route.HostName = hostEntry.HostName;
                        }
                        catch
                        {
                            route.HostName = "Unknown";
                        }
                    }

                    routes.Add(route);

                    if (reply.Status == IPStatus.Success)
                        break;

                    ttl++;
                }
            }
            catch (Exception ex)
            {
                routes.Add(new NetworkRoute
                {
                    HopNumber = 0,
                    Status = IPStatus.Unknown,
                    Errors = new List<string> { $"ルート追跡エラー: {ex.Message}" }
                });
            }

            return routes;
        }

        /// <summary>
        /// ネットワークパフォーマンスを分析
        /// </summary>
        public async Task<NetworkPerformanceMap> AnalyzeNetworkPerformanceAsync(NetworkTopology topology)
        {
            var performanceMap = new NetworkPerformanceMap
            {
                AnalysisDate = DateTime.Now,
                DevicePerformance = new List<DevicePerformanceInfo>(),
                ConnectionPerformance = new List<ConnectionPerformanceInfo>()
            };

            // 各デバイスのパフォーマンスを測定
            foreach (var device in topology.Devices)
            {
                var perfInfo = await MeasureDevicePerformanceAsync(device);
                performanceMap.DevicePerformance.Add(perfInfo);
            }

            // 接続パフォーマンスを測定
            foreach (var connection in topology.Connections)
            {
                var connPerf = await MeasureConnectionPerformanceAsync(connection);
                performanceMap.ConnectionPerformance.Add(connPerf);
            }

            // パフォーマンススコアを計算
            performanceMap.OverallScore = CalculateOverallPerformanceScore(performanceMap);

            return performanceMap;
        }

        /// <summary>
        /// セキュリティトポロジーを分析
        /// </summary>
        public async Task<SecurityTopologyReport> AnalyzeSecurityTopologyAsync(NetworkTopology topology)
        {
            var report = new SecurityTopologyReport
            {
                AnalysisDate = DateTime.Now,
                SecurityIssues = new List<TopologySecurityIssue>(),
                Recommendations = new List<SecurityTopologyRecommendation>()
            };

            // オープンポートの分析
            await AnalyzeOpenPortsAsync(topology, report);

            // ネットワークセグメンテーションの分析
            AnalyzeNetworkSegmentation(topology, report);

            // 不審なデバイスの検出
            DetectSuspiciousDevices(topology, report);

            // セキュリティベストプラクティスのチェック
            CheckSecurityBestPractices(topology, report);

            // セキュリティスコアを計算
            report.SecurityScore = CalculateSecurityScore(report);

            return report;
        }

        /// <summary>
        /// ネットワークセグメンテーションを分析
        /// </summary>
        public async Task<NetworkSegmentationReport> AnalyzeNetworkSegmentationAsync(NetworkTopology topology)
        {
            var report = new NetworkSegmentationReport
            {
                AnalysisDate = DateTime.Now,
                Segments = new List<NetworkSegment>()
            };

            // VLANの検出
            var vlans = DetectVLANs(topology);
            foreach (var vlan in vlans)
            {
                report.Segments.Add(new NetworkSegment
                {
                    SegmentType = SegmentType.VLAN,
                    Identifier = vlan.ToString(),
                    Devices = topology.Devices.Where(d => d.VLAN == vlan).ToList()
                });
            }

            // サブネットベースのセグメンテーション
            foreach (var subnet in topology.Subnets)
            {
                report.Segments.Add(new NetworkSegment
                {
                    SegmentType = SegmentType.Subnet,
                    Identifier = subnet.NetworkAddress,
                    Devices = subnet.Devices
                });
            }

            // セグメンテーション推奨事項
            report.Recommendations = GenerateSegmentationRecommendations(topology);

            return report;
        }

        #region Private Helper Methods

        private async Task<NetworkDevice> DiscoverDeviceAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                using var ping = new Ping();
                var pingReply = await ping.SendPingAsync(ipAddress, _pingTimeout);

                if (pingReply.Status == IPStatus.Success)
                {
                    var device = new NetworkDevice
                    {
                        IPAddress = ipAddress,
                        IsOnline = true,
                        ResponseTime = pingReply.RoundtripTime,
                        LastSeen = DateTime.Now
                    };

                    // 詳細情報を取得
                    var deviceInfo = await GetDeviceInfoAsync(ipAddress);
                    device.HostName = deviceInfo.HostName;
                    device.MacAddress = deviceInfo.MacAddress;
                    device.Vendor = deviceInfo.Vendor;
                    device.OpenPorts = deviceInfo.OpenPorts;
                    device.DeviceType = deviceInfo.DeviceType;

                    return device;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // デバイスが応答しない場合は無視
            }

            return null;
        }

        private async Task<IPAddress> DiscoverGatewayAsync()
        {
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var ni in networkInterfaces)
                {
                    var props = ni.GetIPProperties();
                    foreach (var gateway in props.GatewayAddresses)
                    {
                        if (gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            return gateway.Address;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gateway discovery failed: {ex.Message}");
            }

            return null;
        }

        private List<NetworkSubnet> AnalyzeSubnets(List<NetworkDevice> devices)
        {
            var subnets = new Dictionary<string, NetworkSubnet>();

            foreach (var device in devices)
            {
                var subnet = GetSubnetFromIP(device.IPAddress);
                if (!subnets.ContainsKey(subnet))
                {
                    subnets[subnet] = new NetworkSubnet
                    {
                        NetworkAddress = subnet,
                        Devices = new List<NetworkDevice>()
                    };
                }
                subnets[subnet].Devices.Add(device);
            }

            return subnets.Values.ToList();
        }

        private async Task<List<NetworkConnection>> AnalyzeDeviceConnectionsAsync(List<NetworkDevice> devices, CancellationToken cancellationToken)
        {
            var connections = new List<NetworkConnection>();

            // 基本的な接続性分析（実際の実装では、より詳細な分析が必要）
            foreach (var device in devices)
            {
                if (device.DeviceType == DeviceType.Router || device.DeviceType == DeviceType.Switch)
                {
                    // ルーターやスイッチから他のデバイスへの接続を推定
                    foreach (var otherDevice in devices.Where(d => d != device))
                    {
                        if (IsSameSubnet(device.IPAddress, otherDevice.IPAddress))
                        {
                            connections.Add(new NetworkConnection
                            {
                                SourceDevice = device,
                                TargetDevice = otherDevice,
                                ConnectionType = ConnectionType.Layer3,
                                Latency = Math.Abs(device.ResponseTime - otherDevice.ResponseTime)
                            });
                        }
                    }
                }
            }

            return connections;
        }

        private async Task<List<NetworkService>> DiscoverNetworkServicesAsync(List<NetworkDevice> devices, CancellationToken cancellationToken)
        {
            var services = new List<NetworkService>();

            foreach (var device in devices)
            {
                foreach (var port in device.OpenPorts)
                {
                    var service = new NetworkService
                    {
                        Device = device,
                        Port = port,
                        ServiceName = GetServiceName(port),
                        IsSecure = IsSecurePort(port)
                    };
                    services.Add(service);
                }
            }

            return services;
        }

        private List<IPAddress> GenerateIPRange(string subnet)
        {
            var ipRange = new List<IPAddress>();

            // 簡単な/24サブネット想定
            if (IPAddress.TryParse(subnet.Split('/')[0], out var baseIP))
            {
                var bytes = baseIP.GetAddressBytes();
                for (int i = 1; i < 255; i++)
                {
                    bytes[3] = (byte)i;
                    ipRange.Add(new IPAddress(bytes));
                }
            }

            return ipRange;
        }

        private async Task<string> GetMacAddressAsync(IPAddress ipAddress)
        {
            try
            {
                // ARP テーブルから MAC アドレスを取得
                // 実際の実装では、システムAPIやコマンドライン実行が必要
                return "00:00:00:00:00:00"; // プレースホルダー
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetVendorFromMac(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress) || macAddress.Length < 8)
                return "Unknown";

            var oui = macAddress.Substring(0, 8).Replace(":", "").ToUpper();
            return _vendorDatabase.GetValueOrDefault(oui, "Unknown");
        }

        private async Task<List<int>> ScanCommonPortsAsync(IPAddress ipAddress)
        {
            var openPorts = new List<int>();
            var commonPorts = new[] { 21, 22, 23, 25, 53, 80, 110, 143, 443, 993, 995 };

            foreach (var port in commonPorts)
            {
                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    var connectTask = client.ConnectAsync(ipAddress, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask)
                    {
                        if (client.Connected)
                        {
                            openPorts.Add(port);
                        }
                    }
                }
                catch
                {
                    // ポートが閉じている場合は無視
                }
            }

            return openPorts;
        }

        private async Task<string> DetectOperatingSystemAsync(IPAddress ipAddress)
        {
            // TTL値やその他の特徴からOSを推定
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ipAddress, 1000);
                
                // TTL値によるOS推定
                return reply.Options?.Ttl switch
                {
                    64 => "Linux/Unix",
                    128 => "Windows",
                    255 => "Cisco/Network Device",
                    _ => "Unknown"
                };
            }
            catch
            {
                return "Unknown";
            }
        }

        private DeviceType EstimateDeviceType(DeviceInfo deviceInfo)
        {
            // ベンダー、オープンポート、ホスト名などからデバイスタイプを推定
            if (deviceInfo.Vendor?.ToLower().Contains("cisco") == true)
                return DeviceType.Router;
            
            if (deviceInfo.OpenPorts.Contains(80) || deviceInfo.OpenPorts.Contains(443))
                return DeviceType.Server;
            
            if (deviceInfo.OpenPorts.Contains(22) || deviceInfo.OpenPorts.Contains(23))
                return DeviceType.Server;
            
            return DeviceType.Workstation;
        }

        private TopologyMetrics CalculateTopologyMetrics(NetworkTopology topology)
        {
            return new TopologyMetrics
            {
                TotalDevices = topology.Devices.Count,
                OnlineDevices = topology.Devices.Count(d => d.IsOnline),
                TotalSubnets = topology.Subnets.Count,
                TotalConnections = topology.Connections.Count,
                AverageResponseTime = topology.Devices.Where(d => d.IsOnline).Average(d => d.ResponseTime),
                NetworkComplexity = CalculateNetworkComplexity(topology)
            };
        }

        private double CalculateNetworkComplexity(NetworkTopology topology)
        {
            // ネットワークの複雑さを0-100の値で算出
            var deviceCount = topology.Devices.Count;
            var connectionCount = topology.Connections.Count;
            var subnetCount = topology.Subnets.Count;
            
            return Math.Min(100, (deviceCount * 2 + connectionCount + subnetCount * 5) / 10.0);
        }

        private Dictionary<string, string> InitializeVendorDatabase()
        {
            // IEEE OUI データベースの一部（実際の実装では完全なデータベースを使用）
            return new Dictionary<string, string>
            {
                ["00:50:56"] = "VMware",
                ["00:0C:29"] = "VMware",
                ["08:00:27"] = "VirtualBox",
                ["00:15:5D"] = "Microsoft",
                ["00:1B:21"] = "Intel",
                ["00:E0:4C"] = "Realtek",
                ["00:90:27"] = "Intel"
            };
        }

        private string GetSubnetFromIP(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        private bool IsSameSubnet(IPAddress ip1, IPAddress ip2)
        {
            return GetSubnetFromIP(ip1) == GetSubnetFromIP(ip2);
        }

        private string GetServiceName(int port)
        {
            return port switch
            {
                21 => "FTP",
                22 => "SSH",
                23 => "Telnet",
                25 => "SMTP",
                53 => "DNS",
                80 => "HTTP",
                110 => "POP3",
                143 => "IMAP",
                443 => "HTTPS",
                993 => "IMAPS",
                995 => "POP3S",
                _ => "Unknown"
            };
        }

        private bool IsSecurePort(int port)
        {
            return new[] { 22, 443, 993, 995 }.Contains(port);
        }

        private async Task<DevicePerformanceInfo> MeasureDevicePerformanceAsync(NetworkDevice device)
        {
            return new DevicePerformanceInfo
            {
                Device = device,
                ResponseTime = device.ResponseTime,
                PacketLoss = 0, // 実際の測定が必要
                Throughput = 0, // 実際の測定が必要
                MeasurementDate = DateTime.Now
            };
        }

        private async Task<ConnectionPerformanceInfo> MeasureConnectionPerformanceAsync(NetworkConnection connection)
        {
            return new ConnectionPerformanceInfo
            {
                Connection = connection,
                Latency = connection.Latency,
                Bandwidth = 0, // 実際の測定が必要
                PacketLoss = 0, // 実際の測定が必要
                MeasurementDate = DateTime.Now
            };
        }

        private double CalculateOverallPerformanceScore(NetworkPerformanceMap performanceMap)
        {
            if (!performanceMap.DevicePerformance.Any())
                return 0;

            var avgResponseTime = performanceMap.DevicePerformance.Average(d => d.ResponseTime);
            return Math.Max(0, 100 - (avgResponseTime / 10.0));
        }

        private async Task AnalyzeOpenPortsAsync(NetworkTopology topology, SecurityTopologyReport report)
        {
            foreach (var device in topology.Devices)
            {
                var riskyPorts = device.OpenPorts.Where(p => IsRiskyPort(p));
                foreach (var port in riskyPorts)
                {
                    report.SecurityIssues.Add(new TopologySecurityIssue
                    {
                        Device = device,
                        IssueType = "Risky Open Port",
                        Severity = SecuritySeverity.Medium,
                        Description = $"Device {device.IPAddress} has risky port {port} open",
                        Recommendation = $"Review if port {port} needs to be accessible"
                    });
                }
            }
        }

        private void AnalyzeNetworkSegmentation(NetworkTopology topology, SecurityTopologyReport report)
        {
            if (topology.Subnets.Count == 1)
            {
                report.SecurityIssues.Add(new TopologySecurityIssue
                {
                    IssueType = "Poor Network Segmentation",
                    Severity = SecuritySeverity.Medium,
                    Description = "Network appears to be flat with no segmentation",
                    Recommendation = "Consider implementing VLANs or subnets for better security"
                });
            }
        }

        private void DetectSuspiciousDevices(NetworkTopology topology, SecurityTopologyReport report)
        {
            foreach (var device in topology.Devices)
            {
                if (device.Vendor == "Unknown" && device.OpenPorts.Count > 5)
                {
                    report.SecurityIssues.Add(new TopologySecurityIssue
                    {
                        Device = device,
                        IssueType = "Suspicious Device",
                        Severity = SecuritySeverity.High,
                        Description = $"Unknown device {device.IPAddress} with many open ports",
                        Recommendation = "Investigate this device and verify its legitimacy"
                    });
                }
            }
        }

        private void CheckSecurityBestPractices(NetworkTopology topology, SecurityTopologyReport report)
        {
            // 管理インターフェースの検出
            foreach (var device in topology.Devices.Where(d => d.OpenPorts.Contains(23) || d.OpenPorts.Contains(80)))
            {
                report.SecurityIssues.Add(new TopologySecurityIssue
                {
                    Device = device,
                    IssueType = "Insecure Management Interface",
                    Severity = SecuritySeverity.High,
                    Description = "Device has insecure management interface exposed",
                    Recommendation = "Use secure protocols (SSH, HTTPS) for management"
                });
            }
        }

        private double CalculateSecurityScore(SecurityTopologyReport report)
        {
            var totalIssues = report.SecurityIssues.Count;
            var criticalIssues = report.SecurityIssues.Count(i => i.Severity == SecuritySeverity.Critical);
            var highIssues = report.SecurityIssues.Count(i => i.Severity == SecuritySeverity.High);
            
            var penalty = (criticalIssues * 30) + (highIssues * 20) + (totalIssues * 5);
            return Math.Max(0, 100 - penalty);
        }

        private List<int> DetectVLANs(NetworkTopology topology)
        {
            // VLAN検出ロジック（実際の実装では、SNMP等を使用）
            return new List<int> { 1 }; // デフォルトVLAN
        }

        private List<SegmentationRecommendation> GenerateSegmentationRecommendations(NetworkTopology topology)
        {
            var recommendations = new List<SegmentationRecommendation>();

            if (topology.Subnets.Count == 1)
            {
                recommendations.Add(new SegmentationRecommendation
                {
                    Priority = RecommendationPriority.High,
                    Description = "Implement network segmentation using VLANs",
                    Implementation = "Create separate VLANs for different device types and user groups"
                });
            }

            return recommendations;
        }

        private bool IsRiskyPort(int port)
        {
            var riskyPorts = new[] { 21, 23, 135, 139, 445, 1433, 3389 };
            return riskyPorts.Contains(port);
        }

        #endregion
    }

    #region Data Models

    public class NetworkTopology
    {
        public string NetworkRange { get; set; }
        public DateTime DiscoveryDate { get; set; }
        public List<NetworkDevice> Devices { get; set; } = new();
        public List<NetworkConnection> Connections { get; set; } = new();
        public List<NetworkSubnet> Subnets { get; set; } = new();
        public List<NetworkService> Services { get; set; } = new();
        public NetworkDevice Gateway { get; set; }
        public TopologyMetrics Metrics { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class NetworkDevice
    {
        public IPAddress IPAddress { get; set; }
        public string HostName { get; set; }
        public string MacAddress { get; set; }
        public string Vendor { get; set; }
        public bool IsOnline { get; set; }
        public long ResponseTime { get; set; }
        public List<int> OpenPorts { get; set; } = new();
        public DeviceType DeviceType { get; set; }
        public string OSFingerprint { get; set; }
        public DateTime LastSeen { get; set; }
        public int? VLAN { get; set; }
    }

    public class DeviceInfo
    {
        public IPAddress IPAddress { get; set; }
        public string HostName { get; set; }
        public string MacAddress { get; set; }
        public string Vendor { get; set; }
        public bool IsReachable { get; set; }
        public long ResponseTime { get; set; }
        public List<int> OpenPorts { get; set; } = new();
        public DeviceType DeviceType { get; set; }
        public string OSFingerprint { get; set; }
        public DateTime DiscoveryDate { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class NetworkConnection
    {
        public NetworkDevice SourceDevice { get; set; }
        public NetworkDevice TargetDevice { get; set; }
        public ConnectionType ConnectionType { get; set; }
        public long Latency { get; set; }
        public double Bandwidth { get; set; }
    }

    public class NetworkSubnet
    {
        public string NetworkAddress { get; set; }
        public List<NetworkDevice> Devices { get; set; } = new();
        public int VLAN { get; set; }
    }

    public class NetworkService
    {
        public NetworkDevice Device { get; set; }
        public int Port { get; set; }
        public string ServiceName { get; set; }
        public bool IsSecure { get; set; }
        public string Version { get; set; }
    }

    public class NetworkRoute
    {
        public int HopNumber { get; set; }
        public IPAddress Address { get; set; }
        public string HostName { get; set; }
        public long ResponseTime { get; set; }
        public IPStatus Status { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class TopologyMetrics
    {
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int TotalSubnets { get; set; }
        public int TotalConnections { get; set; }
        public double AverageResponseTime { get; set; }
        public double NetworkComplexity { get; set; }
    }

    public class NetworkPerformanceMap
    {
        public DateTime AnalysisDate { get; set; }
        public List<DevicePerformanceInfo> DevicePerformance { get; set; } = new();
        public List<ConnectionPerformanceInfo> ConnectionPerformance { get; set; } = new();
        public double OverallScore { get; set; }
    }

    public class DevicePerformanceInfo
    {
        public NetworkDevice Device { get; set; }
        public long ResponseTime { get; set; }
        public double PacketLoss { get; set; }
        public double Throughput { get; set; }
        public DateTime MeasurementDate { get; set; }
    }

    public class ConnectionPerformanceInfo
    {
        public NetworkConnection Connection { get; set; }
        public long Latency { get; set; }
        public double Bandwidth { get; set; }
        public double PacketLoss { get; set; }
        public DateTime MeasurementDate { get; set; }
    }

    public class SecurityTopologyReport
    {
        public DateTime AnalysisDate { get; set; }
        public List<TopologySecurityIssue> SecurityIssues { get; set; } = new();
        public List<SecurityTopologyRecommendation> Recommendations { get; set; } = new();
        public double SecurityScore { get; set; }
    }

    public class TopologySecurityIssue
    {
        public NetworkDevice Device { get; set; }
        public string IssueType { get; set; }
        public SecuritySeverity Severity { get; set; }
        public string Description { get; set; }
        public string Recommendation { get; set; }
    }

    public class SecurityTopologyRecommendation
    {
        public RecommendationPriority Priority { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string Implementation { get; set; }
    }

    public class NetworkSegmentationReport
    {
        public DateTime AnalysisDate { get; set; }
        public List<NetworkSegment> Segments { get; set; } = new();
        public List<SegmentationRecommendation> Recommendations { get; set; } = new();
    }

    public class NetworkSegment
    {
        public SegmentType SegmentType { get; set; }
        public string Identifier { get; set; }
        public List<NetworkDevice> Devices { get; set; } = new();
    }

    public class SegmentationRecommendation
    {
        public RecommendationPriority Priority { get; set; }
        public string Description { get; set; }
        public string Implementation { get; set; }
    }

    public enum DeviceType
    {
        Unknown,
        Workstation,
        Server,
        Router,
        Switch,
        AccessPoint,
        Printer,
        IoTDevice,
        Mobile
    }

    public enum ConnectionType
    {
        Layer2,
        Layer3,
        Wireless,
        VPN
    }

    public enum SegmentType
    {
        Subnet,
        VLAN,
        Physical
    }

    public enum SecuritySeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    #endregion
}