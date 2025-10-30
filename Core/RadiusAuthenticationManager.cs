using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MurtiWifiConnecter.Core
{
    /// <summary>
    /// RADIUS認証サーバー統合を管理するクラス
    /// エンタープライズグレードの認証システムを提供
    /// </summary>
    public class RadiusAuthenticationManager
    {
        private readonly ILogger<RadiusAuthenticationManager> _logger;
        private readonly Dictionary<string, RadiusServerConfig> _servers;
        private readonly Random _random;

        public RadiusAuthenticationManager(ILogger<RadiusAuthenticationManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _servers = new Dictionary<string, RadiusServerConfig>();
            _random = new Random();
        }

        /// <summary>
        /// RADIUSサーバーを登録
        /// </summary>
        public async Task<bool> RegisterServerAsync(string serverName, RadiusServerConfig config)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(serverName))
                    throw new ArgumentException("サーバー名は必須です", nameof(serverName));

                if (_servers.ContainsKey(serverName))
                    throw new InvalidOperationException($"サーバー '{serverName}' は既に登録されています");

                // サーバー接続テスト
                var testResult = await TestServerConnectionAsync(config);
                if (!testResult.IsSuccess)
                {
                    await _logger.LogWarning("RADIUSサーバーの接続テストに失敗しました", serverName, new Dictionary<string, object>
                    {
                        ["server"] = config.Host,
                        ["port"] = config.Port,
                        ["error"] = testResult.ErrorMessage
                    });
                    return false;
                }

                _servers[serverName] = config;

                await _logger.LogInformation("RADIUSサーバーを登録しました", serverName, new Dictionary<string, object>
                {
                    ["serverName"] = serverName,
                    ["host"] = config.Host,
                    ["port"] = config.Port,
                    ["timeout"] = config.Timeout.TotalSeconds
                });

                return true;
            }
            catch (Exception ex)
            {
                await _logger.LogError("RADIUSサーバーの登録に失敗しました", serverName, ex);
                return false;
            }
        }

        /// <summary>
        /// ユーザーを認証
        /// </summary>
        public async Task<RadiusAuthenticationResult> AuthenticateUserAsync(string serverName, string username, string password)
        {
            try
            {
                if (!_servers.TryGetValue(serverName, out var config))
                    throw new KeyNotFoundException($"サーバー '{serverName}' が見つかりません");

                var result = await PerformRadiusAuthenticationAsync(config, username, password);

                await _logger.LogInformation("RADIUS認証を実行しました", serverName, new Dictionary<string, object>
                {
                    ["serverName"] = serverName,
                    ["username"] = username,
                    ["isSuccess"] = result.IsSuccess,
                    ["responseCode"] = result.ResponseCode
                });

                return result;
            }
            catch (Exception ex)
            {
                await _logger.LogError("RADIUS認証中にエラーが発生しました", serverName, ex);

                return new RadiusAuthenticationResult
                {
                    IsSuccess = false,
                    ResponseCode = RadiusResponseCode.AccessReject,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// RADIUSサーバー接続をテスト
        /// </summary>
        public async Task<RadiusConnectionTestResult> TestServerConnectionAsync(RadiusServerConfig config)
        {
            try
            {
                using var udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, (int)config.Timeout.TotalMilliseconds);

                var endpoint = new IPEndPoint(IPAddress.Parse(config.Host), config.Port);

                // Access-Requestパケットを作成（認証なしのテスト用）
                var packet = CreateRadiusPacket(RadiusCode.AccessRequest, config, "test-user", "test-password");

                await udpClient.SendAsync(packet, packet.Length, endpoint);

                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(config.Timeout);

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    return new RadiusConnectionTestResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "サーバーからの応答がタイムアウトしました"
                    };
                }

                var response = await receiveTask;
                var responsePacket = response.Buffer;

                // レスポンスコードを確認
                var responseCode = (RadiusCode)responsePacket[0];

                return new RadiusConnectionTestResult
                {
                    IsSuccess = responseCode == RadiusCode.AccessAccept || responseCode == RadiusCode.AccessReject,
                    ErrorMessage = responseCode == RadiusCode.AccessAccept ? null : "認証が拒否されました（テスト用）"
                };
            }
            catch (Exception ex)
            {
                return new RadiusConnectionTestResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<RadiusAuthenticationResult> PerformRadiusAuthenticationAsync(RadiusServerConfig config, string username, string password)
        {
            try
            {
                using var udpClient = new UdpClient();

                var endpoint = new IPEndPoint(IPAddress.Parse(config.Host), config.Port);

                // Access-Requestパケットを作成
                var packet = CreateRadiusPacket(RadiusCode.AccessRequest, config, username, password);

                await udpClient.SendAsync(packet, packet.Length, endpoint);

                var response = await udpClient.ReceiveAsync();
                var responsePacket = response.Buffer;

                // レスポンスコードを確認
                var responseCode = (RadiusCode)responsePacket[0];

                return new RadiusAuthenticationResult
                {
                    IsSuccess = responseCode == RadiusCode.AccessAccept,
                    ResponseCode = responseCode,
                    ErrorMessage = responseCode != RadiusCode.AccessAccept ? GetResponseCodeMessage(responseCode) : null
                };
            }
            catch (Exception ex)
            {
                return new RadiusAuthenticationResult
                {
                    IsSuccess = false,
                    ResponseCode = RadiusResponseCode.AccessReject,
                    ErrorMessage = ex.Message
                };
            }
        }

        private byte[] CreateRadiusPacket(RadiusCode code, RadiusServerConfig config, string username, string password)
        {
            var packet = new List<byte>();
            var identifier = (byte)_random.Next(0, 256);
            var authenticator = new byte[16];
            _random.NextBytes(authenticator);

            // RADIUSヘッダー
            packet.Add((byte)code);
            packet.Add(identifier);
            packet.AddRange(BitConverter.GetBytes((ushort)0)); // 長さ（後で設定）

            // Authenticator
            packet.AddRange(authenticator);

            // アトリビュートを追加
            AddRadiusAttribute(packet, RadiusAttributeType.UserName, Encoding.UTF8.GetBytes(username));
            AddRadiusAttribute(packet, RadiusAttributeType.UserPassword, EncodeUserPassword(password, authenticator, config.SharedSecret));

            // 長さを設定
            var length = (ushort)packet.Count;
            packet[2] = (byte)(length >> 8);
            packet[3] = (byte)(length & 0xFF);

            return packet.ToArray();
        }

        private void AddRadiusAttribute(List<byte> packet, RadiusAttributeType type, byte[] value)
        {
            packet.Add((byte)type);
            packet.Add((byte)(value.Length + 2)); // Type + Length + Value
            packet.AddRange(value);
        }

        private byte[] EncodeUserPassword(string password, byte[] requestAuthenticator, string sharedSecret)
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var sharedSecretBytes = Encoding.UTF8.GetBytes(sharedSecret);

            using var md5 = MD5.Create();

            var result = new byte[passwordBytes.Length];
            Array.Copy(passwordBytes, result, Math.Min(passwordBytes.Length, result.Length));

            // パスワードを16バイトのブロックに分割して暗号化
            for (int i = 0; i < result.Length; i += 16)
            {
                var block = new byte[16];
                Array.Copy(result, i, block, 0, Math.Min(16, result.Length - i));

                // MD5(sharedSecret + requestAuthenticator)
                var hashInput = new byte[sharedSecretBytes.Length + requestAuthenticator.Length];
                Array.Copy(sharedSecretBytes, 0, hashInput, 0, sharedSecretBytes.Length);
                Array.Copy(requestAuthenticator, 0, hashInput, sharedSecretBytes.Length, requestAuthenticator.Length);

                var hash = md5.ComputeHash(hashInput);

                // XOR
                for (int j = 0; j < Math.Min(16, block.Length); j++)
                {
                    result[i + j] = (byte)(block[j] ^ hash[j]);
                }
            }

            return result;
        }

        private string GetResponseCodeMessage(RadiusCode code)
        {
            return code switch
            {
                RadiusCode.AccessAccept => "認証が許可されました",
                RadiusCode.AccessReject => "認証が拒否されました",
                RadiusCode.AccessChallenge => "追加情報が必要です",
                _ => "不明なレスポンスコード"
            };
        }

        /// <summary>
        /// 登録済みサーバーを取得
        /// </summary>
        public IReadOnlyList<string> GetRegisteredServers()
        {
            return _servers.Keys.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// RADIUSサーバー設定
    /// </summary>
    public class RadiusServerConfig
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 1812;
        public string SharedSecret { get; set; } = "";
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
        public int RetryCount { get; set; } = 3;
    }

    /// <summary>
    /// RADIUS認証結果
    /// </summary>
    public class RadiusAuthenticationResult
    {
        public bool IsSuccess { get; set; }
        public RadiusResponseCode ResponseCode { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// RADIUS接続テスト結果
    /// </summary>
    public class RadiusConnectionTestResult
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// RADIUSコード
    /// </summary>
    public enum RadiusCode : byte
    {
        AccessRequest = 1,
        AccessAccept = 2,
        AccessReject = 3,
        AccessChallenge = 11
    }

    /// <summary>
    /// RADIUSレスポンスコード（エイリアス）
    /// </summary>
    public enum RadiusResponseCode
    {
        AccessAccept = 2,
        AccessReject = 3,
        AccessChallenge = 11
    }

    /// <summary>
    /// RADIUSアトリビュートタイプ
    /// </summary>
    public enum RadiusAttributeType : byte
    {
        UserName = 1,
        UserPassword = 2
    }
}
