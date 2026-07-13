using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed record TimelineEntry(string Title, string Description, string TimestampText, string OperationType, string ItemType);

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<TimelineEntry> TimelineEntries { get; } = new ObservableRangeCollection<TimelineEntry>();

    [ObservableProperty]
    private string _exportTimelinePreview = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTimelineEntry))]
    private TimelineEntry? _selectedTimelineEntry;

    public string TimelineCountText => _localization.Format("TimelineCountFormat", TimelineEntries.Count);
    public bool HasSelectedTimelineEntry => SelectedTimelineEntry is not null;
    public bool HasTimelineEntries => TimelineEntries.Count > 0;

    [RelayCommand]
    private void ShowTimelineEntryDetails(TimelineEntry? entry)
    {
        if (entry is not null)
        {
            SelectedTimelineEntry = entry;
        }
    }

    private async Task LoadTimelineAsync()
    {
        var logs = await AppDiagnostics.MeasureAsync(
            "Load timeline",
            () => _repository.GetOperationLogsAsync(150));
        ApplyTimelineLogs(logs);
    }

    private async Task LogOperationAsync(OperationLog log, CancellationToken cancellationToken = default)
    {
        await _repository.LogAsync(log, cancellationToken);

        var entry = CreateTimelineEntry(log);
        TimelineEntries.Insert(0, entry);
        while (TimelineEntries.Count > 150)
        {
            TimelineEntries.RemoveAt(TimelineEntries.Count - 1);
        }

        SelectedTimelineEntry ??= entry;
        OnPropertyChanged(nameof(TimelineCountText));
        OnPropertyChanged(nameof(HasTimelineEntries));
    }

    private async Task LoadTimelineDeferredAsync()
    {
        try
        {
            await LoadTimelineAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Deferred timeline load failed", ex);
        }
    }

    private void ApplyTimelineLogs(IReadOnlyList<OperationLog> logs)
    {
        var selectedStamp = SelectedTimelineEntry?.TimestampText;
        var selectedTitle = SelectedTimelineEntry?.Title;
        var entries = logs
            .Select(CreateTimelineEntry)
            .ToArray();
        ReplaceItems(TimelineEntries, entries);
        SelectedTimelineEntry =
            TimelineEntries.FirstOrDefault(item =>
                string.Equals(item.TimestampText, selectedStamp, StringComparison.Ordinal) &&
                string.Equals(item.Title, selectedTitle, StringComparison.Ordinal)) ??
            TimelineEntries.FirstOrDefault();

        OnPropertyChanged(nameof(TimelineCountText));
        OnPropertyChanged(nameof(HasTimelineEntries));
    }

    private TimelineEntry CreateTimelineEntry(OperationLog log) =>
        new(
            string.IsNullOrWhiteSpace(log.ItemTitle) ? _localization.Get("Untitled") : log.ItemTitle,
            _localization.Format(
                "TimelineEntryDescriptionFormat",
                LocalizeOperationType(log.OperationType),
                log.ItemType,
                log.DeviceName),
            log.Timestamp.LocalDateTime.ToString("g", _localization.Culture),
            log.OperationType,
            log.ItemType);

    [RelayCommand]
    private async Task ExportTimelineAsync()
    {
        if (!await AuthorizeSensitiveExportAsync())
        {
            return;
        }

        if (TimelineEntries.Count == 0)
        {
            StatusMessage = _localization.Get("TimelineExportEmpty");
            return;
        }

        var lines = new List<string>
        {
            $"{_localization.Get("Title")}\t{_localization.Get("Description")}\t{_localization.Get("Timestamp")}\t{_localization.Get("OperationType")}\t{_localization.Get("ItemType")}"
        };

        foreach (var entry in TimelineEntries)
        {
            lines.Add($"{entry.Title}\t{entry.Description}\t{entry.TimestampText}\t{entry.OperationType}\t{entry.ItemType}");
        }

        ExportTimelinePreview = string.Join(Environment.NewLine, lines);
        StatusMessage = _localization.Format("ExportedTimelineFormat", TimelineEntries.Count);
        await Task.CompletedTask;
    }

    private string LocalizeOperationType(string operationType)
    {
        return operationType.ToUpperInvariant() switch
        {
            "CREATE" => _localization.Get("OperationCreate"),
            "UPDATE" => _localization.Get("OperationUpdate"),
            "DELETE" => _localization.Get("OperationDelete"),
            "RESTORE" => _localization.Get("OperationRestore"),
            "PURGE" => _localization.Get("OperationPurge"),
            "FAVORITE" => _localization.Get("OperationFavorite"),
            "MOVE_CATEGORY" => _localization.Get("OperationMoveCategory"),
            "STACK" => _localization.Get("OperationStack"),
            "ATTACHMENT" => _localization.Get("OperationAttachment"),
            "ARCHIVE" => _localization.Get("OperationArchive"),
            "UNARCHIVE" => _localization.Get("OperationUnarchive"),
            "IMPORT" => _localization.Get("OperationImport"),
            _ => operationType
        };
    }
}
