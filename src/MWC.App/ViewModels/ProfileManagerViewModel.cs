using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MWC.Core.Abstractions;
using MWC.Core.Models;

namespace MWC.App.ViewModels;

public sealed partial class ProfileManagerViewModel : ObservableObject
{
    private readonly IWifiService _wifi;
    private Guid _adapterId;

    public ObservableCollection<ProfileItem> Profiles { get; } = new();

    [ObservableProperty] private ProfileItem? _selected;
    [ObservableProperty] private bool         _isBusy;
    [ObservableProperty] private string       _statusMessage = "";

    public ProfileManagerViewModel(IWifiService wifi) => _wifi = wifi;

    public async Task LoadAsync(Guid adapterId)
    {
        _adapterId = adapterId;
        IsBusy = true;
        try
        {
            var names = await _wifi.ListProfilesAsync(adapterId);
            Profiles.Clear();
            foreach (var n in names.OrderBy(x => x))
                Profiles.Add(new ProfileItem(n));
            StatusMessage = $"{Profiles.Count} 件のプロファイル";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (Selected is null) return;
        var ssid = Selected.Name;
        bool ok = await _wifi.DeleteProfileAsync(_adapterId, ssid);
        if (ok)
        {
            Profiles.Remove(Selected);
            StatusMessage = MWC.App.Resources.L.StatusDeleted(ssid);
        }
        else
        {
            StatusMessage = MWC.App.Resources.L.StatusDeleteFailed(ssid);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync(_adapterId);
}

public sealed class ProfileItem(string name)
{
    public string Name { get; } = name;
    public string Icon { get; } = "📶";
}
