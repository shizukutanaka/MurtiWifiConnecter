using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MWC.Core.Abstractions;
using MWC.Core.Models;

namespace MWC.App.Views;

public partial class ConnectionProgressDialog : Window
{
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken CancellationToken => _cts.Token;
    public ConnectionResult? Result { get; private set; }

    private readonly ObservableCollection<StepItem> _steps = new()
    {
        new StepItem(MWC.App.Resources.L.Get("Step_Connect")),
        new StepItem(MWC.App.Resources.L.Get("Step_Auth")),
        new StepItem(MWC.App.Resources.L.StepIpAddress),
        new StepItem(MWC.App.Resources.L.Get("Step_Internet"))
    };

    public ConnectionProgressDialog(string ssid)
    {
        InitializeComponent();
        SsidLabel.Text    = ssid;
        StepLabel.Text    = MWC.App.Resources.L.Get("Step_Preparing");
        StepsControl.ItemsSource = _steps;
    }

    public void SetStep(int index, StepState state, string? statusText = null)
    {
        Dispatcher.Invoke(() =>
        {
            if (index >= 0 && index < _steps.Count)
                _steps[index].State = state;
            if (statusText is not null)
                StepLabel.Text = statusText;
        });
    }

    public void SetResult(ConnectionResult result, string message)
    {
        Result = result;
        Dispatcher.Invoke(() =>
        {
            StepLabel.Text = message;
            CancelBtn.Content = MWC.App.Resources.L.Get("Action_Close");
            CancelBtn.IsEnabled = true;
        });
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (Result is null) _cts.Cancel();
        DialogResult = Result?.Success ?? false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnClosed(e);
    }
}

public sealed class StepItem : System.ComponentModel.INotifyPropertyChanged
{
    private StepState _state = StepState.Pending;

    public string Name { get; }
    public StepState State
    {
        get => _state;
        set { _state = value; PropertyChanged?.Invoke(this, new(nameof(State))); }
    }

    public StepItem(string name) { Name = name; }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public enum StepState { Pending, Active, Done, Error }
