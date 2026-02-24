using Avalonia;
using Avalonia.Threading;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;
using Serilog;
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
    private readonly StartupPreflightService _startupPreflightService;
    private readonly MountHealthService _mountHealthService;
    private readonly Func<MountProfile, CancellationToken, Task<StartupPreflightReport>> _startupPreflightRunner;
    private readonly Func<MountProfile, Action<string>, CancellationToken, Task> _mountStartRunner;
    private readonly Func<MountProfile, Action<string>, CancellationToken, Task> _mountStopRunner;
    private readonly Func<MountProfile, CancellationToken, Task<bool>> _mountedProbe;
    private readonly Func<MountProfile, CancellationToken, Task<ProfileRuntimeState>> _runtimeStateVerifier;
    private readonly Func<MountProfile, string, Action<string>, CancellationToken, Task> _startupEnableRunner;
    private readonly Func<MountProfile, Action<string>, CancellationToken, Task> _startupDisableRunner;
    private readonly Func<MountProfile, bool> _startupEnabledProbe;
    private readonly Func<TimeSpan, CancellationToken, Task<bool>> _runtimeRefreshWaiter;
    private readonly Func<IEnumerable<MountProfile>, CancellationToken, Task<IReadOnlyList<ProfileRuntimeState>>> _runtimeStateBatchVerifier;
    private readonly Func<MountProfile, Action<string>, CancellationToken, Task>? _testConnectionRunner;
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
    private bool _startupTimelineOnly;

    [ObservableProperty]
    private bool _showDiagnosticsView;

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
        get => ShowRemoteEditor ? SelectedRemoteProfile : null;
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
        get => ShowRemoteEditor ? null : SelectedMountProfile;
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
    public string WorkspaceTitle => ShowDiagnosticsView
        ? "Diagnostics"
        : ShowRemoteEditor ? "Remote Assistant" : "Mount Assistant";
    public string WorkspaceSubtitle => ShowDiagnosticsView
        ? "View log entries across all profiles"
        : ShowRemoteEditor
            ? "Choose backend -> set options -> create remote"
            : "Preset -> credentials -> mount path -> Start mount";
    public bool ShowRemoteEditorContent => ShowRemoteEditor && !ShowDiagnosticsView;
    public bool ShowMountEditorContent => !ShowRemoteEditor && !ShowDiagnosticsView;
    public string DiagnosticsInfoCount => DiagnosticsRows.Count(r => string.Equals(r.SeverityText, "information", StringComparison.OrdinalIgnoreCase)).ToString();
    public string DiagnosticsWarningCount => DiagnosticsRows.Count(r => string.Equals(r.SeverityText, "warning", StringComparison.OrdinalIgnoreCase)).ToString();
    public string DiagnosticsErrorCount => DiagnosticsRows.Count(r => string.Equals(r.SeverityText, "error", StringComparison.OrdinalIgnoreCase)).ToString();

    public MainWindowViewModel(
        string? profilesFilePath = null,
        MountManagerService? mountManagerService = null,
        LaunchAgentService? launchAgentService = null,
        RcloneBackendService? rcloneBackendService = null,
        StartupPreflightService? startupPreflightService = null,
        MountHealthService? mountHealthService = null,
        Func<MountProfile, Action<string>, CancellationToken, Task>? mountStartRunner = null,
        Func<MountProfile, Action<string>, CancellationToken, Task>? mountStopRunner = null,
        Func<MountProfile, Action<string>, CancellationToken, Task>? testConnectionRunner = null,
        Func<MountProfile, CancellationToken, Task<bool>>? mountedProbe = null,
        Func<MountProfile, CancellationToken, Task<ProfileRuntimeState>>? runtimeStateVerifier = null,
        Func<MountProfile, CancellationToken, Task<StartupPreflightReport>>? startupPreflightRunner = null,
        Func<MountProfile, string, Action<string>, CancellationToken, Task>? startupEnableRunner = null,
        Func<MountProfile, Action<string>, CancellationToken, Task>? startupDisableRunner = null,
        Func<MountProfile, bool>? startupEnabledProbe = null,
        Func<TimeSpan, CancellationToken, Task<bool>>? runtimeRefreshWaiter = null,
        Func<IEnumerable<MountProfile>, CancellationToken, Task<IReadOnlyList<ProfileRuntimeState>>>? runtimeStateBatchVerifier = null,
        bool loadStartupData = true)
    {
        _mountManagerService = mountManagerService ?? new MountManagerService();
        _launchAgentService = launchAgentService ?? new LaunchAgentService();
        _rcloneBackendService = rcloneBackendService ?? new RcloneBackendService();
        _startupPreflightService = startupPreflightService ?? new StartupPreflightService();
        _mountHealthService = mountHealthService ?? new MountHealthService();
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
        AppendLog(ProfileLogCategory.General, ProfileLogStage.Initialization, $"Profiles file: {_profilesFilePath}");

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
                AppendLog(ProfileLogCategory.General, ProfileLogStage.Initialization, $"Could not load mount options: {ex.Message}", ProfileLogSeverity.Error, ex.Message);
            }
        });
    }

    public string SourceLabel => SelectedProfile?.Type switch
    {
        MountType.MacOsNfs => "NFS export",
        _ => "Source",
    };

    public string SourceHint => SelectedProfile?.Type is MountType.MacOsNfs
        ? "Example: 192.168.1.10:/volume1/media"
        : "Example: remote:media";

    public string MountPointHint => $"Example: {DefaultMountPoint("media")}";

    public string OptionsHint => SelectedProfile?.Type is MountType.MacOsNfs
        ? "Example: nfsvers=4,resvport"
        : "Example: --vfs-cache-mode full --dir-cache-time 15m";

    public string SourceFormatHelp => SelectedProfile?.Type is MountType.MacOsNfs
        ? "NFS uses host + export path directly."
        : "For rclone use remote:path (create remote first or use the backend builder).";

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
        var profile = new MountProfile
        {
            Name = $"Mount {MountProfiles.Count + 1}",
            Type = MountType.RcloneAuto,
            Source = string.Empty,
            MountPoint = DefaultMountPoint("new-mount"),
            ExtraOptions = "--vfs-cache-mode full",
            MountOptions = GetDefaultMountOptions(MountType.RcloneAuto),
            QuickConnectMode = QuickConnectMode.None,
            IsRemoteDefinition = false,
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
        ShowDiagnosticsView = true;
        RefreshDiagnosticsTimeline();
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

            AppendLog(ProfileLogCategory.General, ProfileLogStage.Completion, $"Loaded {AvailableBackends.Count} rclone backend types.");
        }
        catch (Exception ex)
        {
            AppendLog(ProfileLogCategory.General, ProfileLogStage.Initialization, $"Could not load backend list: {ex.Message}", ProfileLogSeverity.Error, ex.Message);
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

            AppendLog(ProfileLogCategory.General, ProfileLogStage.Completion, $"Created remote '{NewRemoteName}' ({SelectedBackend.Name}).");
            StatusText = $"Remote '{NewRemoteName}' created.";
            MarkDirty();
            SaveProfiles();
            HasPendingChanges = false;
            StatusText = $"Remote '{NewRemoteName}' saved.";
        });
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
            AppendLog(profileId, ProfileLogCategory.ManualStart, ProfileLogStage.Initialization, $"Starting mount '{profile.Name}'...");
            ApplyRuntimeState(profile, new ProfileRuntimeState(MountLifecycleState.Mounting, MountHealthState.Unknown, DateTimeOffset.UtcNow, null));

            try
            {
                await _mountStartRunner(profile, line => AppendLog(profileId, ProfileLogCategory.ManualStart, ProfileLogStage.Execution, line), cancellationToken);
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
            AppendLog(profileId, ProfileLogCategory.ManualStop, ProfileLogStage.Initialization, $"Stopping mount '{profile.Name}'...");

            try
            {
                await _mountStopRunner(profile, line => AppendLog(profileId, ProfileLogCategory.ManualStop, ProfileLogStage.Execution, line), cancellationToken);

                if (!await _mountedProbe(profile, cancellationToken))
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
            AppendLog(profileId, ProfileLogCategory.General, ProfileLogStage.Initialization, $"Testing connection for '{profile.Name}'...");

            void LogLine(string line)
            {
                AppendLog(profileId, ProfileLogCategory.General, ProfileLogStage.Execution, line);
                TestDialogLines.Add(line);
            }

            if (_testConnectionRunner is not null)
            {
                await _testConnectionRunner(profile, LogLine, CancellationToken.None);
            }
            else if (profile.IsRemoteDefinition && SelectedBackend is not null)
            {
                var binary = profile.RcloneBinaryPath ?? "rclone";
                await _mountManagerService.TestBackendConnectionAsync(
                    binary,
                    SelectedBackend.Name,
                    BackendOptionInputs.Concat(AdvancedBackendOptionInputs),
                    LogLine,
                    CancellationToken.None);
            }
            else
            {
                await _mountManagerService.TestConnectionAsync(profile, LogLine, CancellationToken.None);
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
            AppendLog(ProfileLogCategory.General, ProfileLogStage.Execution, ex.Message, ProfileLogSeverity.Error, ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private void GenerateScript()
    {
        SyncMountOptionsToProfile();
        GeneratedScript = _mountManagerService.GenerateScript(SelectedProfile);
        _profileScripts[SelectedProfile.Id] = GeneratedScript;
        AppendLog(ProfileLogCategory.General, ProfileLogStage.Completion, "Generated shell script preview.");
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
        AppendLog(ProfileLogCategory.General, ProfileLogStage.Completion, $"Applied reliability preset '{preset.DisplayName}' to profile '{profile.Name}'.");
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

        AppendLog(ProfileLogCategory.General, ProfileLogStage.Completion, $"Script saved to: {scriptPath}");
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
                await _startupDisableRunner(profile, line => AppendLog(profileId, ProfileLogCategory.Startup, ProfileLogStage.Execution, line), cancellationToken);
                profile.StartAtLogin = false;
                SaveProfiles();
                HasPendingChanges = false;
                completionStatus = "Start at login disabled and saved.";
                AppendLog(profileId, ProfileLogCategory.Startup, ProfileLogStage.Completion, "Persisted startup preference after disable.");
            }
            else
            {
                var report = await _startupPreflightRunner(profile, cancellationToken);
                RecordStartupPreflightReport(profileId, report);
                AppendStartupPreflightChecksToLog(profileId, report);

                if (!report.CriticalChecksPassed)
                {
                    completionStatus = "Start at login blocked: startup preflight failed.";
                    AppendLog(profileId, ProfileLogCategory.Startup, ProfileLogStage.Verification, "Startup enable blocked by critical preflight failures.", ProfileLogSeverity.Warning);
                    return;
                }

                var script = _mountManagerService.GenerateScript(profile);
                await _startupEnableRunner(profile, script, line => AppendLog(profileId, ProfileLogCategory.Startup, ProfileLogStage.Execution, line), cancellationToken);
                profile.StartAtLogin = true;
                SaveProfiles();
                HasPendingChanges = false;
                completionStatus = "Start at login enabled and saved.";
                AppendLog(profileId, ProfileLogCategory.Startup, ProfileLogStage.Completion, "Persisted startup preference after enable.");
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
            var report = await _startupPreflightRunner(profile, cancellationToken);
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
            AppendLog(ProfileLogCategory.General, ProfileLogStage.Verification, "Save blocked: one or more mounts have no associated remote.", ProfileLogSeverity.Warning);
            return;
        }

        SaveProfiles();
        HasPendingChanges = false;
        StatusText = "Profile changes saved.";
        AppendLog(ProfileLogCategory.General, ProfileLogStage.Completion, "Saved profile changes.");
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
        GC.SuppressFinalize(this);
    }

    private async Task RunRuntimeMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await VerifyStartupProfilesAsync(cancellationToken);

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
            AppendLog(ProfileLogCategory.RuntimeRefresh, ProfileLogStage.Execution, $"Runtime monitoring loop failed: {ex.Message}", ProfileLogSeverity.Error, ex.Message);
        }
    }

    private async Task VerifyStartupProfilesAsync(CancellationToken cancellationToken)
    {
        var startupProfiles = Profiles
            .Where(profile => !profile.IsRemoteDefinition && profile.StartAtLogin)
            .ToList();

        if (startupProfiles.Count == 0)
        {
            AppendLog(ProfileLogCategory.Startup, ProfileLogStage.Verification, "Startup runtime verification skipped: no start-at-login profiles.");
            return;
        }

        foreach (var profile in startupProfiles)
        {
            AppendLog(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Initialization, "Startup monitor initialization started.");
        }

        var states = await _runtimeStateBatchVerifier(startupProfiles, cancellationToken);

        await RunOnUiThreadAsync(() =>
        {
            for (var index = 0; index < startupProfiles.Count; index++)
            {
                var profile = startupProfiles[index];
                var state = states[index];
                ApplyRuntimeState(profile, state);
                AppendLog(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification, $"Startup verification: lifecycle={FormatLifecycle(state.Lifecycle)}, health={FormatHealth(state.Health)}");
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
            AppendLog(ProfileLogCategory.General, ProfileLogStage.Execution, ex.Message, ProfileLogSeverity.Error, ex.Message);
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
        AppendLog(profile.Id, ProfileLogCategory.RuntimeRefresh, ProfileLogStage.Completion, $"Status for '{profile.Name}': {profile.LastStatus}");
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

    private void AppendLog(ProfileLogCategory category, ProfileLogStage stage, string message, ProfileLogSeverity? severity = null, string? error = null)
    {
        var profileId = SelectedProfile?.Id;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        AppendLog(profileId, category, stage, message, severity, error);
    }

    private void AppendLog(string profileId, string message)
    {
        AppendLog(profileId, ProfileLogCategory.General, ProfileLogStage.Execution, message);
    }

    private void AppendLog(string profileId, ProfileLogCategory category, ProfileLogStage stage, string message, ProfileLogSeverity? severity = null, string? error = null)
    {
        var resolvedSeverity = severity ?? ResolveSeverity(message);
        var resolvedError = string.IsNullOrWhiteSpace(error) && resolvedSeverity is ProfileLogSeverity.Error
            ? message
            : error;

        if (resolvedSeverity is ProfileLogSeverity.Error)
        {
            Log.Error("{Message}", message);
        }
        else if (resolvedSeverity is ProfileLogSeverity.Warning)
        {
            Log.Warning("{Message}", message);
        }
        else
        {
            Log.Information("{Message}", message);
        }

        var entry = new ProfileLogEvent(profileId, DateTimeOffset.UtcNow, category, stage, resolvedSeverity, message, resolvedError);

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

        RefreshDiagnosticsTimeline();
    }

    private static ProfileLogSeverity ResolveSeverity(string message)
    {
        if (message.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileLogSeverity.Error;
        }

        if (message.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileLogSeverity.Warning;
        }

        return ProfileLogSeverity.Information;
    }

    private DiagnosticsTimelineRow ToDiagnosticsRow(ProfileLogEvent entry)
    {
        var timestamp = entry.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var category = entry.Category.ToString().ToLowerInvariant();
        var stage = entry.Stage.ToString().ToLowerInvariant();
        var severity = entry.Severity.ToString().ToLowerInvariant();
        var stageText = $"{category}/{stage}";
        var profileName = Profiles.FirstOrDefault(p => string.Equals(p.Id, entry.ProfileId, StringComparison.OrdinalIgnoreCase))?.Name ?? entry.ProfileId;
        return new DiagnosticsTimelineRow(entry.ProfileId, profileName, timestamp, severity, stageText, entry.Message);
    }

    partial void OnSelectedDiagnosticsProfileIdChanged(string? value)
    {
        RefreshDiagnosticsTimeline();
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
        OnPropertyChanged(nameof(ShowMountEditorContent));
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
        foreach (var profile in Profiles)
        {
            DiagnosticsProfileFilters.Add(new DiagnosticsProfileFilterOption(profile.Id, profile.Name));
        }

        EnsureDiagnosticsFilterSelection(SelectedProfile?.Id);
        RefreshDiagnosticsTimeline();
    }

    private void EnsureDiagnosticsFilterSelection(string? preferredProfileId)
    {
        if (Profiles.Count == 0)
        {
            if (SelectedDiagnosticsProfileId is not null)
            {
                SelectedDiagnosticsProfileId = null;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedDiagnosticsProfileId) &&
            Profiles.Any(profile => string.Equals(profile.Id, SelectedDiagnosticsProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var fallbackProfileId = !string.IsNullOrWhiteSpace(preferredProfileId) &&
            Profiles.Any(profile => string.Equals(profile.Id, preferredProfileId, StringComparison.OrdinalIgnoreCase))
                ? preferredProfileId
                : Profiles[0].Id;

        if (!string.Equals(SelectedDiagnosticsProfileId, fallbackProfileId, StringComparison.OrdinalIgnoreCase))
        {
            SelectedDiagnosticsProfileId = fallbackProfileId;
        }
    }

    private void RefreshDiagnosticsTimeline()
    {
        IEnumerable<ProfileLogEvent> events = _profileLogs.Values.SelectMany(entries => entries);

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

            AppendLog(profileId, ProfileLogCategory.Startup, ProfileLogStage.Verification, $"Startup preflight {check.Severity.ToString().ToLowerInvariant()}: [{check.CheckKey}] {check.Message}", severity);
        }
    }

    private void NotifyCommandStateChanged()
    {
        RemoveProfileCommand.NotifyCanExecuteChanged();
        StartMountCommand.NotifyCanExecuteChanged();
        StopMountCommand.NotifyCanExecuteChanged();
        RefreshStatusCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        GenerateScriptCommand.NotifyCanExecuteChanged();
        SaveScriptCommand.NotifyCanExecuteChanged();
        ToggleStartupCommand.NotifyCanExecuteChanged();
        CreateRemoteCommand.NotifyCanExecuteChanged();
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
                     .OrderByDescending(o => o.Required)
                     .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            BackendOptionInputs.Add(new RcloneBackendOptionInput(option));
        }

        if (ShowAdvancedBackendOptions)
        {
            foreach (var option in backend.Options
                         .Where(o => o.Advanced && !o.Required)
                         .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
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
        if (ShowDiagnosticsView)
        {
            ShowDiagnosticsView = false;
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
        NotifyCommandStateChanged();
    }

    partial void OnShowRemoteEditorChanged(bool value)
    {
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(WorkspaceSubtitle));
        OnPropertyChanged(nameof(ShowRemoteEditorContent));
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
                };

                Profiles.Add(profile);
                _profileLogs[profile.Id] = new List<ProfileLogEvent>();
                _profileScripts[profile.Id] = string.Empty;
            }
        }
        catch (Exception ex)
        {
            AppendLog(ProfileLogCategory.General, ProfileLogStage.Initialization, $"Could not load profiles: {ex.Message}", ProfileLogSeverity.Error, ex.Message);
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
            AppendLog(ProfileLogCategory.General, ProfileLogStage.Execution, $"Could not save profiles: {ex.Message}", ProfileLogSeverity.Error, ex.Message);
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
        return new MountProfile
        {
            Name = "Media remote",
            Type = MountType.RcloneAuto,
            Source = "remote:media",
            MountPoint = DefaultMountPoint("media"),
            ExtraOptions = "--vfs-cache-mode full --dir-cache-time 15m",
            MountOptions = GetDefaultMountOptions(MountType.RcloneAuto),
            QuickConnectMode = QuickConnectMode.None,
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

    public sealed record DiagnosticsProfileFilterOption(string ProfileId, string DisplayName);

    public sealed record DiagnosticsTimelineRow(
        string ProfileId,
        string ProfileName,
        string TimestampText,
        string SeverityText,
        string StageText,
        string MessageText)
    {
        public string DisplayText => $"[{TimestampText}] [{ProfileName}] [{StageText}] [{SeverityText}] {MessageText}";
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
    }
}
