using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed record TimelineEntry(string Title, string Description, string TimestampText, string OperationType, string ItemType);

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<TimelineEntry> _filteredTimelineEntries = [];

    private static readonly PlatformFilePickerFileType[] TimelineTsvFileTypes =
    [
        new("Timeline TSV", ["*.tsv"])
    ];

    public ObservableCollection<TimelineEntry> TimelineEntries { get; } = new ObservableRangeCollection<TimelineEntry>();

    [ObservableProperty]
    private string _exportTimelinePreview = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTimelineEntry))]
    private TimelineEntry? _selectedTimelineEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimelineSearchText))]
    [NotifyPropertyChangedFor(nameof(ShowClearTimelineSearchInEmptyState))]
    private string _timelineSearchText = "";

    [ObservableProperty]
    private bool _timelineNarrowShowsList = true;

    public string TimelineCountText => _localization.Format("TimelineCountFormat", TimelineEntries.Count);
    public bool HasSelectedTimelineEntry => SelectedTimelineEntry is not null;
    public bool HasTimelineEntries => TimelineEntries.Count > 0;
    public bool HasTimelineSearchText => !string.IsNullOrWhiteSpace(TimelineSearchText);
    public IReadOnlyList<TimelineEntry> FilteredTimelineEntries => _filteredTimelineEntries;
    public bool HasFilteredTimelineEntries => FilteredTimelineEntries.Count > 0;
    public bool ShowClearTimelineSearchInEmptyState =>
        HasTimelineEntries && HasTimelineSearchText && !HasFilteredTimelineEntries;
    public string TimelineEmptyStateText => ShowClearTimelineSearchInEmptyState
        ? _localization.Format("TimelineNoSearchResultsFormat", TimelineSearchText.Trim())
        : _localization.Get("TimelineEmptyHint");

    partial void OnTimelineSearchTextChanged(string value) => RefreshTimelineSearchState();

    [RelayCommand]
    private void ClearTimelineSearch() => TimelineSearchText = "";

    [RelayCommand]
    private void CloseTimelineEntryDetails() => TimelineNarrowShowsList = true;

    [RelayCommand]
    private void ShowTimelineEntryDetails(TimelineEntry? entry)
    {
        if (entry is not null)
        {
            SelectedTimelineEntry = entry;
            TimelineNarrowShowsList = false;
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
        RefreshTimelineSearchState();
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
        RefreshTimelineSearchState();
    }

    private bool MatchesTimelineSearch(TimelineEntry entry)
    {
        var query = TimelineSearchText.Trim();
        return query.Length == 0 ||
            entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            entry.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            entry.OperationType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.ItemType.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshTimelineSearchState()
    {
        _filteredTimelineEntries = TimelineEntries.Where(MatchesTimelineSearch).ToArray();
        OnPropertyChanged(nameof(FilteredTimelineEntries));
        OnPropertyChanged(nameof(HasFilteredTimelineEntries));
        OnPropertyChanged(nameof(ShowClearTimelineSearchInEmptyState));
        OnPropertyChanged(nameof(TimelineEmptyStateText));
        SelectedTimelineEntry =
            FilteredTimelineEntries.FirstOrDefault(item => ReferenceEquals(item, SelectedTimelineEntry)) ??
            FilteredTimelineEntries.FirstOrDefault();
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
        if (TimelineEntries.Count == 0)
        {
            StatusMessage = _localization.Get("TimelineExportEmpty");
            return;
        }

        if (!await AuthorizeSensitiveExportAsync())
        {
            return;
        }

        var lines = new List<string>
        {
            $"{_localization.Get("Title")}\t{_localization.Get("Description")}\t{_localization.Get("Timestamp")}\t{_localization.Get("OperationType")}\t{_localization.Get("ItemType")}"
        };

        foreach (var entry in TimelineEntries)
        {
            lines.Add(string.Join('\t',
                EscapeTimelineCell(entry.Title),
                EscapeTimelineCell(entry.Description),
                EscapeTimelineCell(entry.TimestampText),
                EscapeTimelineCell(entry.OperationType),
                EscapeTimelineCell(entry.ItemType)));
        }

        ExportTimelinePreview = string.Join(Environment.NewLine, lines);
        StatusMessage = _localization.Format("ExportedTimelineFormat", TimelineEntries.Count);
        await Task.CompletedTask;
    }

    private static string EscapeTimelineCell(string value)
    {
        var sanitized = value
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return sanitized.Length > 0 && sanitized[0] is '=' or '+' or '-' or '@'
            ? $"'{sanitized}"
            : sanitized;
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task SaveTimelineExportAsync()
    {
        if (TimelineEntries.Count == 0)
        {
            StatusMessage = _localization.Get("TimelineExportEmpty");
            return;
        }

        await ExportTimelineAsync();
        if (string.IsNullOrWhiteSpace(ExportTimelinePreview))
        {
            return;
        }

        await SaveExportTextAsync(
            _localization.Get("Timeline"),
            $"monica_timeline_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.tsv",
            ExportTimelinePreview,
            TimelineTsvFileTypes);
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
