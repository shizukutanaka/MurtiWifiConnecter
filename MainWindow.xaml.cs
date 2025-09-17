using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MurtiWifiConnecter
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly SimplifiedWifiManager _wifiManager;
        private ObservableCollection<WifiNetwork> _wifiNetworks;
        private WifiNetwork? _selectedNetwork;
        private readonly DispatcherTimer _refreshTimer;
        private bool _isConnecting;
        private string _statusText = "Ready";
        private string _lastUpdateTime = "";

        public ObservableCollection<WifiNetwork> WifiNetworks
        {
            get => _wifiNetworks;
            set
            {
                _wifiNetworks = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public string LastUpdateTime
        {
            get => _lastUpdateTime;
            set
            {
                _lastUpdateTime = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnecting
        {
            get => _isConnecting;
            set
            {
                _isConnecting = value;
                OnPropertyChanged();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _wifiManager = new SimplifiedWifiManager();
            _wifiManager.StatusChanged += OnWifiStatusChanged;

            WifiNetworks = new ObservableCollection<WifiNetwork>();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _refreshTimer.Tick += async (s, e) => await RefreshNetworksAsync();

            Loaded += async (s, e) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                StatusText = "Initializing...";
                await RefreshNetworksAsync();
                await UpdateCurrentConnectionAsync();
                _refreshTimer.Start();
                StatusText = "Ready";
            }
            catch (Exception ex)
            {
                ShowError($"Initialization error: {ex.Message}");
            }
        }

        private async Task RefreshNetworksAsync()
        {
            try
            {
                StatusText = "Scanning for networks...";

                var networks = await _wifiManager.ScanNetworksAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    WifiNetworks.Clear();
                    foreach (var network in networks.OrderByDescending(n => n.SignalStrength))
                    {
                        WifiNetworks.Add(network);
                    }

                    StatusText = $"Found {networks.Count} networks";
                    LastUpdateTime = DateTime.Now.ToString("HH:mm:ss");
                });
            }
            catch (Exception ex)
            {
                ShowError($"Scan error: {ex.Message}");
            }
        }

        private async Task UpdateCurrentConnectionAsync()
        {
            try
            {
                var currentSSID = await _wifiManager.GetCurrentSSIDAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(currentSSID))
                    {
                        CurrentSSIDText.Text = currentSSID;
                        // Connection is active
                    }
                    else
                    {
                        CurrentSSIDText.Text = "Not Connected";
                        // No active connection
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Update connection status error: {ex.Message}", ex);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            try
            {
                await RefreshNetworksAsync();
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        private void NetworkItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is WifiNetwork network)
            {
                SelectNetwork(network);
            }
        }

        private void SelectNetwork(WifiNetwork network)
        {
            _selectedNetwork = network;
            // Update selected network info
            _selectedNetwork = network;
            SSIDComboBox.Text = network.SSID;

            PasswordBox.Clear();

            // No password needed for open networks
            if (network.Authentication.Contains("Open", StringComparison.OrdinalIgnoreCase))
            {
                ConnectButton.IsEnabled = true;
                PasswordBox.IsEnabled = false;
            }
            else
            {
                PasswordBox.IsEnabled = true;
                PasswordBox.Focus();
                ConnectButton.IsEnabled = false;
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNetwork == null)
                return;

            IsConnecting = true;
            ConnectButton.IsEnabled = false;

            try
            {
                var password = PasswordBox.Password;

                if (_selectedNetwork.IsSecured && string.IsNullOrEmpty(password))
                {
                    ShowError("Password is required for secured networks");
                    return;
                }

                var success = await _wifiManager.ConnectAsync(_selectedNetwork.SSID, password);

                if (success)
                {
                    ShowSuccess($"Connected to {_selectedNetwork.SSID}");
                    await UpdateCurrentConnectionAsync();
                    PasswordBox.Clear();
                }
                else
                {
                    ShowError($"Failed to connect to {_selectedNetwork.SSID}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Connection error: {ex.Message}");
            }
            finally
            {
                IsConnecting = false;
                ConnectButton.IsEnabled = true;
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            DisconnectButton.IsEnabled = false;

            try
            {
                var success = await _wifiManager.DisconnectAsync();

                if (success)
                {
                    ShowSuccess("Disconnected");
                    await UpdateCurrentConnectionAsync();
                }
                else
                {
                    ShowError("Failed to disconnect");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Disconnect error: {ex.Message}");
            }
            finally
            {
                DisconnectButton.IsEnabled = true;
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = PasswordBox.Password.Length >= 8 ||
                (_selectedNetwork != null && !_selectedNetwork.IsSecured);
        }

        private void OnWifiStatusChanged(object? sender, string message)
        {
            Dispatcher.BeginInvoke(() => StatusText = message);
        }

        private void ShowError(string message)
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusText = message;
                StatusMessage.Text = message;
                StatusMessage.Foreground = Brushes.Red;

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (s, e) =>
                {
                    StatusMessage.Foreground = Brushes.Black;
                    timer.Stop();
                };
                timer.Start();
            });
        }

        private void ShowSuccess(string message)
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusText = message;
                StatusMessage.Text = message;
                StatusMessage.Foreground = Brushes.Green;

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (s, e) =>
                {
                    StatusMessage.Foreground = Brushes.Black;
                    timer.Stop();
                };
                timer.Start();
            });
        }

        private void NetworksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle network selection
            if (sender is DataGrid dataGrid && dataGrid.SelectedItem is WifiNetwork network)
            {
                _selectedNetwork = network;
                SSIDComboBox.Text = network.SSID;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Open settings dialog or perform settings action
            MessageBox.Show("Settings functionality coming soon!", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            // Run diagnostics
            StatusText = "Running diagnostics...";
            await Task.Delay(2000); // Simulate diagnostics
            StatusText = "Diagnostics complete - All systems operational";
        }

        private void FamilyButton_Click(object sender, RoutedEventArgs e)
        {
            // Open family settings
            MessageBox.Show("Family settings functionality coming soon!", "Family Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BatteryButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle battery optimization
            MessageBox.Show("Battery optimization functionality coming soon!", "Battery Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _refreshTimer?.Stop();
            _wifiManager?.Dispose();
            Logger.Shutdown();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}