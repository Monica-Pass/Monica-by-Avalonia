using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class GeneratorWorkspaceViewModel : ObservableObject
{
    private readonly IPasswordGeneratorService _passwordGenerator;
    private readonly IClipboardService _clipboardService;
    private readonly ILocalizationService _localization;
    private readonly Action<string> _showNotice;

    public GeneratorWorkspaceViewModel(
        IPasswordGeneratorService passwordGenerator,
        IClipboardService clipboardService,
        ILocalizationService localization,
        Action<string> showNotice,
        IAsyncRelayCommand addPasswordCommand)
    {
        _passwordGenerator = passwordGenerator;
        _clipboardService = clipboardService;
        _localization = localization;
        _showNotice = showNotice;
        AddPasswordCommand = addPasswordCommand;
        RefreshLocalization();
    }

    public ILocalizationService L => _localization;
    public IAsyncRelayCommand AddPasswordCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeneratorResultPanelPadding))]
    [NotifyPropertyChangedFor(nameof(GeneratorOptionsPanelPadding))]
    [NotifyPropertyChangedFor(nameof(GeneratorOptionsSpacing))]
    [NotifyPropertyChangedFor(nameof(GeneratorCheckboxSpacing))]
    [NotifyPropertyChangedFor(nameof(GeneratorPasswordBoxMinHeight))]
    [NotifyPropertyChangedFor(nameof(GeneratorHistoryPanelMaxHeight))]
    [NotifyPropertyChangedFor(nameof(ShowGeneratorStrengthSummaryCard))]
    private bool _isCompact;

    public void ClearSensitiveState()
    {
        GeneratedPassword = "";
        ClearGeneratedPasswordHistorySecrets();
    }
}
