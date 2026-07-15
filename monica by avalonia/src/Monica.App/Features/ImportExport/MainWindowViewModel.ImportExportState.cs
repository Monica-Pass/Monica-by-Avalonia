using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed record MonicaJsonImportResult(int Passwords, int SecureItems, int Categories);

public sealed partial class MainWindowViewModel
{
    private static readonly PlatformFilePickerFileType[] MonicaJsonFileTypes =
    [
        new("Monica JSON", ["*.json"])
    ];
    private static readonly PlatformFilePickerFileType[] PasswordCsvFileTypes =
    [
        new("Password CSV", ["*.csv"])
    ];
    private static readonly PlatformFilePickerFileType[] NoteCsvFileTypes =
    [
        new("Notes CSV", ["*.csv"])
    ];

    private readonly IImportExportService _importExportService;
    private int _importExportOperationActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportExportIdle))]
    [NotifyPropertyChangedFor(nameof(IsImportWorkspaceIdle))]
    private bool _isImportExportBusy;

    public bool IsImportExportIdle => !IsImportExportBusy;
    public bool IsImportWorkspaceIdle => !IsImportExportBusy && !IsKeePassImportBusy;

    [ObservableProperty]
    private string _exportPreview = "";

    [ObservableProperty]
    private string _importJsonText = "";

    [ObservableProperty]
    private string _importNoteCsvText = "";

    [ObservableProperty]
    private string _exportCsvPreview = "";

    [ObservableProperty]
    private string _exportNoteCsvPreview = "";

    [ObservableProperty]
    private string _importCsvText = "";

    private async Task RunImportExportOperationAsync(Func<Task> operation)
    {
        if (Interlocked.CompareExchange(ref _importExportOperationActive, 1, 0) != 0)
        {
            return;
        }

        IsImportExportBusy = true;
        try
        {
            await operation();
        }
        finally
        {
            IsImportExportBusy = false;
            Interlocked.Exchange(ref _importExportOperationActive, 0);
        }
    }
}
