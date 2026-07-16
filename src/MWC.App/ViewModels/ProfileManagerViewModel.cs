using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.ViewModels;

public sealed partial class ProfileManagerViewModel : ObservableObject
{
    private readonly IWifiService          _wifi;
    private readonly NetworkHistoryService _history;
    private readonly ILogger               _log;
    private Guid _adapterId;

    public ObservableCollection<ProfileItem> Profiles { get; } = new();

    [ObservableProperty] private ProfileItem? _selected;
    [ObservableProperty] private bool         _isBusy;
    [ObservableProperty] private string       _statusMessage = "";

    public ProfileManagerViewModel(IWifiService wifi, NetworkHistoryService history, ILogger<ProfileManagerViewModel> log)
    {
        _wifi    = wifi;
        _history = history;
        _log     = log;
    }

    public async Task LoadAsync(Guid adapterId)
    {
        if (IsBusy) return;
        _adapterId = adapterId;
        IsBusy = true;
        try
        {
            var names = await _wifi.ListProfilesAsync(adapterId);
            Profiles.Clear();
            foreach (var n in names.OrderBy(x => x))
                Profiles.Add(new ProfileItem(n));
            StatusMessage = MWC.App.Resources.L.StatusProfileCount(Profiles.Count);
        }
        catch (Exception ex)
        {
            // このクラスは元々 ILogger を持たず、例外は無音で握りつぶされていた
            // (2026-07 品質パスで是正。AdapterViewModel.RefreshAsync と同じ問題)。
            _log.LogError(ex, "ProfileManagerViewModel.LoadAsync failed for adapter {AdapterId}", adapterId);
            StatusMessage = MWC.App.Resources.L.ErrorUnexpected(ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (Selected is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var ssid = Selected.Name;
            bool ok = await _wifi.DeleteProfileAsync(_adapterId, ssid);
            if (ok)
            {
                _history.Forget(ssid);
                Profiles.Remove(Selected);
                StatusMessage = MWC.App.Resources.L.StatusDeleted(ssid);
            }
            else
            {
                StatusMessage = MWC.App.Resources.L.StatusDeleteFailed(ssid);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ProfileManagerViewModel.DeleteAsync failed for adapter {AdapterId}", _adapterId);
            StatusMessage = MWC.App.Resources.L.ErrorUnexpected(ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync(_adapterId);
}

public sealed class ProfileItem(string name)
{
    public string Name { get; } = name;
    public string Icon { get; } = "📶";
}
