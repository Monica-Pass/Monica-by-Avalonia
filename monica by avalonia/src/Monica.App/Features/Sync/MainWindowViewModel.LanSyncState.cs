using Monica.App.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RefreshDeviceSyncChoices()
    {
        _applyingDeviceSyncState = true;
        try
        {
            DeviceSyncFrequencies.Clear();
            DeviceSyncModes.Clear();
            DeviceSyncModes.Add(new(DeviceSyncMode.Bidirectional, L.Get("DeviceSyncBidirectional")));
            DeviceSyncModes.Add(new(DeviceSyncMode.SendOnly, L.Get("DeviceSyncSendOnly")));
            DeviceSyncModes.Add(new(DeviceSyncMode.ReceiveOnly, L.Get("DeviceSyncReceiveOnly")));
            SelectedDeviceSyncMode = DeviceSyncModes.First(item => item.Mode == (_deviceSync?.State.Mode ?? DeviceSyncMode.Bidirectional));
            foreach (var seconds in new[] { 0, 60, 300, 900, 1800, -1 })
                DeviceSyncFrequencies.Add(new(seconds, seconds == 0 ? L.Get("DeviceSyncRealtime") :
                    seconds == -1 ? L.Get("DeviceSyncCustom") : L.Format("DeviceSyncEveryMinutes", seconds / 60)));
            var current = _deviceSync?.State.IntervalSeconds ?? 0;
            _displayedDeviceSyncInterval = current;
            SelectedDeviceSyncFrequency = DeviceSyncFrequencies.FirstOrDefault(item => item.Seconds == current) ?? DeviceSyncFrequencies.Last();
            IsCustomDeviceSyncInterval = SelectedDeviceSyncFrequency.Seconds == -1;
        }
        finally { _applyingDeviceSyncState = false; }
    }

    internal void ApplyDeviceSyncState(DeviceSyncState state)
    {
        _applyingDeviceSyncState = true;
        try
        {
            DeviceSyncEnabled = state.Enabled;
            IsDeviceSyncPairing = state.Pairing;
            SelectedDeviceSyncMode = DeviceSyncModes.First(item => item.Mode == state.Mode);
            if (_displayedDeviceSyncInterval != state.IntervalSeconds || SelectedDeviceSyncFrequency is null)
            {
                _displayedDeviceSyncInterval = state.IntervalSeconds;
                SelectedDeviceSyncFrequency = DeviceSyncFrequencies.FirstOrDefault(item => item.Seconds == state.IntervalSeconds) ?? DeviceSyncFrequencies.LastOrDefault();
                IsCustomDeviceSyncInterval = SelectedDeviceSyncFrequency?.Seconds == -1;
                if (IsCustomDeviceSyncInterval) DeviceSyncCustomMinutes = (state.IntervalSeconds / 60).ToString();
            }
            foreach (var removed in SyncedDevices.Where(item => state.Peers.All(peer => peer.DeviceId != item.DeviceId)).ToArray()) SyncedDevices.Remove(removed);
            foreach (var peer in state.Peers)
            {
                var display = SyncedDevices.FirstOrDefault(item => item.DeviceId == peer.DeviceId);
                if (display is null) { display = new(peer.DeviceId); SyncedDevices.Add(display); }
                display.Name = peer.Name;
                display.Status = L.Get(peer.Status switch
                {
                    "syncing" => "DeviceSyncSyncing",
                    "online" => "DeviceSyncOnline",
                    "scheduled" => "DeviceSyncScheduled",
                    "paused" => "DeviceSyncPaused",
                    "locked" => "DeviceSyncLocked",
                    "waiting" => "DeviceSyncWaiting",
                    "first_sync_pending" => "DeviceSyncFirstPending",
                    "preparation_failed" => "DeviceSyncPreparationFailed",
                    _ => "DeviceSyncOffline"
                });
                display.LastSynced = peer.LastSyncedAt is { } time ? L.Format("DeviceSyncLast", time.ToLocalTime().ToString("g")) : "";
            }
            IsLanBusy = state.Pairing || state.Phase is "preparing" or "sending" or "receiving" or "transferring" or "applying" or "authenticating" or "connecting" or "checking" or "confirming";
            var progress = state.Progress;
            var total = progress?.TotalBytes > 0 ? progress.TotalBytes : progress?.Total ?? 0;
            var completed = progress?.TotalBytes > 0 ? progress.Bytes : progress?.Completed ?? 0;
            LanProgressIndeterminate = total <= 0;
            LanProgress = total > 0 ? Math.Clamp(100.0 * completed / total, 0, 100) : 0;
            LanHostCode = IsUnlocked ? state.Code : "";
            var links = IsUnlocked ? state.Invitations ?? [] : [];
            if (!LanHostLinks.SequenceEqual(links))
            {
                ReplaceItems(LanHostLinks, links);
                LanSelectedHostLink = links.FirstOrDefault() ?? "";
            }
            LanStatusText = L.Get(state.Phase switch
            {
                "waiting" when state.Pairing => "DeviceSyncScanOther",
                "completed" => "DeviceSyncPaired",
                "locked" => "DeviceSyncLocked",
                "paused" => "DeviceSyncPaused",
                "idle" => "",
                "failed" => "",
                "cancelled" => "LanCancelled",
                _ => "DeviceSyncSyncing"
            });
            if (IsLanBusy && progress is not null)
                LanStatusText = L.Get(progress.Stage switch
                {
                    "preparing" => "DeviceSyncPreparing",
                    "applying" => "DeviceSyncApplying",
                    "sending" => "DeviceSyncSending",
                    "receiving" => "DeviceSyncReceiving",
                    "transferring" => "DeviceSyncTransferring",
                    _ => "DeviceSyncSyncing"
                }) +
                    (total <= 0 ? "" : progress.TotalBytes > 0 ? $" · {completed / 1048576.0:F1} / {total / 1048576.0:F1} MB" : $" · {completed:N0} / {total:N0}");
            LanErrorText = state.Error is null ? "" : DeviceSyncError(state.Error, state.FailureStage);
            LanDiagnosticText = state.Error is null ? "" : L.Format("DeviceSyncDiagnosticFormat", state.FailureStage, state.Error,
                state.FailureKind, state.ConnectionAttempts, string.IsNullOrEmpty(state.FailureOperation) ? "-" : state.FailureOperation,
                state.FailureRequestBytes);
            if (state.Error is not null) LanDiagnosticText = L.Format("DeviceSyncDirectionDiagnostic", state.Mode.ToWire()) + Environment.NewLine + LanDiagnosticText;
            if (state.Phase is "locked" or "completed") { LanInvitation = ""; LanPairingCode = ""; }
        }
        finally { _applyingDeviceSyncState = false; }
    }

    private string DeviceSyncError(string error, string stage = "") => L.Get(error switch
    {
        "connection_failed" or "listen_failed" or "discovery_unavailable" => "DeviceSyncConnectionFailed",
        "pairing_rejected" or "authentication_failed" or "different_device" => "DeviceSyncPairingFailed",
        "peer_merge_failed" => "LanPeerFailed",
        "native_unavailable" or "device_sync_unavailable" => "LanUnavailable",
        "unsupported_protocol" or "invalid_invitation" => "DeviceSyncQrInvalid",
        "pairing_expired" => "LanExpired",
        "same_device" => "DeviceSyncSameDevice",
        "too_many_devices" => "DeviceSyncLimit",
        "invalid_password_payload" or "invalid_record" => "DeviceSyncInvalidData",
        "sync_direction_conflict" => "DeviceSyncDirectionConflict",
        "sync_direction_unsupported" => "DeviceSyncDirectionUnsupported",
        "request_too_large" => "DeviceSyncRequestTooLarge",
        _ when error.StartsWith("storage_", StringComparison.Ordinal) => "DeviceSyncStorageFailed",
        _ when stage == "preparing" => "DeviceSyncPrepareFailed",
        _ when stage is "applying" or "received" or "remembering" => "DeviceSyncApplyFailed",
        _ => "DeviceSyncFailed"
    });
}
