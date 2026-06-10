using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MWC.App.Resources;
using MWC.App.ViewModels;
using MWC.Core.Services;

namespace MWC.App.Views;

public partial class AdapterPreferencesDialog : Window
{
    private readonly AdapterViewModel _adapter;
    private readonly IReadOnlyList<AdapterViewModel> _allAdapters;
    private readonly ObservableCollection<string> _pinned = new();

    // Item in the failover adapter ComboBox
    private sealed record FailoverItem(string Name, Guid? Id)
    {
        public override string ToString() => Name;
    }

    public AdapterPreferencesDialog(
        AdapterViewModel adapter,
        IReadOnlyList<AdapterViewModel>? allAdapters = null)
    {
        InitializeComponent();
        _adapter = adapter;
        _allAdapters = allAdapters ?? Array.Empty<AdapterViewModel>();
        AdapterNameLabel.Text = adapter.Name;
        AdapterDescLabel.Text = adapter.Id.ToString();

        // 表示名
        LabelBox.Text = adapter.Preferences.CustomLabel ?? "";

        // バンド選択
        switch (adapter.PreferredBand)
        {
            case BandPreference.Only2_4GHz: Band24.IsChecked = true; break;
            case BandPreference.Only5GHz:   Band5.IsChecked  = true; break;
            case BandPreference.Only6GHz:   Band6.IsChecked  = true; break;
            default:                        BandAny.IsChecked = true; break;
        }

        // 有効化
        EnabledCheck.IsChecked = adapter.IsEnabled;

        // ピン留めSSID
        foreach (var ssid in adapter.PinnedSsids) _pinned.Add(ssid);
        PinnedList.ItemsSource = _pinned;
        EmptyHint.Visibility = _pinned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // フェイルオーバー
        var items = new List<FailoverItem> { new(L.FailoverBackupNone, null) };
        items.AddRange(_allAdapters
            .Where(a => a.Id != adapter.Id)
            .Select(a => new FailoverItem(a.Name, a.Id)));
        FailoverAdapterCombo.ItemsSource = items;

        var pref = adapter.Preferences;
        FailoverCheck.IsChecked = pref.EnableFailover;
        FailoverAdapterPanel.IsEnabled = pref.EnableFailover;
        FailoverAdapterCombo.SelectedItem = pref.FailoverAdapterId.HasValue
            ? items.FirstOrDefault(i => i.Id == pref.FailoverAdapterId)
            : items[0];

        FailoverCheck.Checked   += (_, _) => FailoverAdapterPanel.IsEnabled = true;
        FailoverCheck.Unchecked += (_, _) => FailoverAdapterPanel.IsEnabled = false;
    }

    private void OnUnpin(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string ssid)
        {
            _pinned.Remove(ssid);
            EmptyHint.Visibility = _pinned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // 表示名
        var label = string.IsNullOrWhiteSpace(LabelBox.Text) ? null : LabelBox.Text.Trim();
        _adapter.SetCustomLabel(label);

        // バンド
        var band = BandAny.IsChecked == true ? BandPreference.Any
                 : Band24.IsChecked  == true ? BandPreference.Only2_4GHz
                 : Band5.IsChecked   == true ? BandPreference.Only5GHz
                 : Band6.IsChecked   == true ? BandPreference.Only6GHz
                 : BandPreference.Any;
        _adapter.SetPreferredBand(band);

        // 有効化トグル(変更があった場合のみ)
        if (_adapter.IsEnabled != (EnabledCheck.IsChecked ?? true))
            _adapter.ToggleEnabled();

        // ピン留めSSID(変更を反映)
        var prefs = _adapter.PrefsService;
        var current = _adapter.PinnedSsids.ToHashSet();
        foreach (var keep in _pinned.Where(p => !current.Contains(p)))
            prefs.PinSsid(_adapter.Id, keep);
        foreach (var rm in current.Where(c => !_pinned.Contains(c)))
            prefs.UnpinSsid(_adapter.Id, rm);

        // フェイルオーバー
        var failoverEnabled = FailoverCheck.IsChecked ?? false;
        var failoverItem    = FailoverAdapterCombo.SelectedItem as FailoverItem;
        prefs.SetFailover(_adapter.Id, failoverItem?.Id, failoverEnabled);

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
