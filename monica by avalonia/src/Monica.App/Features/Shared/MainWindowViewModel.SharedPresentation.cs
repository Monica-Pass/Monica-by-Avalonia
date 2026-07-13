using System.Collections.ObjectModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{

    private string FormatLocalDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture);


    private string LocalizeSyncStatus(SyncStatus status)
    {
        return status switch
        {
            SyncStatus.Synced => _localization.Get("Synced"),
            SyncStatus.Syncing => _localization.Get("Syncing"),
            SyncStatus.Pending => _localization.Get("Pending"),
            SyncStatus.PendingUpload => _localization.Get("PendingUpload"),
            SyncStatus.InSync => _localization.Get("Synced"),
            SyncStatus.RemoteChanged => _localization.Get("RemoteChanged"),
            SyncStatus.LocalOnly => _localization.Get("LocalOnly"),
            SyncStatus.Conflict => _localization.Get("Conflict"),
            SyncStatus.Failed => _localization.Get("Failed"),
            _ => _localization.Get("None")
        };
    }
}
