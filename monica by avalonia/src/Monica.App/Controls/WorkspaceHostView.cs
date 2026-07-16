using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Monica.App.Features.Archive;
using Monica.App.Features.Authenticator;
using Monica.App.Features.DatabaseManagement;
using Monica.App.Features.Generator;
using Monica.App.Features.Mdbx;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
using Monica.App.Features.RecycleBin;
using Monica.App.Features.SecurityAnalysis;
using Monica.App.Features.Settings;
using Monica.App.Features.Sync;
using Monica.App.Features.Timeline;
using Monica.App.Features.Wallet;

namespace Monica.App.Controls;

public sealed class WorkspaceHostView : ContentControl
{
    private static readonly IReadOnlyDictionary<string, Func<Control>> WorkspaceFactories =
        new Dictionary<string, Func<Control>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Passwords"] = static () => new PasswordVaultView(),
            ["Notes"] = static () => new NoteWorkspaceView(),
            ["Totp"] = static () => new AuthenticatorWorkspaceView(),
            ["Cards"] = static () => new WalletWorkspaceView(),
            ["Generator"] = static () => new GeneratorWorkspaceView(),
            ["Archive"] = static () => new ArchiveWorkspaceView(),
            ["RecycleBin"] = static () => new RecycleBinWorkspaceView(),
            ["SecurityAnalysis"] = static () => new SecurityAnalysisWorkspaceView(),
            ["Timeline"] = static () => new TimelineWorkspaceView(),
            ["Mdbx"] = static () => new MdbxWorkspaceView(),
            ["DatabaseManagement"] = static () => new DatabaseManagementWorkspaceView(),
            ["Sync"] = static () => new SyncWorkspaceView(),
            ["Settings"] = static () => new SettingsWorkspaceView()
        };

    public static readonly StyledProperty<string?> SectionProperty =
        AvaloniaProperty.Register<WorkspaceHostView, string?>(nameof(Section));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<WorkspaceHostView, bool>(nameof(IsActive));

    private readonly Dictionary<string, Control> _workspaces =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingSections =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _createdSections = [];

    public string? Section
    {
        get => GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Control? CurrentWorkspace => Content as Control;

    public IReadOnlyList<string> CreatedSections => _createdSections;

    public TWorkspace GetOrCreate<TWorkspace>(string section)
        where TWorkspace : Control
    {
        var workspace = GetOrCreate(section);
        if (workspace is TWorkspace typedWorkspace)
        {
            return typedWorkspace;
        }

        throw new InvalidOperationException(
            $"Workspace '{section}' is {workspace.GetType().Name}, not {typeof(TWorkspace).Name}.");
    }

    public bool TryGet<TWorkspace>(string section, out TWorkspace workspace)
        where TWorkspace : Control
    {
        if (_workspaces.TryGetValue(section, out var cached) && cached is TWorkspace typedWorkspace)
        {
            workspace = typedWorkspace;
            return true;
        }

        workspace = null!;
        return false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SectionProperty || change.Property == IsActiveProperty)
        {
            UpdateWorkspace();
        }
    }

    private Control GetOrCreate(string section)
    {
        if (_workspaces.TryGetValue(section, out var workspace))
        {
            return workspace;
        }

        if (!WorkspaceFactories.TryGetValue(section, out var factory))
        {
            throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown workspace section.");
        }

        _pendingSections.Remove(section);
        workspace = factory();
        _workspaces.Add(section, workspace);
        if (!_createdSections.Contains(section, StringComparer.OrdinalIgnoreCase))
        {
            _createdSections.Add(section);
        }

        return workspace;
    }

    private void UpdateWorkspace()
    {
        if (!IsActive)
        {
            Content = null;
            foreach (var cachedWorkspace in _workspaces.Values)
            {
                cachedWorkspace.DataContext = null;
            }

            _workspaces.Clear();
            _pendingSections.Clear();
            _createdSections.Clear();
            return;
        }

        if (string.IsNullOrWhiteSpace(Section))
        {
            Content = null;
            return;
        }

        if (_workspaces.TryGetValue(Section, out var workspace))
        {
            Content = workspace;
            return;
        }

        Content = CreateLoadingPlaceholder();
        if (_pendingSections.Add(Section))
        {
            var section = Section;
            Dispatcher.UIThread.Post(
                () => MaterializeWorkspace(section),
                DispatcherPriority.ContextIdle);
        }
    }

    private void MaterializeWorkspace(string section)
    {
        if (!_pendingSections.Remove(section) ||
            !IsActive ||
            !string.Equals(Section, section, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var materializedWorkspace = GetOrCreate(section);
        Content = materializedWorkspace;
    }

    private static Control CreateLoadingPlaceholder() =>
        new Border
        {
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 10,
                Children =
                {
                    new ProgressBar
                    {
                        Width = 220,
                        Height = 4,
                        IsIndeterminate = true
                    }
                }
            }
        };
}
