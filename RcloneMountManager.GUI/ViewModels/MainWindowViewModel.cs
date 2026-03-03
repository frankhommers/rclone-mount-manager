using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;
using RcloneMountManager.Services;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan RuntimeRefreshCadence = TimeSpan.FromSeconds(3);
    private const int MaxProfileLogEntries = 250;
    private readonly MountManagerService _mountManagerService;
    private readonly LaunchAgentService _launchAgentService;
    private readonly RcloneBackendService _rcloneBackendService;
    private readonly RcloneConfigWizardService _rcloneConfigWizardService;
    private readonly StartupPreflightService _startupPreflightService;
    private readonly MountHealthService _mountHealthService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Func<MountProfile, CancellationToken, Task<StartupPreflightReport>> _startupPreflightRunner;
    private readonly Func<MountProfile, CancellationToken, Task> _mountStartRunner;
    private readonly Func<MountProfile, CancellationToken, Task> _mountStopRunner;
    private readonly Func<MountProfile, CancellationToken, Task<bool>> _mountedProbe;
    private readonly Func<MountProfile, CancellationToken, Task<ProfileRuntimeState>> _runtimeStateVerifier;
    private readonly Func<MountProfile, string, CancellationToken, Task> _startupEnableRunner;
    private readonly Func<MountProfile, CancellationToken, Task> _startupDisableRunner;
    private readonly Func<MountProfile, bool> _startupEnabledProbe;
    private readonly Func<TimeSpan, CancellationToken, Task<bool>> _runtimeRefreshWaiter;
    private readonly Func<IEnumerable<MountProfile>, CancellationToken, Task<IReadOnlyList<ProfileRuntimeState>>> _runtimeStateBatchVerifier;
    private readonly Func<MountProfile, CancellationToken, Task>? _testConnectionRunner;
    private readonly bool _isStartupSupported;
    public MountOptionsViewModel MountOptionsVm { get; } = new();
    private readonly string _profilesFilePath;
    private readonly Dictionary<string, List<ProfileLogEvent>> _profileLogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _profileScripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StartupPreflightReport> _profileStartupPreflightReports = new(StringComparer.OrdinalIgnoreCase);
    private MountProfile? _observedProfile;
    private bool _isLoadingProfiles;
    private readonly object _runtimeMonitoringGate = new();
    private CancellationTokenSource? _runtimeMonitoringCts;
    private Task? _runtimeMonitoringTask;
    private bool _runtimeMonitoringActive;
    private bool _syncingSidebarSelection;
    private bool _syncingMountRemoteSelection;
    private bool _syncingRemoteNameInput;
    private MountProfile? _rememberedRemoteProfile;
    private MountProfile? _rememberedMountProfile;

    [ObservableProperty]
    private MountProfile _selectedProfile = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _generatedScript = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _selectedThemeMode = "Follow system";

    [ObservableProperty]
    private RcloneBackendInfo? _selectedBackend;

    [ObservableProperty]
    private string _newRemoteName = string.Empty;

    [ObservableProperty]
    private bool _isWizardActive;

    [ObservableProperty]
    private ConfigWizardStep? _currentWizardStep;

    [ObservableProperty]
    private string _wizardAnswer = string.Empty;

    [ObservableProperty]
    private WizardStepOptionInput? _wizardStepInput;

    [ObservableProperty]
    private bool _isWizardWaitingForOAuth;

    [ObservableProperty]
    private string _wizardOAuthUrl = string.Empty;

    [ObservableProperty]
    private int _wizardStepNumber;

    [ObservableProperty]
    private bool _showAdvancedBackendOptions;

    [ObservableProperty]
    private bool _hasAdvancedBackendOptions;

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private string _startupPreflightSummary = "Startup preflight has not been run.";

    [ObservableProperty]
    private string _startupPreflightReport = string.Empty;

    [ObservableProperty]
    private string? _selectedDiagnosticsProfileId;

    [ObservableProperty]
    private DiagnosticsProfileFilterOption? _selectedDiagnosticsProfileFilterOption;

    [ObservableProperty]
    private bool _startupTimelineOnly;

    [ObservableProperty]
    private bool _showDiagnosticsView;

    [ObservableProperty]
    private bool _showSettingsView;

    [ObservableProperty]
    private string _selectedDiagnosticsCategoryFilter = "All";

    [ObservableProperty]
    private string _diagnosticsSearchText = string.Empty;

    [ObservableProperty]
    private string _selectedReliabilityPresetId = ReliabilityPolicyPreset.NormalId;

    [ObservableProperty]
    private MountProfile? _selectedRemoteProfile;

    [ObservableProperty]
    private MountProfile? _selectedMountProfile;

    [ObservableProperty]
    private bool _showRemoteEditor;

    [ObservableProperty]
    private bool _isDeleteBlockedDialogVisible;

    [ObservableProperty]
    private string _deleteBlockedDialogMessage = string.Empty;

    [ObservableProperty]
    private bool _isTestDialogVisible;

    [ObservableProperty]
    private string _testDialogTitle = string.Empty;

    [ObservableProperty]
    private bool? _testDialogSuccess;

    private string? _testDialogProfileId;
    private string? _wizardState;

    [ObservableProperty]
    private MountProfile? _selectedMountRemoteProfile;

    public ObservableCollection<MountProfile> Profiles { get; } = new();
    public ObservableCollection<MountProfile> MountProfiles { get; } = new();
    public ObservableCollection<MountType> MountTypes { get; } = new(Enum.GetValues<MountType>());
    public ObservableCollection<QuickConnectMode> QuickConnectModes { get; } = new(Enum.GetValues<QuickConnectMode>());
    public ObservableCollection<string> ThemeModes { get; } = new() { "Follow system", "Dark", "Light" };
    public ObservableCollection<string> DiagnosticsCategoryFilters { get; } = new() { "All", "Remotes", "Mounts" };
    public ObservableCollection<DiagnosticsProfileFilterOption> DiagnosticsProfileFilters { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<DiagnosticsTimelineRow> DiagnosticsRows { get; } = new();
    public ObservableCollection<string> TestDialogLines { get; } = new();
    public ObservableCollection<RcloneBackendInfo> AvailableBackends { get; } = new();
    public ObservableCollection<RcloneBackendOptionInput> BackendOptionInputs { get; } = new();
    public ObservableCollection<RcloneBackendOptionInput> AdvancedBackendOptionInputs { get; } = new();
    public ObservableCollection<ReliabilityPolicyPreset> ReliabilityPresets { get; } = new(ReliabilityPolicyPreset.Catalog);
    public ObservableCollection<MountProfile> RemoteProfiles { get; } = new();
    public bool HasProfiles => Profiles.Count > 0;
    public MountProfile? SidebarSelectedRemoteProfile
    {
        get => IsRemoteListActive ? SelectedRemoteProfile : null;
        set
        {
            if (value is null)
            {
                return;
            }

            SelectedRemoteProfile = value;
        }
    }

    public MountProfile? SidebarSelectedMountProfile
    {
        get => IsMountListActive ? SelectedMountProfile : null;
        set
        {
            if (value is null)
            {
                return;
            }

            SelectedMountProfile = value;
        }
    }

    public bool HasDiagnosticsRows => DiagnosticsRows.Count > 0;
    public bool IsTestDialogRunning => TestDialogSuccess is null && IsTestDialogVisible;
    public bool ShowTestDialogSuccessIcon => TestDialogSuccess == true;
    public bool ShowTestDialogFailureIcon => TestDialogSuccess == false;
    public bool HasRemoteProfiles => RemoteProfiles.Count > 0;
    public bool HasMountProfiles => MountProfiles.Count > 0;
    public string DiagnosticsEmptyStateText => "No diagnostics for current filter.";
    public string WorkspaceTitle => ShowSettingsView
        ? "Settings"
        : ShowDiagnosticsView
        ? "Diagnostics"
        : ShowRemoteEditor ? "Remote Assistant" : "Mount Assistant";
    public string WorkspaceSubtitle => ShowSettingsView
        ? "Application preferences"
        : ShowDiagnosticsView
        ? "View log entries across all profiles"
        : ShowRemoteEditor
            ? "Choose backend -> set options -> create remote"
            : "Preset -> credentials -> mount path -> Start mount";
    public bool ShowRemoteEditorContent => ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView;
    public bool ShowWizardContent => IsWizardActive && ShowRemoteEditorContent;
    public bool ShowStandardRemoteForm => !IsWizardActive && ShowRemoteEditorContent;
    public bool ShowWizardOAuthSpinner => IsWizardWaitingForOAuth;
    public string WizardStepTitle => CurrentWizardStep?.Name ?? string.Empty;
    public string WizardStepHelp => CurrentWizardStep?.Help?.Replace("\n", " ").Trim() ?? string.Empty;
    public bool WizardHasExamples => CurrentWizardStep is { Examples.Count: > 0, Exclusive: true };
    public bool ShowMountEditorContent => !ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView;

    public static string RevealInFileManagerLabel => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
        System.Runtime.InteropServices.OSPlatform.Windows) ? "Show in Explorer"
        : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.OSX) ? "Show in Finder"
        : "Open in Files";
    public bool ShowSettingsContent => ShowSettingsView;
    public bool ShowEditorScrollViewer => !ShowDiagnosticsView;
    public bool IsRemoteListActive => ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView;
    public bool IsMountListActive => !ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView;
    public string DiagnosticsInfoCount => DiagnosticsRows.Count(r => string.Equals(r.SeverityText, "information", StringComparison.OrdinalIgnoreCase)).ToString();
    public string DiagnosticsWarningCount => DiagnosticsRows.Count(r => string.Equals(r.SeverityText, "warning", StringComparison.OrdinalIgnoreCase)).ToString();
    public string DiagnosticsErrorCount => DiagnosticsRows.Count(r => string.Equals(r.SeverityText, "error", StringComparison.OrdinalIgnoreCase)).ToString();

    public MainWindowViewModel(
        string? profilesFilePath = null,
        MountManagerService? mountManagerService = null,
        LaunchAgentService? launchAgentService = null,
        RcloneBackendService? rcloneBackendService = null,
        RcloneConfigWizardService? rcloneConfigWizardService = null,
        StartupPreflightService? startupPreflightService = null,
        MountHealthService? mountHealthService = null,
        Func<MountProfile, CancellationToken, Task>? mountStartRunner = null,
        Func<MountProfile, CancellationToken, Task>? mountStopRunner = null,
        Func<MountProfile, CancellationToken, Task>? testConnectionRunner = null,
        Func<MountProfile, CancellationToken, Task<bool>>? mountedProbe = null,
        Func<MountProfile, CancellationToken, Task<ProfileRuntimeState>>? runtimeStateVerifier = null,
        Func<MountProfile, CancellationToken, Task<StartupPreflightReport>>? startupPreflightRunner = null,
        Func<MountProfile, string, CancellationToken, Task>? startupEnableRunner = null,
        Func<MountProfile, CancellationToken, Task>? startupDisableRunner = null,
        Func<MountProfile, bool>? startupEnabledProbe = null,
        Func<TimeSpan, CancellationToken, Task<bool>>? runtimeRefreshWaiter = null,
        Func<IEnumerable<MountProfile>, CancellationToken, Task<IReadOnlyList<ProfileRuntimeState>>>? runtimeStateBatchVerifier = null,
        bool loadStartupData = true,
        ILogger<MainWindowViewModel>? logger = null)
    {
        _mountManagerService = mountManagerService ?? new MountManagerService(NullLogger<MountManagerService>.Instance);
        _launchAgentService = launchAgentService ?? new LaunchAgentService(NullLogger<LaunchAgentService>.Instance);
        _rcloneBackendService = rcloneBackendService ?? new RcloneBackendService(NullLogger<RcloneBackendService>.Instance);
        _rcloneConfigWizardService = rcloneConfigWizardService ?? new RcloneConfigWizardService(NullLogger<RcloneConfigWizardService>.Instance);
        _startupPreflightService = startupPreflightService ?? new StartupPreflightService(NullLogger<StartupPreflightService>.Instance);
        _mountHealthService = mountHealthService ?? new MountHealthService(NullLogger<MountHealthService>.Instance);
        _logger = logger ?? NullLogger<MainWindowViewModel>.Instance;
        _mountStartRunner = mountStartRunner ?? _mountManagerService.StartAsync;
        _mountStopRunner = mountStopRunner ?? _mountManagerService.StopAsync;
        _testConnectionRunner = testConnectionRunner;
        _mountedProbe = mountedProbe ?? ((profile, cancellationToken) => _mountManagerService.IsMountedAsync(profile.MountPoint, cancellationToken));
        _runtimeStateVerifier = runtimeStateVerifier ?? _mountHealthService.VerifyAsync;
        _startupPreflightRunner = startupPreflightRunner ?? _startupPreflightService.RunAsync;
        _startupEnableRunner = startupEnableRunner ?? _launchAgentService.EnableAsync;
        _startupDisableRunner = startupDisableRunner ?? _launchAgentService.DisableAsync;
        _startupEnabledProbe = startupEnabledProbe ?? _launchAgentService.IsEnabled;
        _runtimeRefreshWaiter = runtimeRefreshWaiter ?? DefaultRuntimeRefreshWaiterAsync;
        _runtimeStateBatchVerifier = runtimeStateBatchVerifier ?? _mountHealthService.VerifyAllAsync;
        _isStartupSupported = _launchAgentService.IsSupported;

        _profilesFilePath = profilesFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RcloneMountManager",
            "profiles.json");

        Profiles.CollectionChanged += OnProfilesCollectionChanged;
        DiagnosticsRows.CollectionChanged += OnDiagnosticsRowsCollectionChanged;
        _profileLogs.TryAdd(DiagnosticsSink.SystemProfileId, new List<ProfileLogEvent>());
        DiagnosticsSink.Instance.RegisterHandler(OnSerilogEvent);
        LoadProfiles();
        LoadBackendsSync(loadStartupData);

        if (Profiles.Count == 0 && !File.Exists(_profilesFilePath))
        {
            var defaultProfile = CreateDefaultProfile();
            Profiles.Add(defaultProfile);
            _profileLogs[defaultProfile.Id] = new List<ProfileLogEvent>();
            _profileScripts[defaultProfile.Id] = string.Empty;
        }

        EnsureRemoteDefinitionsForMountSources();
        RefreshRemoteProfiles();
        RefreshMountProfiles();

        if (Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
            _syncingSidebarSelection = true;
            SelectedMountProfile = MountProfiles.FirstOrDefault(p => ReferenceEquals(p, SelectedProfile)) ?? MountProfiles.FirstOrDefault();
            SelectedRemoteProfile = RemoteProfiles.FirstOrDefault(p => ReferenceEquals(p, SelectedProfile)) ?? RemoteProfiles.FirstOrDefault();
            _syncingSidebarSelection = false;
            _rememberedMountProfile = SelectedMountProfile;
            _rememberedRemoteProfile = SelectedRemoteProfile;
            ShowRemoteEditor = false;
            EnsureSingleActiveSidebarSelection();
            UpdateSelectedMountRemoteFromSource(SelectedProfile);
        }
        else
        {
            _syncingSidebarSelection = true;
            SelectedMountProfile = null;
            SelectedRemoteProfile = null;
            _syncingSidebarSelection = false;
            _rememberedMountProfile = null;
            _rememberedRemoteProfile = null;
            ShowRemoteEditor = false;
            StatusText = "Library is empty.";
        }

        SyncDiagnosticsFilters();
        HasPendingChanges = false;
        if (SelectedProfile is { } selectedProfile)
            using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Initialization))
                _logger.LogInformation("Profiles file: {ProfilesFilePath}", _profilesFilePath);

        if (!loadStartupData)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
                await MountOptionsVm.LoadOptionsAsync(binary, SelectedProfile?.MountOptions ?? new Dictionary<string, string>(), CancellationToken.None, SelectedProfile?.PinnedMountOptions);
            }
            catch (Exception ex)
            {
                if (SelectedProfile is { } selectedProfile)
                    using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Initialization))
                        _logger.LogError(ex, "Could not load mount options: {ErrorMessage}", ex.Message);
            }
        });
    }

    public bool HasSelectedMountRemote => SelectedMountRemoteProfile is not null
        && SelectedProfile is { IsRemoteDefinition: false, Type: not MountType.MacOsNfs };

    public string MountRemotePath
    {
        get
        {
            if (SelectedProfile is null)
                return string.Empty;
            if (!TryGetRemoteAliasFromSource(SelectedProfile.Source, out _, out string suffix))
                return SelectedProfile.Source;
            return suffix;
        }
        set
        {
            if (SelectedProfile is null)
                return;
            var remoteAlias = SelectedMountRemoteProfile is not null
                ? GetRemoteAlias(SelectedMountRemoteProfile) : null;
            if (!string.IsNullOrWhiteSpace(remoteAlias))
            {
                var path = string.IsNullOrWhiteSpace(value) ? "/" : value;
                SelectedProfile.Source = $"{remoteAlias}:{path}";
            }
            else
            {
                SelectedProfile.Source = value;
            }
            OnPropertyChanged();
        }
    }

    public string SourceLabel => SelectedProfile?.Type switch
    {
        MountType.MacOsNfs => "NFS export",
        _ when HasSelectedMountRemote => "Remote path",
        _ => "Source",
    };

    public string SourceHint => SelectedProfile?.Type switch
    {
        MountType.MacOsNfs => "Example: 192.168.1.10:/volume1/media",
        _ when HasSelectedMountRemote => "/ (root of remote)",
        _ => "Example: remote:media",
    };

    public string MountPointHint => $"Example: {DefaultMountPoint("media")}";

    public string OptionsHint => SelectedProfile?.Type is MountType.MacOsNfs
        ? "Example: nfsvers=4,resvport"
        : "Example: --vfs-cache-mode full --dir-cache-time 15m";

    public string SourceFormatHelp => SelectedProfile?.Type switch
    {
        MountType.MacOsNfs => "NFS uses host + export path directly.",
        _ when HasSelectedMountRemote => "Path on the remote to mount (use / for root).",
        _ => "For rclone use remote:path (create remote first or use the backend builder).",
    };

    public bool CanUseQuickConnect => SelectedProfile?.Type is MountType.RcloneAuto;

    public bool ShowQuickConnectSettings =>
        SelectedProfile?.Type is MountType.RcloneAuto && SelectedProfile.QuickConnectMode is not QuickConnectMode.None;

    public bool ShowQuickConnectPort =>
        SelectedProfile?.QuickConnectMode is QuickConnectMode.Sftp or QuickConnectMode.Ftp or QuickConnectMode.Ftps;

    public string QuickConnectModeHelp => SelectedProfile?.QuickConnectMode switch
    {
        QuickConnectMode.WebDav => "WebDAV: provide URL, username, and password.",
        QuickConnectMode.Sftp => "SFTP: provide host, port (22), username, and password.",
        QuickConnectMode.Ftp => "FTP: provide host, port (21), username, and password.",
        QuickConnectMode.Ftps => "FTPS: provide host, port (21), username, and password.",
        _ => string.Empty,
    };

    public string QuickConnectEndpointLabel => SelectedProfile?.QuickConnectMode is QuickConnectMode.WebDav ? "Endpoint URL" : "Host";

    public string QuickConnectEndpointHint => SelectedProfile?.QuickConnectMode is QuickConnectMode.WebDav
        ? "https://example.com/remote.php/webdav"
        : "Example: ftp.example.com";

    public string QuickStartHelp =>
        "Quick start: choose a preset, change mount path, then click Start mount.";

    public bool IsStartupSupported => _isStartupSupported;

    public string StartupButtonText => SelectedProfile?.StartAtLogin is true ? "Disable start at login" : "Enable start at login";

    public string SaveChangesButtonText => HasPendingChanges ? "Save mount *" : "Save mount";

    public string SelectedProfileLifecycleText => FormatLifecycle(SelectedProfile?.RuntimeState.Lifecycle ?? MountLifecycleState.Idle);

    public string SelectedProfileHealthText => FormatHealth(SelectedProfile?.RuntimeState.Health ?? MountHealthState.Unknown);

    public bool HasBackendOptions => BackendOptionInputs.Count > 0;
    public bool HasAdvancedBackendOptionInputs => AdvancedBackendOptionInputs.Count > 0;

    public string SelectedBackendDescription => SelectedBackend?.Details ?? string.Empty;

    [RelayCommand]
    private void AddMount()
    {
        var id = Guid.NewGuid().ToString("N");
        var profile = new MountProfile
        {
            Id = id,
            Name = $"Mount {MountProfiles.Count + 1}",
            Type = MountType.RcloneAuto,
            Source = string.Empty,
            MountPoint = DefaultMountPoint("new-mount"),
            ExtraOptions = "--vfs-cache-mode full",
            MountOptions = GetDefaultMountOptions(MountType.RcloneAuto),
            QuickConnectMode = QuickConnectMode.None,
            IsRemoteDefinition = false,
            RcPort = MountManagerService.AssignRcPort(id),
        };

        Profiles.Add(profile);
        _profileLogs[profile.Id] = new List<ProfileLogEvent>();
        _profileScripts[profile.Id] = string.Empty;
        ShowRemoteEditor = false;
        SelectedProfile = profile;
        SelectedMountProfile = profile;
        UpdateSelectedMountRemoteFromSource(profile);
        MarkDirty();
    }

    [RelayCommand]
    private void AddRemote()
    {
        var remoteNumber = RemoteProfiles.Count + 1;
        var remoteAlias = $"remote{remoteNumber}";
        var profile = CreateRemoteDefinitionProfile(remoteAlias, $"Remote {remoteNumber}");

        Profiles.Add(profile);
        _profileLogs[profile.Id] = new List<ProfileLogEvent>();
        _profileScripts[profile.Id] = string.Empty;
        ShowRemoteEditor = true;
        SelectedProfile = profile;
        SelectedRemoteProfile = profile;
        MarkDirty();
    }

    [RelayCommand]
    private void AddProfile()
    {
        AddMount();
    }

    [RelayCommand]
    private void SelectDiagnostics()
    {
        ShowSettingsView = false;
        ShowDiagnosticsView = true;
        RefreshDiagnosticsTimeline();
    }

    [RelayCommand]
    private void SelectSettings()
    {
        ShowDiagnosticsView = false;
        ShowSettingsView = true;
    }

    [RelayCommand]
    private async Task CopyDiagnosticsAsync()
    {
        var rows = DiagnosticsRows.ToList();
        if (rows.Count == 0) return;

        var text = string.Join(Environment.NewLine,
            rows.Select(r => $"{r.TimestampText}\t{r.ProfileName}\t{r.SeverityText}\t{r.StageText}\t{r.MessageText}"));

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            StatusText = $"Copied {rows.Count} log entries to clipboard.";
        }
    }

    [RelayCommand]
    private async Task CopySelectedDiagnosticsAsync(object? selectedItems)
    {
        if (selectedItems is not System.Collections.IList items || items.Count == 0) return;

        var rows = items.OfType<DiagnosticsTimelineRow>().ToList();
        if (rows.Count == 0) return;

        var text = string.Join(Environment.NewLine,
            rows.Select(r => $"{r.TimestampText}\t{r.ProfileName}\t{r.SeverityText}\t{r.StageText}\t{r.MessageText}"));

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            StatusText = $"Copied {rows.Count} selected log entries to clipboard.";
        }
    }

    private void LoadBackendsSync(bool enabled)
    {
        if (!enabled) return;

        try
        {
            var binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
            var backends = Task.Run(() => _rcloneBackendService.GetBackendsAsync(binary, CancellationToken.None)).GetAwaiter().GetResult();

            AvailableBackends.Clear();
            foreach (var backend in backends)
            {
                AvailableBackends.Add(backend);
            }

            if (AvailableBackends.Count > 0)
            {
                SelectedBackend = AvailableBackends[0];
            }

            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
                    _logger.LogInformation("Loaded {BackendCount} rclone backend types.", AvailableBackends.Count);
        }
        catch (Exception ex)
        {
            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Initialization))
                    _logger.LogError(ex, "Could not load backend list: {ErrorMessage}", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateRemote))]
    private async Task CreateRemoteAsync()
    {
        await RunBusyActionAsync(async cancellationToken =>
        {
            if (SelectedBackend is null)
            {
                throw new InvalidOperationException("Please select a backend first.");
            }

            var missingRequired = BackendOptionInputs
                .Where(o => o.Required && string.IsNullOrWhiteSpace(o.Value))
                .Select(o => o.Name)
                .ToList();

            if (missingRequired.Count > 0)
            {
                throw new InvalidOperationException($"Missing required fields: {string.Join(", ", missingRequired)}");
            }

            var binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
            var activeProfile = SelectedProfile ?? throw new InvalidOperationException("No profile selected.");
            if (!activeProfile.IsRemoteDefinition)
            {
                throw new InvalidOperationException("Create remote is only available for REMOTES entries.");
            }

            await _rcloneBackendService.CreateRemoteAsync(
                binary,
                NewRemoteName,
                SelectedBackend.Name,
                BackendOptionInputs,
                cancellationToken);

            activeProfile.Name = NewRemoteName.Trim();
            activeProfile.Source = $"{NewRemoteName.Trim()}:/";
            activeProfile.Type = MountType.RcloneAuto;
            activeProfile.QuickConnectMode = QuickConnectMode.None;
            activeProfile.BackendName = SelectedBackend.Name;
            activeProfile.BackendOptions = BackendOptionInputs
                .Concat(AdvancedBackendOptionInputs)
                .Where(o => !string.IsNullOrWhiteSpace(o.Value))
                .ToDictionary(o => o.Name, o => o.Value);

            using (ProfileScope(activeProfile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
                _logger.LogInformation("Created remote '{RemoteName}' ({BackendName}).", NewRemoteName, SelectedBackend.Name);
            StatusText = $"Remote '{NewRemoteName}' created.";
            MarkDirty();
            SaveProfiles();
            HasPendingChanges = false;
            StatusText = $"Remote '{NewRemoteName}' saved.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartWizard))]
    private async Task StartWizardAsync()
    {
        await RunBusyActionAsync(async cancellationToken =>
        {
            if (SelectedBackend is null || SelectedProfile is null) return;

            var binary = SelectedProfile.RcloneBinaryPath ?? "rclone";
            var remoteName = string.IsNullOrWhiteSpace(NewRemoteName)
                ? $"{SelectedBackend.Name}-remote"
                : NewRemoteName;

            try
            {
                var step = await _rcloneConfigWizardService.StartAsync(binary, remoteName, SelectedBackend.Name, cancellationToken);

                IsWizardActive = true;
                WizardStepNumber = 1;
                await HandleWizardStepAsync(step, binary, remoteName, cancellationToken);
            }
            catch
            {
                ResetWizardState();
                throw;
            }
        });
    }

    private bool CanStartWizard() =>
        !IsBusy &&
        HasProfiles &&
        SelectedBackend is not null &&
        !string.IsNullOrWhiteSpace(NewRemoteName) &&
        SelectedProfile is not null &&
        SelectedProfile.IsRemoteDefinition;

    [RelayCommand]
    private async Task SubmitWizardAnswerAsync()
    {
        await RunBusyActionAsync(async cancellationToken =>
        {
            if (_wizardState is null || SelectedProfile is null || SelectedBackend is null) return;

            var binary = SelectedProfile.RcloneBinaryPath ?? "rclone";
            var remoteName = NewRemoteName;

            try
            {
                WizardStepNumber++;
                var answer = WizardStepInput?.Value ?? WizardAnswer;
                var step = await _rcloneConfigWizardService.ContinueAsync(
                    binary, remoteName, _wizardState, answer, cancellationToken);

                await HandleWizardStepAsync(step, binary, remoteName, cancellationToken);
            }
            catch
            {
                ResetWizardState();
                throw;
            }
        });
    }

    [RelayCommand]
    private async Task CancelWizardAsync()
    {
        if (SelectedProfile is not null && !string.IsNullOrWhiteSpace(NewRemoteName))
        {
            try
            {
                var binary = SelectedProfile.RcloneBinaryPath ?? "rclone";
                await _rcloneConfigWizardService.DeleteRemoteAsync(binary, NewRemoteName, CancellationToken.None);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        ResetWizardState();
    }

    private async Task HandleWizardStepAsync(ConfigWizardStep step, string binary, string remoteName, CancellationToken cancellationToken)
    {
        if (step.IsComplete)
        {
            await ReadBackWizardConfigAsync(binary, remoteName, cancellationToken);
            ResetWizardState();
            StatusText = $"Remote '{remoteName}' configured successfully.";
            return;
        }

        if (step.IsOAuthBrowserPrompt)
        {
            IsWizardWaitingForOAuth = true;
            WizardStepNumber++;
            try
            {
                var nextStep = await _rcloneConfigWizardService.ContinueOAuthAsync(
                    binary, remoteName, step.State,
                    url =>
                    {
                        WizardOAuthUrl = url;
                        _ = OpenBrowserAsync(url);
                    },
                    cancellationToken);
                IsWizardWaitingForOAuth = false;
                await HandleWizardStepAsync(nextStep, binary, remoteName, cancellationToken);
            }
            catch
            {
                IsWizardWaitingForOAuth = false;
                throw;
            }
            return;
        }

        if (step.IsAdvancedPrompt)
        {
            WizardStepNumber++;
            var nextStep = await _rcloneConfigWizardService.ContinueAsync(
                binary, remoteName, step.State, "false", cancellationToken);
            await HandleWizardStepAsync(nextStep, binary, remoteName, cancellationToken);
            return;
        }

        CurrentWizardStep = step;
        _wizardState = step.State;
        WizardStepInput = new WizardStepOptionInput(step);
        WizardAnswer = step.DefaultValue;
    }

    private async Task ReadBackWizardConfigAsync(string binary, string remoteName, CancellationToken cancellationToken)
    {
        var config = await _rcloneConfigWizardService.ReadRemoteConfigAsync(binary, remoteName, cancellationToken);

        if (SelectedProfile is not null)
        {
            SelectedProfile.Name = remoteName;
            SelectedProfile.Source = $"{remoteName}:";
            if (SelectedBackend is not null)
            {
                SelectedProfile.BackendName = SelectedBackend.Name;
            }

            var backendOptions = new Dictionary<string, string>();
            foreach (var kvp in config)
            {
                if (string.Equals(kvp.Key, "type", StringComparison.OrdinalIgnoreCase)) continue;
                backendOptions[kvp.Key] = kvp.Value;
            }
            SelectedProfile.BackendOptions = backendOptions;

            foreach (var input in BackendOptionInputs.Concat(AdvancedBackendOptionInputs))
            {
                if (config.TryGetValue(input.Name, out string? value))
                {
                    input.Value = value;
                }
            }

            SaveProfiles();
        }
    }

    private void ResetWizardState()
    {
        IsWizardActive = false;
        IsWizardWaitingForOAuth = false;
        CurrentWizardStep = null;
        WizardStepInput = null;
        WizardAnswer = string.Empty;
        WizardOAuthUrl = string.Empty;
        WizardStepNumber = 0;
        _wizardState = null;
    }

    private static async Task OpenBrowserAsync(string url)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } window)
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(window);
                if (topLevel?.Launcher is { } launcher)
                {
                    await launcher.LaunchUriAsync(new Uri(url));
                }
            }
        }
        catch
        {
            // Browser launch failed - user can manually copy the URL
        }
    }

    [RelayCommand]
    private void UseRcloneExample()
    {
        SelectedProfile.Type = MountType.RcloneAuto;
        SelectedProfile.QuickConnectMode = QuickConnectMode.None;
        SelectedProfile.Source = "remote:media";
        SelectedProfile.MountPoint = DefaultMountPoint("media");
        SelectedProfile.ExtraOptions = "--vfs-cache-mode full --dir-cache-time 15m";
        SelectedProfile.RcloneBinaryPath = "rclone";
        ResetQuickConnectFields();
        StatusText = "Preset loaded: rclone remote.";
        NotifyLabelsChanged();
    }

    [RelayCommand]
    private void UseWebDavExample()
    {
        SelectedProfile.Type = MountType.RcloneAuto;
        SelectedProfile.QuickConnectMode = QuickConnectMode.WebDav;
        SelectedProfile.Source = "/";
        SelectedProfile.MountPoint = DefaultMountPoint("webdav");
        SelectedProfile.ExtraOptions = "--vfs-cache-mode full --dir-cache-time 15m";
        SelectedProfile.RcloneBinaryPath = "rclone";
        SelectedProfile.QuickConnectEndpoint = "https://example.com/remote.php/webdav";
        SelectedProfile.QuickConnectPort = string.Empty;
        SelectedProfile.QuickConnectUsername = string.Empty;
        SelectedProfile.QuickConnectPassword = string.Empty;
        StatusText = "Preset loaded: WebDAV Quick Connect.";
        NotifyLabelsChanged();
    }

    [RelayCommand]
    private void UseSftpExample()
    {
        SelectedProfile.Type = MountType.RcloneAuto;
        SelectedProfile.QuickConnectMode = QuickConnectMode.Sftp;
        SelectedProfile.Source = "/";
        SelectedProfile.MountPoint = DefaultMountPoint("sftp");
        SelectedProfile.ExtraOptions = "--vfs-cache-mode full";
        SelectedProfile.RcloneBinaryPath = "rclone";
        SelectedProfile.QuickConnectEndpoint = "sftp.example.com";
        SelectedProfile.QuickConnectPort = "22";
        SelectedProfile.QuickConnectUsername = string.Empty;
        SelectedProfile.QuickConnectPassword = string.Empty;
        StatusText = "Preset loaded: SFTP Quick Connect.";
        NotifyLabelsChanged();
    }

    [RelayCommand]
    private void UseFtpExample()
    {
        SelectedProfile.Type = MountType.RcloneAuto;
        SelectedProfile.QuickConnectMode = QuickConnectMode.Ftp;
        SelectedProfile.Source = "/";
        SelectedProfile.MountPoint = DefaultMountPoint("ftp");
        SelectedProfile.ExtraOptions = "--vfs-cache-mode full";
        SelectedProfile.RcloneBinaryPath = "rclone";
        SelectedProfile.QuickConnectEndpoint = "ftp.example.com";
        SelectedProfile.QuickConnectPort = "21";
        SelectedProfile.QuickConnectUsername = string.Empty;
        SelectedProfile.QuickConnectPassword = string.Empty;
        StatusText = "Preset loaded: FTP Quick Connect.";
        NotifyLabelsChanged();
    }

    [RelayCommand]
    private void UseFtpsExample()
    {
        SelectedProfile.Type = MountType.RcloneAuto;
        SelectedProfile.QuickConnectMode = QuickConnectMode.Ftps;
        SelectedProfile.Source = "/";
        SelectedProfile.MountPoint = DefaultMountPoint("ftps");
        SelectedProfile.ExtraOptions = "--vfs-cache-mode full";
        SelectedProfile.RcloneBinaryPath = "rclone";
        SelectedProfile.QuickConnectEndpoint = "ftp.example.com";
        SelectedProfile.QuickConnectPort = "21";
        SelectedProfile.QuickConnectUsername = string.Empty;
        SelectedProfile.QuickConnectPassword = string.Empty;
        StatusText = "Preset loaded: FTPS Quick Connect.";
        NotifyLabelsChanged();
    }

    [RelayCommand]
    private void UseNfsExample()
    {
        SelectedProfile.Type = MountType.MacOsNfs;
        SelectedProfile.QuickConnectMode = QuickConnectMode.None;
        SelectedProfile.Source = "192.168.1.10:/volume1/media";
        SelectedProfile.MountPoint = DefaultMountPoint("media");
        SelectedProfile.ExtraOptions = "nfsvers=4,resvport";
        ResetQuickConnectFields();
        StatusText = "Preset loaded: NFS.";
        NotifyLabelsChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveProfile))]
    private void RemoveProfile()
    {
        if (!HasProfiles)
        {
            return;
        }

        var profileToRemove = SelectedProfile;
        if (profileToRemove.IsRemoteDefinition)
        {
            var remoteAlias = GetRemoteAlias(profileToRemove);
            if (!string.IsNullOrWhiteSpace(remoteAlias))
            {
                var dependentMounts = Profiles
                    .Where(IsMountProfileCandidate)
                    .Where(RequiresRemoteAssociation)
                    .Where(mount => TryGetRemoteAliasFromSource(mount.Source, out var mountAlias, out _) &&
                        string.Equals(mountAlias, remoteAlias, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (dependentMounts.Count > 0)
                {
                    var mountNames = string.Join(", ", dependentMounts.Select(m => m.Name));
                    var message = $"Cannot delete remote '{profileToRemove.Name}'. It is still used by {dependentMounts.Count} mount(s): {mountNames}. Remove or reassign those mounts first.";
                    DeleteBlockedDialogMessage = message;
                    IsDeleteBlockedDialogVisible = true;
                    StatusText = message;
                    return;
                }
            }
        }

        var index = Profiles.IndexOf(profileToRemove);
        var removedId = profileToRemove.Id;
        Profiles.Remove(profileToRemove);
        _profileLogs.Remove(removedId);
        _profileScripts.Remove(removedId);

        if (Profiles.Count == 0)
        {
            _syncingSidebarSelection = true;
            SelectedRemoteProfile = null;
            SelectedMountProfile = null;
            _syncingSidebarSelection = false;
            _rememberedRemoteProfile = null;
            _rememberedMountProfile = null;
            ShowRemoteEditor = false;
            StatusText = $"Removed {GetProfileTypeLabel(profileToRemove)} '{profileToRemove.Name}'. Library is now empty.";
            OnPropertyChanged(nameof(HasProfiles));
            NotifyCommandStateChanged();
            MarkDirty();
            SaveProfiles();
            HasPendingChanges = false;
            StatusText = "Library is now empty and saved.";
            return;
        }

        var fallback = Profiles[Math.Max(0, index - 1)];
        SelectedProfile = fallback;
        StatusText = $"Removed {GetProfileTypeLabel(profileToRemove)} '{profileToRemove.Name}'.";
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task StartMountAsync()
    {
        SyncMountOptionsToProfile();
        await RunBusyActionAsync(async cancellationToken =>
        {
            var profile = SelectedProfile;
            var profileId = profile.Id;
            using (ProfileScope(profileId, ProfileLogCategory.ManualStart, ProfileLogStage.Initialization))
                _logger.LogInformation("Starting mount...");
            ApplyRuntimeState(profile, new ProfileRuntimeState(MountLifecycleState.Mounting, MountHealthState.Unknown, DateTimeOffset.UtcNow, null));

            try
            {
                using (ProfileScope(profileId, ProfileLogCategory.ManualStart, ProfileLogStage.Execution))
                    await Task.Run(() => _mountStartRunner(profile, cancellationToken), cancellationToken);
                await RefreshRuntimeStateInternalAsync(profile, cancellationToken);
            }
            catch (Exception ex)
            {
                ApplyRuntimeState(profile, new ProfileRuntimeState(MountLifecycleState.Failed, MountHealthState.Failed, DateTimeOffset.UtcNow, ex.Message));
                throw;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task StopMountAsync()
    {
        await RunBusyActionAsync(async cancellationToken =>
        {
            var profile = SelectedProfile;
            var profileId = profile.Id;
            using (ProfileScope(profileId, ProfileLogCategory.ManualStop, ProfileLogStage.Initialization))
                _logger.LogInformation("Stopping mount...");

            try
            {
                using (ProfileScope(profileId, ProfileLogCategory.ManualStop, ProfileLogStage.Execution))
                    await Task.Run(() => _mountStopRunner(profile, cancellationToken), cancellationToken);

                if (!await Task.Run(() => _mountedProbe(profile, cancellationToken), cancellationToken))
                {
                    ApplyRuntimeState(profile, new ProfileRuntimeState(MountLifecycleState.Idle, MountHealthState.Unknown, DateTimeOffset.UtcNow, null));
                    return;
                }

                await RefreshRuntimeStateInternalAsync(profile, cancellationToken);
            }
            catch (Exception ex)
            {
                ApplyRuntimeState(profile, new ProfileRuntimeState(MountLifecycleState.Failed, MountHealthState.Failed, DateTimeOffset.UtcNow, ex.Message));
                throw;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanRevealInFileManager))]
    private async Task RevealInFileManagerAsync()
    {
        var mountPoint = SelectedProfile?.MountPoint;
        if (string.IsNullOrWhiteSpace(mountPoint) || !Directory.Exists(mountPoint)) return;

        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } window)
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(window);
                if (topLevel?.Launcher is { } launcher)
                {
                    await launcher.LaunchUriAsync(new Uri($"file://{mountPoint}"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open file manager for {MountPoint}", mountPoint);
        }
    }

    private bool CanRevealInFileManager() =>
        HasProfiles &&
        !IsBusy &&
        SelectedProfile is not null &&
        !SelectedProfile.IsRemoteDefinition &&
        !string.IsNullOrWhiteSpace(SelectedProfile.MountPoint) &&
        SelectedProfile.RuntimeState.Lifecycle is MountLifecycleState.Mounted &&
        SelectedProfile.RuntimeState.Health is MountHealthState.Healthy or MountHealthState.Degraded;

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task RefreshStatusAsync()
    {
        await RunBusyActionAsync(RefreshStatusInternalAsync);
    }

    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        TestDialogLines.Clear();
        TestDialogSuccess = null;
        TestDialogTitle = "Testing connection...";
        IsTestDialogVisible = true;

        try
        {
            var profile = SelectedProfile;
            var profileId = profile.Id;
            _testDialogProfileId = profileId;
            using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Initialization))
                _logger.LogInformation("Testing connection...");

            if (_testConnectionRunner is not null)
            {
                using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Execution))
                    await _testConnectionRunner(profile, CancellationToken.None);
            }
            else if (profile.IsRemoteDefinition && SelectedBackend is not null)
            {
                var binary = profile.RcloneBinaryPath ?? "rclone";
                using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Execution))
                    await _mountManagerService.TestBackendConnectionAsync(
                        binary,
                        SelectedBackend.Name,
                        BackendOptionInputs.Concat(AdvancedBackendOptionInputs),
                        CancellationToken.None);
            }
            else
            {
                using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Execution))
                    await _mountManagerService.TestConnectionAsync(profile, CancellationToken.None);
            }

            TestDialogSuccess = true;
            TestDialogTitle = "Connection test passed";
            StatusText = "Connectivity test passed.";
        }
        catch (Exception ex)
        {
            TestDialogLines.Add(ex.Message);
            TestDialogSuccess = false;
            TestDialogTitle = "Connection test failed";
            StatusText = "Connectivity test failed.";
            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Execution))
                    _logger.LogError(ex, "{ErrorMessage}", ex.Message);
        }
        finally
        {
            _testDialogProfileId = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private void GenerateScript()
    {
        SyncMountOptionsToProfile();
        GeneratedScript = _mountManagerService.GenerateScript(SelectedProfile);
        _profileScripts[SelectedProfile.Id] = GeneratedScript;
        using (ProfileScope(SelectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
            _logger.LogInformation("Generated shell script preview.");
    }

    [RelayCommand]
    private void ApplyReliabilityPreset()
    {
        var profile = SelectedProfile;
        if (profile.IsRemoteDefinition || profile.Type is not MountType.RcloneAuto)
        {
            StatusText = "Reliability presets apply to rclone profiles only.";
            return;
        }

        SyncMountOptionsToProfile();

        var preset = ReliabilityPolicyPreset.GetByIdOrDefault(SelectedReliabilityPresetId);
        var patchedOptions = new Dictionary<string, string>(profile.MountOptions, StringComparer.OrdinalIgnoreCase);

        foreach (var key in ReliabilityPolicyPreset.ManagedReliabilityKeys)
        {
            patchedOptions.Remove(key);
        }

        foreach (var (key, value) in preset.OptionOverrides)
        {
            patchedOptions[key] = value;
        }

        profile.SelectedReliabilityPresetId = preset.Id;
        profile.MountOptions = patchedOptions;
        MountOptionsVm.UpdateFromProfile(profile.MountOptions, profile.PinnedMountOptions);
        SelectedReliabilityPresetId = preset.Id;

        StatusText = $"Applied reliability preset: {preset.DisplayName}.";
        using (ProfileScope(profile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
            _logger.LogInformation("Applied reliability preset '{PresetDisplayName}'.", preset.DisplayName);
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanSaveScript))]
    private async Task SaveScriptAsync()
    {
        SyncMountOptionsToProfile();
        var scriptPath = _launchAgentService.GetScriptPath(SelectedProfile);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);

        GeneratedScript = _mountManagerService.GenerateScript(SelectedProfile);
        _profileScripts[SelectedProfile.Id] = GeneratedScript;

        await File.WriteAllTextAsync(scriptPath, GeneratedScript);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        using (ProfileScope(SelectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
            _logger.LogInformation("Script saved to: {ScriptPath}", scriptPath);
    }

    [RelayCommand]
    private void DismissDeleteBlockedDialog()
    {
        IsDeleteBlockedDialogVisible = false;
        DeleteBlockedDialogMessage = string.Empty;
    }

    [RelayCommand]
    private void DismissTestDialog()
    {
        IsTestDialogVisible = false;
        TestDialogTitle = string.Empty;
        TestDialogLines.Clear();
        TestDialogSuccess = null;
    }

    [RelayCommand(CanExecute = nameof(CanToggleStartup))]
    private async Task ToggleStartupAsync()
    {
        SyncMountOptionsToProfile();
        string? completionStatus = null;
        await RunBusyActionAsync(async cancellationToken =>
        {
            var profile = SelectedProfile;
            var profileId = profile.Id;

            if (!IsStartupSupported)
            {
                throw new InvalidOperationException("Start at login is currently supported on macOS only.");
            }

            if (profile.StartAtLogin)
            {
                using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Execution))
                    await _startupDisableRunner(profile, cancellationToken);
                profile.StartAtLogin = false;
                SaveProfiles();
                HasPendingChanges = false;
                completionStatus = "Start at login disabled and saved.";
                using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Completion))
                    _logger.LogInformation("Persisted startup preference after disable.");
            }
            else
            {
                var report = await Task.Run(() => _startupPreflightRunner(profile, cancellationToken), cancellationToken);
                RecordStartupPreflightReport(profileId, report);
                AppendStartupPreflightChecksToLog(profileId, report);

                if (!report.CriticalChecksPassed)
                {
                    completionStatus = "Start at login blocked: startup preflight failed.";
                    using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                        _logger.LogWarning("Startup enable blocked by critical preflight failures.");
                    return;
                }

                var resolved = MountManagerService.ResolveAbsoluteBinaryPath(profile.RcloneBinaryPath);
                if (!string.Equals(profile.RcloneBinaryPath, resolved, StringComparison.Ordinal))
                {
                    profile.RcloneBinaryPath = resolved;
                    using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Execution))
                        _logger.LogInformation("Resolved rclone binary path to '{ResolvedBinaryPath}'.", resolved);
                }

                var script = _mountManagerService.GenerateScript(profile);
                using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Execution))
                    await _startupEnableRunner(profile, script, cancellationToken);
                profile.StartAtLogin = true;
                SaveProfiles();
                HasPendingChanges = false;
                completionStatus = "Start at login enabled and saved.";
                using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Completion))
                    _logger.LogInformation("Persisted startup preference after enable.");
            }

            OnPropertyChanged(nameof(StartupButtonText));
            OnPropertyChanged(nameof(SaveChangesButtonText));
            NotifyCommandStateChanged();
        });

        if (!string.IsNullOrWhiteSpace(completionStatus) && !IsBusy)
        {
            StatusText = completionStatus;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunStartupPreflight))]
    private async Task RunStartupPreflightAsync()
    {
        SyncMountOptionsToProfile();
        string? completionStatus = null;

        await RunBusyActionAsync(async cancellationToken =>
        {
            var profile = SelectedProfile;
            var profileId = profile.Id;
            var report = await Task.Run(() => _startupPreflightRunner(profile, cancellationToken), cancellationToken);
            RecordStartupPreflightReport(profileId, report);
            AppendStartupPreflightChecksToLog(profileId, report);

            completionStatus = report.CriticalChecksPassed
                ? "Startup preflight passed."
                : "Startup preflight completed with failures.";
        });

        if (!string.IsNullOrWhiteSpace(completionStatus) && !IsBusy)
        {
            StatusText = completionStatus;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private void SaveChanges()
    {
        if (HasAnyInvalidMountRemoteAssociations())
        {
            StatusText = "Assign a remote to each rclone mount before saving.";
            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Verification))
                    _logger.LogWarning("Save blocked: one or more mounts have no associated remote.");
            return;
        }

        SaveProfiles();
        HasPendingChanges = false;
        StatusText = "Profile changes saved.";
        if (SelectedProfile is { } activeProfile)
            using (ProfileScope(activeProfile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
                _logger.LogInformation("Saved profile changes.");
        OnPropertyChanged(nameof(SaveChangesButtonText));
        NotifyCommandStateChanged();
    }

    public void InitializeRuntimeMonitoring()
    {
        lock (_runtimeMonitoringGate)
        {
            if (_runtimeMonitoringActive)
            {
                return;
            }


            _runtimeMonitoringCts = new CancellationTokenSource();
            _runtimeMonitoringTask = Task.Run(() => RunRuntimeMonitoringLoopAsync(_runtimeMonitoringCts.Token));
            _runtimeMonitoringActive = true;
        }
    }

    public void StopRuntimeMonitoring()
    {
        CancellationTokenSource? cancellationSource;

        lock (_runtimeMonitoringGate)
        {
            if (!_runtimeMonitoringActive)
            {
                return;
            }

            cancellationSource = _runtimeMonitoringCts;
            _runtimeMonitoringCts = null;
            _runtimeMonitoringTask = null;
            _runtimeMonitoringActive = false;
        }

        cancellationSource?.Cancel();
        cancellationSource?.Dispose();
    }

    public void Dispose()
    {
        StopRuntimeMonitoring();
        DiagnosticsSink.Instance.UnregisterHandler(OnSerilogEvent);
        GC.SuppressFinalize(this);
    }

    private async Task RunRuntimeMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await AdoptOrphanMountsAsync(cancellationToken);
            await VerifyStartupProfilesAsync(cancellationToken);
            await RefreshAllRuntimeStatesAsync(cancellationToken);

            while (await _runtimeRefreshWaiter(RuntimeRefreshCadence, cancellationToken))
            {
                await RefreshAllRuntimeStatesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.RuntimeRefresh, ProfileLogStage.Execution))
                    _logger.LogError(ex, "Runtime monitoring loop failed: {ErrorMessage}", ex.Message);
        }
    }

    private async Task AdoptOrphanMountsAsync(CancellationToken cancellationToken)
    {
        var mountProfiles = Profiles
            .Where(p => !p.IsRemoteDefinition && p.EnableRemoteControl && p.RcPort > 0)
            .ToList();

        if (mountProfiles.Count == 0)
        {
            return;
        }

        if (SelectedProfile is { } selectedProfile)
            using (ProfileScope(selectedProfile.Id, ProfileLogCategory.Startup, ProfileLogStage.Initialization))
                _logger.LogInformation("Probing {MountProfileCount} mount profiles for running orphans...", mountProfiles.Count);

        foreach (var profile in mountProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                    _logger.LogInformation("Probing orphan: {ProfileName} (RC port {RcPort}, mount {MountPoint})",
                        profile.Name, profile.RcPort, profile.MountPoint);

                var pid = await _mountManagerService.ProbeRcPidAsync(profile.RcPort, cancellationToken);

                using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                    _logger.LogInformation("RC probe result for {ProfileName}: PID={Pid}", profile.Name, pid?.ToString() ?? "null");

                var isMounted = await _mountManagerService.IsMountedAsync(profile.MountPoint, cancellationToken);

                using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                    _logger.LogInformation("IsMounted result for {ProfileName}: {IsMounted}", profile.Name, isMounted);

                if (pid.HasValue && isMounted)
                {
                    _mountManagerService.AdoptMount(profile.MountPoint, pid.Value, profile.RcPort);
                    using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                        _logger.LogInformation("Adopted running mount (PID {Pid}, RC port {RcPort}).", pid.Value, profile.RcPort);
                }
                else if (pid.HasValue && !isMounted)
                {
                    using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                        _logger.LogWarning("Stale rclone on port {RcPort} (PID {Pid}), sending quit.", profile.RcPort, pid.Value);
                    await _mountManagerService.StopViaRcAsync(profile.RcPort, cancellationToken);
                }
                else if (!pid.HasValue && isMounted)
                {
                    using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                        _logger.LogWarning("Mount point is active but no RC connection. Unmanaged external mount.");
                }
                else
                {
                    using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                        _logger.LogInformation("No orphan found for {ProfileName}: not mounted, no RC.", profile.Name);
                }
            }
            catch (Exception ex)
            {
                using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                    _logger.LogWarning(ex, "Failed to probe orphan mount: {ErrorMessage}", ex.Message);
            }
        }
    }

    private async Task VerifyStartupProfilesAsync(CancellationToken cancellationToken)
    {
        var startupProfiles = Profiles
            .Where(profile => !profile.IsRemoteDefinition && profile.StartAtLogin)
            .ToList();

        if (startupProfiles.Count == 0)
        {
            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                    _logger.LogInformation("Startup runtime verification skipped: no start-at-login profiles.");
            return;
        }

        foreach (var profile in startupProfiles)
        {
            using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Initialization))
                _logger.LogInformation("Startup monitor initialization started.");
        }

        var states = await _runtimeStateBatchVerifier(startupProfiles, cancellationToken);

        await RunOnUiThreadAsync(() =>
        {
            for (var index = 0; index < startupProfiles.Count; index++)
            {
                var profile = startupProfiles[index];
                var state = states[index];
                ApplyRuntimeState(profile, state);
                using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
                    _logger.LogInformation("Startup verification: lifecycle={Lifecycle}, health={Health}", FormatLifecycle(state.Lifecycle), FormatHealth(state.Health));
            }
        });
    }

    private async Task RefreshAllRuntimeStatesAsync(CancellationToken cancellationToken)
    {
        var profilesSnapshot = Profiles.Where(IsMountProfileCandidate).ToList();
        if (profilesSnapshot.Count == 0)
        {
            return;
        }

        var states = await _runtimeStateBatchVerifier(profilesSnapshot, cancellationToken);
        await RunOnUiThreadAsync(() =>
        {
            for (var index = 0; index < profilesSnapshot.Count; index++)
            {
                ApplyRuntimeState(profilesSnapshot[index], states[index]);
            }
        });
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private static async Task<bool> DefaultRuntimeRefreshWaiterAsync(TimeSpan cadence, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(cadence);
        return await timer.WaitForNextTickAsync(cancellationToken);
    }

    private async Task RunBusyActionAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyCommandStateChanged();

        using var cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var statusBeforeAction = StatusText;
            await action(cancellationTokenSource.Token);

            if (string.Equals(StatusText, statusBeforeAction, StringComparison.Ordinal))
            {
                StatusText = "Operation completed.";
            }
        }
        catch (Exception ex)
        {
            StatusText = "Operation failed.";
            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Execution))
                    _logger.LogError(ex, "{ErrorMessage}", ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task RefreshStatusInternalAsync(CancellationToken cancellationToken)
    {
        var profile = SelectedProfile;
        await RefreshRuntimeStateInternalAsync(profile, cancellationToken);
    }

    private async Task RefreshRuntimeStateInternalAsync(MountProfile profile, CancellationToken cancellationToken)
    {
        var state = await _runtimeStateVerifier(profile, cancellationToken);
        ApplyRuntimeState(profile, state);
        using (ProfileScope(profile.Id, ProfileLogCategory.RuntimeRefresh, ProfileLogStage.Completion))
            _logger.LogInformation("Status: {LastStatus}", profile.LastStatus);
    }

    private void ApplyRuntimeState(MountProfile profile, ProfileRuntimeState state)
    {
        profile.RuntimeState = state;
        profile.IsMounted = state.Lifecycle is MountLifecycleState.Mounted;
        profile.IsRunning = state.Lifecycle is MountLifecycleState.Mounted or MountLifecycleState.Mounting;
        profile.LastStatus = BuildStatusText(state);

        if (ReferenceEquals(SelectedProfile, profile))
        {
            StatusText = profile.LastStatus;
            OnPropertyChanged(nameof(SelectedProfileLifecycleText));
            OnPropertyChanged(nameof(SelectedProfileHealthText));
        }
    }

    private static string BuildStatusText(ProfileRuntimeState state)
    {
        var text = $"Lifecycle: {FormatLifecycle(state.Lifecycle)} | Health: {FormatHealth(state.Health)}";
        return string.IsNullOrWhiteSpace(state.ErrorText)
            ? text
            : $"{text} | Detail: {state.ErrorText}";
    }

    private static string FormatLifecycle(MountLifecycleState lifecycle) => lifecycle.ToString().ToLowerInvariant();

    private static string FormatHealth(MountHealthState health) => health.ToString().ToLowerInvariant();

    private IDisposable ProfileScope(string profileId, ProfileLogCategory category, ProfileLogStage stage)
    {
        var profileName = Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase))?.Name ?? profileId;

        return _logger.BeginScope(new Dictionary<string, object>
        {
            ["ProfileId"] = profileId,
            ["ProfileName"] = profileName,
            ["LogCategory"] = category.ToString(),
            ["LogStage"] = stage.ToString(),
        })!;
    }

    private void OnSerilogEvent(LogEvent logEvent)
    {
        var profileId = DiagnosticsSink.ExtractProfileId(logEvent);
        var message = DiagnosticsSink.RenderMessage(logEvent);

        var severity = logEvent.Level switch
        {
            LogEventLevel.Error or LogEventLevel.Fatal => ProfileLogSeverity.Error,
            LogEventLevel.Warning => ProfileLogSeverity.Warning,
            _ => ProfileLogSeverity.Information,
        };

        string? errorText = logEvent.Exception?.Message;
        if (string.IsNullOrWhiteSpace(errorText) && severity is ProfileLogSeverity.Error)
        {
            errorText = message;
        }

        var category = ExtractEnumProperty<ProfileLogCategory>(logEvent, "LogCategory") ?? ProfileLogCategory.General;
        var stage = ExtractEnumProperty<ProfileLogStage>(logEvent, "LogStage") ?? ProfileLogStage.Execution;

        var entry = new ProfileLogEvent(profileId, DateTimeOffset.UtcNow, category, stage, severity, message, errorText);

        lock (_profileLogs)
        {
            if (!_profileLogs.TryGetValue(profileId, out var logEntries))
            {
                logEntries = new List<ProfileLogEvent>();
                _profileLogs[profileId] = logEntries;
            }

            logEntries.Add(entry);
            while (logEntries.Count > MaxProfileLogEntries)
            {
                logEntries.RemoveAt(0);
            }
        }

        if (_testDialogProfileId is not null
            && string.Equals(profileId, _testDialogProfileId, StringComparison.Ordinal))
        {
            void AddLine() => TestDialogLines.Add(message);
            if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
                AddLine();
            else
                Dispatcher.UIThread.Post(AddLine);
        }

        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            RefreshDiagnosticsTimeline();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshDiagnosticsTimeline);
        }
    }

    private static T? ExtractEnumProperty<T>(LogEvent logEvent, string propertyName) where T : struct, Enum
    {
        if (logEvent.Properties.TryGetValue(propertyName, out LogEventPropertyValue? value)
            && value is ScalarValue { Value: string text }
            && Enum.TryParse<T>(text, ignoreCase: true, out T parsed))
        {
            return parsed;
        }

        return null;
    }

    private DiagnosticsTimelineRow ToDiagnosticsRow(ProfileLogEvent entry)
    {
        var timestamp = entry.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var category = entry.Category.ToString().ToLowerInvariant();
        var stage = entry.Stage.ToString().ToLowerInvariant();
        var severity = entry.Severity.ToString().ToLowerInvariant();
        var stageText = $"{category}/{stage}";

        string profileName;
        string profileType;

        if (string.Equals(entry.ProfileId, DiagnosticsSink.SystemProfileId, StringComparison.OrdinalIgnoreCase))
        {
            profileName = "System";
            profileType = "system";
        }
        else
        {
            MountProfile? profile = Profiles.FirstOrDefault(p => string.Equals(p.Id, entry.ProfileId, StringComparison.OrdinalIgnoreCase));
            profileName = profile?.Name ?? entry.ProfileId;
            profileType = profile?.IsRemoteDefinition == true ? "remote" : "mount";
        }

        return new DiagnosticsTimelineRow(entry.ProfileId, profileName, profileType, timestamp, severity, stageText, entry.Message);
    }

    partial void OnSelectedDiagnosticsProfileIdChanged(string? value)
    {
        RefreshDiagnosticsTimeline();
    }

    partial void OnSelectedDiagnosticsProfileFilterOptionChanged(DiagnosticsProfileFilterOption? value)
    {
        SelectedDiagnosticsProfileId = string.IsNullOrEmpty(value?.ProfileId) ? null : value.ProfileId;
    }

    partial void OnStartupTimelineOnlyChanged(bool value)
    {
        RefreshDiagnosticsTimeline();
    }

    partial void OnShowDiagnosticsViewChanged(bool value)
    {
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(WorkspaceSubtitle));
        OnPropertyChanged(nameof(ShowRemoteEditorContent));
        OnPropertyChanged(nameof(ShowWizardContent));
        OnPropertyChanged(nameof(ShowStandardRemoteForm));
        OnPropertyChanged(nameof(ShowMountEditorContent));
        OnPropertyChanged(nameof(ShowSettingsContent));
        OnPropertyChanged(nameof(ShowEditorScrollViewer));
        OnPropertyChanged(nameof(IsRemoteListActive));
        OnPropertyChanged(nameof(IsMountListActive));
        EnsureSingleActiveSidebarSelection();
    }

    partial void OnShowSettingsViewChanged(bool value)
    {
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(WorkspaceSubtitle));
        OnPropertyChanged(nameof(ShowRemoteEditorContent));
        OnPropertyChanged(nameof(ShowWizardContent));
        OnPropertyChanged(nameof(ShowStandardRemoteForm));
        OnPropertyChanged(nameof(ShowMountEditorContent));
        OnPropertyChanged(nameof(ShowSettingsContent));
        OnPropertyChanged(nameof(ShowEditorScrollViewer));
        OnPropertyChanged(nameof(IsRemoteListActive));
        OnPropertyChanged(nameof(IsMountListActive));
        EnsureSingleActiveSidebarSelection();
    }

    partial void OnIsWizardActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWizardContent));
        OnPropertyChanged(nameof(ShowStandardRemoteForm));
    }

    partial void OnCurrentWizardStepChanged(ConfigWizardStep? value)
    {
        OnPropertyChanged(nameof(WizardStepTitle));
        OnPropertyChanged(nameof(WizardStepHelp));
        OnPropertyChanged(nameof(WizardHasExamples));
    }

    partial void OnIsWizardWaitingForOAuthChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWizardOAuthSpinner));
    }

    partial void OnSelectedDiagnosticsCategoryFilterChanged(string value)
    {
        RefreshDiagnosticsTimeline();
    }

    partial void OnDiagnosticsSearchTextChanged(string value)
    {
        RefreshDiagnosticsTimeline();
    }

    partial void OnTestDialogSuccessChanged(bool? value)
    {
        OnPropertyChanged(nameof(IsTestDialogRunning));
        OnPropertyChanged(nameof(ShowTestDialogSuccessIcon));
        OnPropertyChanged(nameof(ShowTestDialogFailureIcon));
    }

    partial void OnSelectedReliabilityPresetIdChanged(string value)
    {
        var resolvedPresetId = ReliabilityPolicyPreset.GetByIdOrDefault(value).Id;
        if (!string.Equals(value, resolvedPresetId, StringComparison.OrdinalIgnoreCase))
        {
            SelectedReliabilityPresetId = resolvedPresetId;
            return;
        }

        if (!string.Equals(SelectedProfile.SelectedReliabilityPresetId, resolvedPresetId, StringComparison.OrdinalIgnoreCase))
        {
            SelectedProfile.SelectedReliabilityPresetId = resolvedPresetId;
        }
    }

    private void SyncDiagnosticsFilters()
    {
        DiagnosticsProfileFilters.Clear();
        DiagnosticsProfileFilters.Add(new DiagnosticsProfileFilterOption(string.Empty, "All", "all"));
        foreach (var profile in Profiles)
        {
            var profileType = profile.IsRemoteDefinition ? "remote" : "mount";
            DiagnosticsProfileFilters.Add(new DiagnosticsProfileFilterOption(profile.Id, profile.Name, profileType));
        }

        if (SelectedDiagnosticsProfileFilterOption is null ||
            !DiagnosticsProfileFilters.Contains(SelectedDiagnosticsProfileFilterOption))
        {
            SelectedDiagnosticsProfileFilterOption = DiagnosticsProfileFilters[0];
        }

        EnsureDiagnosticsFilterSelection(SelectedProfile?.Id);
        RefreshDiagnosticsTimeline();
    }

    private void EnsureDiagnosticsFilterSelection(string? preferredProfileId)
    {
        // Profile filter dropdown is independent of sidebar selection.
        // Only reset if current selection refers to a deleted profile.
        if (SelectedDiagnosticsProfileFilterOption is null)
        {
            SelectedDiagnosticsProfileFilterOption = DiagnosticsProfileFilters.FirstOrDefault();
            return;
        }

        var currentId = SelectedDiagnosticsProfileFilterOption.ProfileId;
        if (!string.IsNullOrEmpty(currentId) &&
            !Profiles.Any(p => string.Equals(p.Id, currentId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedDiagnosticsProfileFilterOption = DiagnosticsProfileFilters.FirstOrDefault();
        }
    }

    private void RefreshDiagnosticsTimeline()
    {
        List<ProfileLogEvent> snapshot;
        lock (_profileLogs)
        {
            snapshot = _profileLogs.Values.SelectMany(entries => entries.ToList()).ToList();
        }

        IEnumerable<ProfileLogEvent> events = snapshot;

        if (string.Equals(SelectedDiagnosticsCategoryFilter, "Remotes", StringComparison.OrdinalIgnoreCase))
        {
            var remoteIds = Profiles.Where(p => p.IsRemoteDefinition).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            events = events.Where(e => remoteIds.Contains(e.ProfileId));
        }
        else if (string.Equals(SelectedDiagnosticsCategoryFilter, "Mounts", StringComparison.OrdinalIgnoreCase))
        {
            var mountIds = Profiles.Where(p => !p.IsRemoteDefinition).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            events = events.Where(e => mountIds.Contains(e.ProfileId));
        }

        if (!string.IsNullOrWhiteSpace(SelectedDiagnosticsProfileId))
        {
            events = events.Where(entry => string.Equals(entry.ProfileId, SelectedDiagnosticsProfileId, StringComparison.OrdinalIgnoreCase));
        }

        if (StartupTimelineOnly)
        {
            events = events.Where(IsStartupTimelineEvent);
        }

        if (!string.IsNullOrWhiteSpace(DiagnosticsSearchText))
        {
            var search = DiagnosticsSearchText;
            events = events.Where(entry => entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var rows = events
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Category)
            .ThenBy(entry => entry.Stage)
            .ThenBy(entry => entry.Message, StringComparer.Ordinal)
            .Select(ToDiagnosticsRow)
            .ToList();

        DiagnosticsRows.Clear();
        Logs.Clear();
        foreach (var row in rows)
        {
            DiagnosticsRows.Add(row);
            Logs.Add(row.DisplayText);
        }
    }

    private static bool IsStartupTimelineEvent(ProfileLogEvent entry)
        => entry.Category is ProfileLogCategory.Startup;

    private void OnDiagnosticsRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasDiagnosticsRows));
        OnPropertyChanged(nameof(DiagnosticsInfoCount));
        OnPropertyChanged(nameof(DiagnosticsWarningCount));
        OnPropertyChanged(nameof(DiagnosticsErrorCount));
    }

    private void RecordStartupPreflightReport(string profileId, StartupPreflightReport report)
    {
        _profileStartupPreflightReports[profileId] = report;

        if (SelectedProfile is not null && string.Equals(SelectedProfile.Id, profileId, StringComparison.OrdinalIgnoreCase))
        {
            StartupPreflightSummary = report.ToSummaryText();
            StartupPreflightReport = report.ToUserFacingMessage();
        }
    }

    private void AppendStartupPreflightChecksToLog(string profileId, StartupPreflightReport report)
    {
        foreach (var check in report.Checks)
        {
            var severity = check.Severity switch
            {
                StartupCheckSeverity.Pass => ProfileLogSeverity.Information,
                StartupCheckSeverity.Warning => ProfileLogSeverity.Warning,
                _ => ProfileLogSeverity.Error,
            };

            using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Verification))
            {
                if (severity is ProfileLogSeverity.Error)
                {
                    _logger.LogError("Startup preflight {CheckSeverity}: [{CheckKey}] {CheckMessage}",
                        check.Severity.ToString().ToLowerInvariant(),
                        check.CheckKey,
                        check.Message);
                }
                else if (severity is ProfileLogSeverity.Warning)
                {
                    _logger.LogWarning("Startup preflight {CheckSeverity}: [{CheckKey}] {CheckMessage}",
                        check.Severity.ToString().ToLowerInvariant(),
                        check.CheckKey,
                        check.Message);
                }
                else
                {
                    _logger.LogInformation("Startup preflight {CheckSeverity}: [{CheckKey}] {CheckMessage}",
                        check.Severity.ToString().ToLowerInvariant(),
                        check.CheckKey,
                        check.Message);
                }
            }
        }
    }

    private void NotifyCommandStateChanged()
    {
        RemoveProfileCommand.NotifyCanExecuteChanged();
        StartMountCommand.NotifyCanExecuteChanged();
        StopMountCommand.NotifyCanExecuteChanged();
        RevealInFileManagerCommand.NotifyCanExecuteChanged();
        RefreshStatusCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        GenerateScriptCommand.NotifyCanExecuteChanged();
        SaveScriptCommand.NotifyCanExecuteChanged();
        ToggleStartupCommand.NotifyCanExecuteChanged();
        CreateRemoteCommand.NotifyCanExecuteChanged();
        StartWizardCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
        RunStartupPreflightCommand.NotifyCanExecuteChanged();
        AddMountCommand.NotifyCanExecuteChanged();
        AddRemoteCommand.NotifyCanExecuteChanged();
        AddProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedThemeModeChanged(string value)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = value switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }

    private bool CanRemoveProfile() => HasProfiles && !IsBusy;

    private static string GetProfileTypeLabel(MountProfile profile) => profile.IsRemoteDefinition ? "remote" : "mount";

    private bool CanRunActions() =>
        HasProfiles &&
        !IsBusy &&
        SelectedProfile is not null &&
        !SelectedProfile.IsRemoteDefinition &&
        HasValidRemoteAssociation(SelectedProfile) &&
        !string.IsNullOrWhiteSpace(SelectedProfile.Source) &&
        !string.IsNullOrWhiteSpace(SelectedProfile.MountPoint);

    private bool CanTestConnection() =>
        HasProfiles &&
        !IsBusy &&
        SelectedProfile is not null &&
        (SelectedProfile.IsRemoteDefinition
            ? SelectedBackend is not null
            : SelectedProfile.Type is MountType.RcloneAuto &&
              HasValidRemoteAssociation(SelectedProfile) &&
              !string.IsNullOrWhiteSpace(SelectedProfile.Source));

    private bool CanSaveScript() => HasProfiles && !IsBusy && !string.IsNullOrWhiteSpace(GeneratedScript);

    private bool CanToggleStartup() => HasProfiles && !IsBusy && SelectedProfile is not null && !SelectedProfile.IsRemoteDefinition && IsStartupSupported;

    private bool CanRunStartupPreflight() => HasProfiles && !IsBusy && SelectedProfile is not null && !SelectedProfile.IsRemoteDefinition && IsStartupSupported;

    private bool CanSaveChanges() => !IsBusy && HasPendingChanges && !HasAnyInvalidMountRemoteAssociations();



    private bool CanCreateRemote() =>
        !IsBusy &&
        HasProfiles &&
        SelectedBackend is not null &&
        !string.IsNullOrWhiteSpace(NewRemoteName) &&
        SelectedProfile is not null &&
        SelectedProfile.IsRemoteDefinition;

    partial void OnSelectedBackendChanged(RcloneBackendInfo? value)
    {
        OnPropertyChanged(nameof(SelectedBackendDescription));

        var previousValues = BackendOptionInputs
            .Concat(AdvancedBackendOptionInputs)
            .Where(o => !string.IsNullOrWhiteSpace(o.Value))
            .ToDictionary(o => o.Name, o => (o.Value, o.IsPassword));

        BackendOptionInputs.Clear();
        AdvancedBackendOptionInputs.Clear();
        if (value is null)
        {
            HasAdvancedBackendOptions = false;
            OnPropertyChanged(nameof(HasBackendOptions));
            OnPropertyChanged(nameof(HasAdvancedBackendOptionInputs));
            NotifyCommandStateChanged();
            return;
        }

        if (!ShowRemoteEditor && string.IsNullOrWhiteSpace(NewRemoteName))
        {
            SetRemoteNameInput($"{value.Name}-remote");
        }
        HasAdvancedBackendOptions = value.Options.Any(o => o.Advanced && !o.Required);

        PopulateBackendOptionInputs(value);

        foreach (var input in BackendOptionInputs.Concat(AdvancedBackendOptionInputs))
        {
            if (previousValues.TryGetValue(input.Name, out var saved))
            {
                input.Value = saved.Value;
                if (input.IsPassword)
                {
                    input.ConfirmValue = saved.Value;
                }
            }
        }

        OnPropertyChanged(nameof(HasBackendOptions));
        OnPropertyChanged(nameof(HasAdvancedBackendOptionInputs));
        NotifyCommandStateChanged();
    }

    partial void OnShowAdvancedBackendOptionsChanged(bool value)
    {
        if (SelectedBackend is null)
        {
            return;
        }

        PopulateBackendOptionInputs(SelectedBackend);
        OnPropertyChanged(nameof(HasBackendOptions));
        OnPropertyChanged(nameof(HasAdvancedBackendOptionInputs));
    }

    private void RestoreBackendSelection(MountProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.BackendName))
        {
            SelectedBackend = null;
            return;
        }

        var backend = AvailableBackends.FirstOrDefault(b =>
            string.Equals(b.Name, profile.BackendName, StringComparison.OrdinalIgnoreCase));

        if (backend is null)
        {
            return;
        }

        SelectedBackend = backend;

        // Restore saved option values into the populated inputs
        if (profile.BackendOptions.Count > 0)
        {
            foreach (var input in BackendOptionInputs.Concat(AdvancedBackendOptionInputs))
            {
                if (profile.BackendOptions.TryGetValue(input.Name, out var savedValue))
                {
                    input.Value = savedValue;
                    if (input.IsPassword)
                    {
                        input.ConfirmValue = savedValue;
                    }
                    if (input.ControlType is Core.Models.OptionControlType.EditableComboBox
                        or Core.Models.OptionControlType.ComboBox)
                    {
                        input.SelectedEnumValue = savedValue;
                    }
                }
            }
        }
    }

    private void PopulateBackendOptionInputs(RcloneBackendInfo backend)
    {
        BackendOptionInputs.Clear();
        AdvancedBackendOptionInputs.Clear();

        foreach (var option in backend.Options
                     .Where(o => o.Required || !o.Advanced)
                     .OrderByDescending(o => o.Required))
        {
            BackendOptionInputs.Add(new RcloneBackendOptionInput(option));
        }

        if (ShowAdvancedBackendOptions)
        {
            foreach (var option in backend.Options
                         .Where(o => o.Advanced && !o.Required))
            {
                AdvancedBackendOptionInputs.Add(new RcloneBackendOptionInput(option));
            }
        }

        OnPropertyChanged(nameof(HasAdvancedBackendOptionInputs));
    }

    partial void OnNewRemoteNameChanged(string value)
    {
        if (_syncingRemoteNameInput)
        {
            NotifyCommandStateChanged();
            return;
        }

        if (SelectedProfile is { IsRemoteDefinition: true } && !string.IsNullOrWhiteSpace(value))
        {
            var remote = SelectedProfile;
            var newAlias = value.Trim();
            var previousAlias = GetRemoteAlias(remote);

            remote.Name = newAlias;
            remote.Source = $"{newAlias}:/";

            if (!string.IsNullOrWhiteSpace(previousAlias) &&
                !string.Equals(previousAlias, newAlias, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var mount in Profiles.Where(IsMountProfileCandidate))
                {
                    if (!TryGetRemoteAliasFromSource(mount.Source, out var mountAlias, out var suffix) ||
                        !string.Equals(mountAlias, previousAlias, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(suffix) || string.Equals(suffix, "/", StringComparison.Ordinal))
                    {
                        mount.Source = $"{newAlias}:/";
                    }
                }
            }
        }

        NotifyCommandStateChanged();
    }

    private void SetRemoteNameInput(string value)
    {
        _syncingRemoteNameInput = true;
        NewRemoteName = value;
        _syncingRemoteNameInput = false;
    }

    partial void OnSelectedProfileChanged(MountProfile value)
    {
        if (IsWizardActive)
        {
            ResetWizardState();
        }

        if (ShowDiagnosticsView)
        {
            ShowDiagnosticsView = false;
        }

        if (ShowSettingsView)
        {
            ShowSettingsView = false;
        }

        if (_observedProfile is not null)
        {
            _observedProfile.PropertyChanged -= OnObservedProfileChanged;
        }

        _observedProfile = value;
        _observedProfile.PropertyChanged += OnObservedProfileChanged;

        NotifyCommandStateChanged();
        NotifyLabelsChanged();

        if (!string.IsNullOrWhiteSpace(value.LastStatus))
        {
            StatusText = value.LastStatus;
        }
        else
        {
            StatusText = BuildStatusText(value.RuntimeState);
        }

        OnPropertyChanged(nameof(SelectedProfileLifecycleText));
        OnPropertyChanged(nameof(SelectedProfileHealthText));

        if (IsStartupSupported)
        {
            value.StartAtLogin = _startupEnabledProbe(value);
        }

        if (_profileStartupPreflightReports.TryGetValue(value.Id, out var preflightReport))
        {
            StartupPreflightSummary = preflightReport.ToSummaryText();
            StartupPreflightReport = preflightReport.ToUserFacingMessage();
        }
        else
        {
            StartupPreflightSummary = "Startup preflight has not been run.";
            StartupPreflightReport = string.Empty;
        }

        EnsureDiagnosticsFilterSelection(preferredProfileId: value.Id);
        RefreshDiagnosticsTimeline();

        if (_profileScripts.TryGetValue(value.Id, out var script))
        {
            GeneratedScript = script;
        }
        else
        {
            GeneratedScript = string.Empty;
        }

        MountOptionsVm.UpdateFromProfile(value.MountOptions, value.PinnedMountOptions);
        SelectedReliabilityPresetId = ReliabilityPolicyPreset.GetByIdOrDefault(value.SelectedReliabilityPresetId).Id;

        _syncingSidebarSelection = true;
        if (value.IsRemoteDefinition)
        {
            SetRemoteNameInput(value.Name);
            RestoreBackendSelection(value);
            SelectedRemoteProfile = value;
            if (!ShowRemoteEditor)
            {
                ShowRemoteEditor = true;
            }
        }
        else
        {
            SelectedMountProfile = value;
            if (ShowRemoteEditor)
            {
                ShowRemoteEditor = false;
            }

            UpdateSelectedMountRemoteFromSource(value);
        }
        _syncingSidebarSelection = false;
        EnsureSingleActiveSidebarSelection();

        OnPropertyChanged(nameof(StartupButtonText));
    }

    partial void OnSelectedRemoteProfileChanged(MountProfile? value)
    {
        OnPropertyChanged(nameof(SidebarSelectedRemoteProfile));

        if (value is not null)
        {
            _rememberedRemoteProfile = value;
        }

        if (_syncingSidebarSelection || value is null)
        {
            return;
        }

        ShowRemoteEditor = true;
        ShowDiagnosticsView = false;
        ShowSettingsView = false;
        if (!ReferenceEquals(SelectedProfile, value))
        {
            SelectedProfile = value;
        }
    }

    partial void OnSelectedMountProfileChanged(MountProfile? value)
    {
        OnPropertyChanged(nameof(SidebarSelectedMountProfile));

        if (value is not null)
        {
            _rememberedMountProfile = value;
        }

        if (_syncingSidebarSelection || value is null)
        {
            return;
        }

        ShowRemoteEditor = false;
        ShowDiagnosticsView = false;
        ShowSettingsView = false;
        if (!ReferenceEquals(SelectedProfile, value))
        {
            SelectedProfile = value;
        }

        UpdateSelectedMountRemoteFromSource(value);
    }

    partial void OnSelectedMountRemoteProfileChanged(MountProfile? value)
    {
        if (_syncingMountRemoteSelection || value is null)
        {
            return;
        }

        var mountProfile = SelectedMountProfile;
        if (mountProfile is null || mountProfile.IsRemoteDefinition)
        {
            return;
        }

        var remoteAlias = GetRemoteAlias(value);
        if (string.IsNullOrWhiteSpace(remoteAlias))
        {
            return;
        }

        var suffix = "/";
        if (TryGetRemoteAliasFromSource(mountProfile.Source, out _, out var existingSuffix) && !string.IsNullOrWhiteSpace(existingSuffix))
        {
            suffix = existingSuffix;
        }

        mountProfile.Source = $"{remoteAlias}:{suffix}";
        OnPropertyChanged(nameof(HasSelectedMountRemote));
        OnPropertyChanged(nameof(MountRemotePath));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SourceHint));
        OnPropertyChanged(nameof(SourceFormatHelp));
        NotifyCommandStateChanged();
    }

    partial void OnShowRemoteEditorChanged(bool value)
    {
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(WorkspaceSubtitle));
        OnPropertyChanged(nameof(ShowRemoteEditorContent));
        OnPropertyChanged(nameof(ShowWizardContent));
        OnPropertyChanged(nameof(ShowStandardRemoteForm));
        OnPropertyChanged(nameof(ShowMountEditorContent));
        OnPropertyChanged(nameof(SidebarSelectedRemoteProfile));
        OnPropertyChanged(nameof(SidebarSelectedMountProfile));

        EnsureSingleActiveSidebarSelection();
    }

    private void OnObservedProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MountProfile.Type) && _observedProfile is not null && _observedProfile.Type is MountType.MacOsNfs)
        {
            _observedProfile.QuickConnectMode = QuickConnectMode.None;
        }

        if (e.PropertyName is nameof(MountProfile.QuickConnectMode) && _observedProfile is not null &&
            _observedProfile.QuickConnectMode is not QuickConnectMode.None && _observedProfile.Type is MountType.MacOsNfs)
        {
            _observedProfile.Type = MountType.RcloneAuto;
        }

        if (e.PropertyName is nameof(MountProfile.Source)
            or nameof(MountProfile.MountPoint)
            or nameof(MountProfile.Type)
            or nameof(MountProfile.QuickConnectMode)
            or nameof(MountProfile.QuickConnectEndpoint)
            or nameof(MountProfile.QuickConnectPort)
            or nameof(MountProfile.QuickConnectUsername)
            or nameof(MountProfile.QuickConnectPassword)
            or nameof(MountProfile.StartAtLogin))
        {
            NotifyCommandStateChanged();
            NotifyLabelsChanged();
            OnPropertyChanged(nameof(StartupButtonText));
        }

        if (e.PropertyName is nameof(MountProfile.RuntimeState) or nameof(MountProfile.LastStatus))
        {
            OnPropertyChanged(nameof(SelectedProfileLifecycleText));
            OnPropertyChanged(nameof(SelectedProfileHealthText));

            if (_observedProfile is not null && !string.IsNullOrWhiteSpace(_observedProfile.LastStatus))
            {
                StatusText = _observedProfile.LastStatus;
            }
        }
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (MountProfile profile in e.OldItems)
            {
                profile.PropertyChanged -= OnAnyProfileChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (MountProfile profile in e.NewItems)
            {
                profile.PropertyChanged += OnAnyProfileChanged;
            }
        }

        if (!_isLoadingProfiles)
        {
            MarkDirty();
        }

        RefreshRemoteProfiles();
        RefreshMountProfiles();
        SyncDiagnosticsFilters();
        OnPropertyChanged(nameof(HasProfiles));
        NotifyCommandStateChanged();
    }

    private void OnAnyProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingProfiles)
        {
            return;
        }

        if (e.PropertyName is nameof(MountProfile.Name)
            or nameof(MountProfile.Type)
            or nameof(MountProfile.Source)
            or nameof(MountProfile.MountPoint)
            or nameof(MountProfile.ExtraOptions)
            or nameof(MountProfile.RcloneBinaryPath)
            or nameof(MountProfile.QuickConnectMode)
            or nameof(MountProfile.QuickConnectEndpoint)
            or nameof(MountProfile.QuickConnectPort)
            or nameof(MountProfile.QuickConnectUsername)
            or nameof(MountProfile.QuickConnectPassword)
            or nameof(MountProfile.AllowInsecurePasswordsInScript)
            or nameof(MountProfile.RcPort)
            or nameof(MountProfile.EnableRemoteControl)
            or nameof(MountProfile.SelectedReliabilityPresetId)
            or nameof(MountProfile.StartAtLogin)
            or nameof(MountProfile.IsRemoteDefinition))
        {
            MarkDirty();

            if (e.PropertyName is nameof(MountProfile.Name))
            {
                SyncDiagnosticsFilters();
            }
        }

        if (e.PropertyName is nameof(MountProfile.Type)
            or nameof(MountProfile.IsRemoteDefinition)
            or nameof(MountProfile.Source)
            or nameof(MountProfile.Name))
        {
            RefreshRemoteProfiles();
            RefreshMountProfiles();
            if (SelectedMountProfile is not null)
            {
                UpdateSelectedMountRemoteFromSource(SelectedMountProfile);
            }
            NotifyCommandStateChanged();
        }
    }

    private void LoadProfiles()
    {
        _isLoadingProfiles = true;

        try
        {
            if (!File.Exists(_profilesFilePath))
            {
                return;
            }

            var json = File.ReadAllText(_profilesFilePath);
            var savedProfiles = JsonSerializer.Deserialize<List<PersistedProfile>>(json);
            if (savedProfiles is null || savedProfiles.Count == 0)
            {
                return;
            }

            Profiles.Clear();
            _profileLogs.Clear();
            _profileScripts.Clear();
            foreach (var saved in savedProfiles)
            {
                var profile = new MountProfile
                {
                    Id = string.IsNullOrWhiteSpace(saved.Id) ? Guid.NewGuid().ToString("N") : saved.Id,
                    Name = saved.Name,
                    Type = saved.Type,
                    Source = saved.Source,
                    MountPoint = saved.MountPoint,
                    ExtraOptions = saved.ExtraOptions,
                    SelectedReliabilityPresetId = ReliabilityPolicyPreset.GetByIdOrDefault(saved.SelectedReliabilityPresetId).Id,
                    MountOptions = saved.MountOptions ?? new Dictionary<string, string>(),
                    PinnedMountOptions = saved.PinnedMountOptions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    RcloneBinaryPath = saved.RcloneBinaryPath,
                    QuickConnectMode = QuickConnectMode.None,
                    QuickConnectEndpoint = string.Empty,
                    QuickConnectPort = string.Empty,
                    QuickConnectUsername = string.Empty,
                    QuickConnectPassword = string.Empty,
                    AllowInsecurePasswordsInScript = saved.AllowInsecurePasswordsInScript,
                    StartAtLogin = saved.StartAtLogin,
                    IsRemoteDefinition = saved.IsRemoteDefinition,
                    BackendName = saved.BackendName,
                    BackendOptions = saved.BackendOptions ?? new Dictionary<string, string>(),
                    RcPort = saved.RcPort,
                    EnableRemoteControl = saved.EnableRemoteControl,
                };

                if (profile.RcPort == 0 && !profile.IsRemoteDefinition)
                {
                    profile.RcPort = MountManagerService.AssignRcPort(profile.Id);
                }

                if (!profile.IsRemoteDefinition && profile.MountOptions.TryGetValue("rc_addr", out var rcAddrValue))
                {
                    if (rcAddrValue.Contains(':'))
                    {
                        var portStr = rcAddrValue.Split(':').Last();
                        if (int.TryParse(portStr, out var port) && port > 0)
                        {
                            profile.RcPort = port;
                        }
                    }

                    profile.MountOptions.Remove("rc");
                    profile.MountOptions.Remove("rc_addr");
                    profile.MountOptions.Remove("rc_no_auth");
                }

                Profiles.Add(profile);
                _profileLogs[profile.Id] = new List<ProfileLogEvent>();
                _profileScripts[profile.Id] = string.Empty;
            }
        }
        catch (Exception ex)
        {
            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Initialization))
                    _logger.LogError(ex, "Could not load profiles: {ErrorMessage}", ex.Message);
        }
        finally
        {
            _isLoadingProfiles = false;
        }
    }

    private void SaveProfiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(_profilesFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SyncMountOptionsToProfile();

            var payload = Profiles
                .Select(profile => new PersistedProfile
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Type = profile.Type,
                    Source = profile.Source,
                    MountPoint = profile.MountPoint,
                    ExtraOptions = profile.ExtraOptions,
                    SelectedReliabilityPresetId = profile.SelectedReliabilityPresetId,
                    MountOptions = profile.MountOptions,
                    PinnedMountOptions = profile.PinnedMountOptions,
                    RcloneBinaryPath = profile.RcloneBinaryPath,
                    QuickConnectMode = profile.QuickConnectMode,
                    QuickConnectEndpoint = profile.QuickConnectEndpoint,
                    QuickConnectPort = profile.QuickConnectPort,
                    QuickConnectUsername = profile.QuickConnectUsername,
                    QuickConnectPassword = profile.QuickConnectPassword,
                    AllowInsecurePasswordsInScript = profile.AllowInsecurePasswordsInScript,
                    StartAtLogin = profile.StartAtLogin,
                    IsRemoteDefinition = profile.IsRemoteDefinition,
                    BackendName = profile.BackendName,
                    BackendOptions = profile.BackendOptions,
                    RcPort = profile.RcPort,
                    EnableRemoteControl = profile.EnableRemoteControl,
                })
                .ToList();

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            File.WriteAllText(_profilesFilePath, json);
        }
        catch (Exception ex)
        {
            if (SelectedProfile is { } selectedProfile)
                using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Execution))
                    _logger.LogError(ex, "Could not save profiles: {ErrorMessage}", ex.Message);
        }
    }

    private void MarkDirty()
    {
        if (_isLoadingProfiles)
        {
            return;
        }

        HasPendingChanges = true;
        OnPropertyChanged(nameof(SaveChangesButtonText));
        NotifyCommandStateChanged();
    }

    private static MountProfile CreateDefaultProfile()
    {
        var id = Guid.NewGuid().ToString("N");
        return new MountProfile
        {
            Id = id,
            Name = "Media remote",
            Type = MountType.RcloneAuto,
            Source = "remote:media",
            MountPoint = DefaultMountPoint("media"),
            ExtraOptions = "--vfs-cache-mode full --dir-cache-time 15m",
            MountOptions = GetDefaultMountOptions(MountType.RcloneAuto),
            QuickConnectMode = QuickConnectMode.None,
            RcPort = MountManagerService.AssignRcPort(id),
        };
    }

    private static Dictionary<string, string> GetDefaultMountOptions(MountType type)
    {
        var port = FindFreePort();
        var rcDefaults = new Dictionary<string, string>
        {
            ["rc"] = "true",
            ["rc_addr"] = $"localhost:{port}",
        };

        return type switch
        {
            MountType.MacOsNfs => rcDefaults,
            _ => new Dictionary<string, string>(rcDefaults)
            {
                ["vfs_cache_mode"] = "full",
                ["dir_cache_time"] = "10m",
            },
        };
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void NotifyLabelsChanged()
    {
        OnPropertyChanged(nameof(HasSelectedMountRemote));
        OnPropertyChanged(nameof(MountRemotePath));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SourceHint));
        OnPropertyChanged(nameof(MountPointHint));
        OnPropertyChanged(nameof(OptionsHint));
        OnPropertyChanged(nameof(SourceFormatHelp));
        OnPropertyChanged(nameof(QuickStartHelp));
        OnPropertyChanged(nameof(CanUseQuickConnect));
        OnPropertyChanged(nameof(ShowQuickConnectSettings));
        OnPropertyChanged(nameof(ShowQuickConnectPort));
        OnPropertyChanged(nameof(QuickConnectModeHelp));
        OnPropertyChanged(nameof(QuickConnectEndpointLabel));
        OnPropertyChanged(nameof(QuickConnectEndpointHint));
        OnPropertyChanged(nameof(IsStartupSupported));
        OnPropertyChanged(nameof(StartupButtonText));
    }

    private void SyncMountOptionsToProfile()
    {
        if (SelectedProfile is not null)
        {
            SelectedProfile.MountOptions = MountOptionsVm.GetNonDefaultValues();
            SelectedProfile.PinnedMountOptions = MountOptionsVm.GetPinnedOptionNames();
        }
    }

    private void ResetQuickConnectFields()
    {
        SelectedProfile.QuickConnectEndpoint = string.Empty;
        SelectedProfile.QuickConnectPort = string.Empty;
        SelectedProfile.QuickConnectUsername = string.Empty;
        SelectedProfile.QuickConnectPassword = string.Empty;
    }

    private static bool IsRemoteProfileCandidate(MountProfile profile) => profile.IsRemoteDefinition;

    private static bool IsMountProfileCandidate(MountProfile profile) => !profile.IsRemoteDefinition;

    private static bool TryGetRemoteAliasFromSource(string source, out string remoteAlias, out string suffix)
    {
        remoteAlias = string.Empty;
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var separator = source.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        remoteAlias = source[..separator].Trim();
        suffix = source[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(remoteAlias);
    }

    private static string? GetRemoteAlias(MountProfile profile)
    {
        if (!TryGetRemoteAliasFromSource(profile.Source, out var remoteAlias, out _))
        {
            return null;
        }

        return remoteAlias;
    }

    private MountProfile? ResolveAssociatedRemote(MountProfile profile)
    {
        if (!TryGetRemoteAliasFromSource(profile.Source, out var remoteAlias, out _))
        {
            return null;
        }

        return RemoteProfiles.FirstOrDefault(remote =>
            string.Equals(GetRemoteAlias(remote), remoteAlias, StringComparison.OrdinalIgnoreCase));
    }

    private bool RequiresRemoteAssociation(MountProfile profile)
        => !profile.IsRemoteDefinition &&
           profile.Type is MountType.RcloneAuto &&
           profile.QuickConnectMode is QuickConnectMode.None;

    private bool HasValidRemoteAssociation(MountProfile profile)
        => !RequiresRemoteAssociation(profile) || ResolveAssociatedRemote(profile) is not null;

    private bool HasAnyInvalidMountRemoteAssociations()
        => Profiles.Where(IsMountProfileCandidate).Any(profile => !HasValidRemoteAssociation(profile));

    private void UpdateSelectedMountRemoteFromSource(MountProfile profile)
    {
        if (profile.IsRemoteDefinition)
        {
            return;
        }

        _syncingMountRemoteSelection = true;
        SelectedMountRemoteProfile = ResolveAssociatedRemote(profile);
        _syncingMountRemoteSelection = false;
    }

    private void EnsureSingleActiveSidebarSelection()
    {
        _syncingSidebarSelection = true;

        try
        {
            if (ShowDiagnosticsView || ShowSettingsView)
            {
                if (SelectedRemoteProfile is not null)
                {
                    _rememberedRemoteProfile = SelectedRemoteProfile;
                    SelectedRemoteProfile = null;
                }

                if (SelectedMountProfile is not null)
                {
                    _rememberedMountProfile = SelectedMountProfile;
                    SelectedMountProfile = null;
                }

                return;
            }

            if (ShowRemoteEditor)
            {
                if (SelectedMountProfile is not null)
                {
                    _rememberedMountProfile = SelectedMountProfile;
                    SelectedMountProfile = null;
                }

                if (SelectedRemoteProfile is null && RemoteProfiles.Count > 0)
                {
                    var candidate = _rememberedRemoteProfile;
                    if (candidate is null || !RemoteProfiles.Contains(candidate))
                    {
                        candidate = RemoteProfiles.FirstOrDefault();
                    }

                    SelectedRemoteProfile = candidate;
                }

                if (SelectedRemoteProfile is null)
                {
                    ShowRemoteEditor = false;
                }
            }
            else
            {
                if (SelectedRemoteProfile is not null)
                {
                    _rememberedRemoteProfile = SelectedRemoteProfile;
                    SelectedRemoteProfile = null;
                }

                if (SelectedMountProfile is null && MountProfiles.Count > 0)
                {
                    var candidate = _rememberedMountProfile;
                    if (candidate is null || !MountProfiles.Contains(candidate))
                    {
                        candidate = MountProfiles.FirstOrDefault();
                    }

                    SelectedMountProfile = candidate;
                }
            }
        }
        finally
        {
            _syncingSidebarSelection = false;
            OnPropertyChanged(nameof(SidebarSelectedRemoteProfile));
            OnPropertyChanged(nameof(SidebarSelectedMountProfile));
        }
    }

    private void RefreshRemoteProfiles()
    {
        var previousRemote = SelectedRemoteProfile ?? _rememberedRemoteProfile;

        RemoteProfiles.Clear();
        foreach (var profile in Profiles.Where(IsRemoteProfileCandidate))
        {
            RemoteProfiles.Add(profile);
        }

        OnPropertyChanged(nameof(HasRemoteProfiles));
        OnPropertyChanged(nameof(HasProfiles));

        if (RemoteProfiles.Count == 0)
        {
            _syncingSidebarSelection = true;
            SelectedRemoteProfile = null;
            _syncingSidebarSelection = false;
            _rememberedRemoteProfile = null;
            ShowRemoteEditor = false;
            if (SelectedMountProfile is not null)
            {
                UpdateSelectedMountRemoteFromSource(SelectedMountProfile);
            }
            return;
        }

        var replacement = previousRemote is not null && RemoteProfiles.Contains(previousRemote)
            ? previousRemote
            : RemoteProfiles.FirstOrDefault(p => ReferenceEquals(p, SelectedProfile)) ?? RemoteProfiles[0];

        _rememberedRemoteProfile = replacement;
        if (ShowRemoteEditor)
        {
            _syncingSidebarSelection = true;
            SelectedRemoteProfile = replacement;
            _syncingSidebarSelection = false;
        }

        if (SelectedMountProfile is not null)
        {
            UpdateSelectedMountRemoteFromSource(SelectedMountProfile);
        }

        EnsureSingleActiveSidebarSelection();
    }

    private void RefreshMountProfiles()
    {
        var previousMount = SelectedMountProfile ?? _rememberedMountProfile;

        MountProfiles.Clear();
        foreach (var profile in Profiles.Where(IsMountProfileCandidate))
        {
            MountProfiles.Add(profile);
        }

        OnPropertyChanged(nameof(HasMountProfiles));
        OnPropertyChanged(nameof(HasProfiles));

        if (MountProfiles.Count == 0)
        {
            _syncingSidebarSelection = true;
            SelectedMountProfile = null;
            _syncingSidebarSelection = false;
            _rememberedMountProfile = null;
            return;
        }

        var replacement = previousMount is not null && MountProfiles.Contains(previousMount)
            ? previousMount
            : MountProfiles.FirstOrDefault(p => ReferenceEquals(p, SelectedProfile)) ?? MountProfiles[0];

        _rememberedMountProfile = replacement;
        if (!ShowRemoteEditor)
        {
            _syncingSidebarSelection = true;
            SelectedMountProfile = replacement;
            _syncingSidebarSelection = false;
        }

        EnsureSingleActiveSidebarSelection();
    }

    private MountProfile CreateRemoteDefinitionProfile(string remoteAlias, string? name = null)
    {
        return new MountProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"{remoteAlias} remote" : name,
            Type = MountType.RcloneAuto,
            Source = $"{remoteAlias}:/",
            MountPoint = DefaultMountPoint($"remote-{remoteAlias}"),
            ExtraOptions = string.Empty,
            MountOptions = GetDefaultMountOptions(MountType.RcloneAuto),
            QuickConnectMode = QuickConnectMode.None,
            IsRemoteDefinition = true,
        };
    }

    private void EnsureRemoteDefinitionsForMountSources()
    {
        var knownAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var remote in Profiles.Where(IsRemoteProfileCandidate))
        {
            var alias = GetRemoteAlias(remote);
            if (!string.IsNullOrWhiteSpace(alias))
            {
                knownAliases.Add(alias);
            }
        }

        var missingAliases = Profiles
            .Where(IsMountProfileCandidate)
            .Select(GetRemoteAlias)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Cast<string>()
            .Where(alias => !knownAliases.Contains(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var alias in missingAliases)
        {
            var remoteProfile = CreateRemoteDefinitionProfile(alias);
            Profiles.Add(remoteProfile);
            _profileLogs[remoteProfile.Id] = new List<ProfileLogEvent>();
            _profileScripts[remoteProfile.Id] = string.Empty;
            knownAliases.Add(alias);
        }
    }

    private static string DefaultMountPoint(string name)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Mounts", name);
    }

    public sealed record DiagnosticsProfileFilterOption(string ProfileId, string DisplayName, string ProfileType)
    {
        public Geometry? TypeIconData => ProfileType switch
        {
            "remote" => Application.Current?.FindResource("MdiCloudOutline") as Geometry,
            "system" => Application.Current?.FindResource("MdiCogOutline") as Geometry,
            "all" => null,
            _ => Application.Current?.FindResource("MdiFolderOutline") as Geometry,
        };

        public IBrush TypeIconBrush => ProfileType switch
        {
            "remote" => new SolidColorBrush(Color.Parse("#4CAF50")),
            "system" => new SolidColorBrush(Color.Parse("#9E9E9E")),
            _ => new SolidColorBrush(Color.Parse("#2196F3")),
        };

        public bool HasTypeIcon => TypeIconData is not null;
    }

    public sealed record DiagnosticsTimelineRow(
        string ProfileId,
        string ProfileName,
        string ProfileType,
        string TimestampText,
        string SeverityText,
        string StageText,
        string MessageText)
    {
        private static readonly IBrush MountBrush = new SolidColorBrush(Color.Parse("#2196F3"));
        private static readonly IBrush RemoteBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
        private static readonly IBrush SystemBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));

        public string DisplayText => $"[{TimestampText}] [{ProfileName}] [{StageText}] [{SeverityText}] {MessageText}";

        private static Geometry? ResolveIconGeometry(string resourceKey)
        {
            if (Application.Current?.TryFindResource(resourceKey, ThemeVariant.Default, out object? resource) == true)
            {
                return resource as Geometry;
            }

            return null;
        }

        public Geometry? TypeIconData => ProfileType switch
        {
            "remote" => ResolveIconGeometry("MdiCloudOutline"),
            "system" => ResolveIconGeometry("MdiCogOutline"),
            _ => ResolveIconGeometry("MdiFolderOutline"),
        };

        public IBrush TypeIconBrush => ProfileType switch
        {
            "remote" => RemoteBrush,
            "system" => SystemBrush,
            _ => MountBrush,
        };
    }

    private sealed class PersistedProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Profile";
        public MountType Type { get; set; } = MountType.RcloneAuto;
        public string Source { get; set; } = string.Empty;
        public string MountPoint { get; set; } = string.Empty;
        public string ExtraOptions { get; set; } = string.Empty;
        public string SelectedReliabilityPresetId { get; set; } = ReliabilityPolicyPreset.NormalId;
        public Dictionary<string, string> MountOptions { get; set; } = new();
        public HashSet<string> PinnedMountOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string RcloneBinaryPath { get; set; } = "rclone";
        public QuickConnectMode QuickConnectMode { get; set; } = QuickConnectMode.None;
        public string QuickConnectEndpoint { get; set; } = string.Empty;
        public string QuickConnectPort { get; set; } = string.Empty;
        public string QuickConnectUsername { get; set; } = string.Empty;
        public string QuickConnectPassword { get; set; } = string.Empty;
        public bool AllowInsecurePasswordsInScript { get; set; }
        public bool StartAtLogin { get; set; }
        public bool IsRemoteDefinition { get; set; }
        public string BackendName { get; set; } = string.Empty;
        public Dictionary<string, string> BackendOptions { get; set; } = new();
        public int RcPort { get; set; }
        public bool EnableRemoteControl { get; set; } = true;
    }
}
