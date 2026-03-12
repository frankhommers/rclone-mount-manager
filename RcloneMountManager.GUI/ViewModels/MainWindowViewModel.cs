using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;
using RcloneMountManager.GUI.Services;
using Serilog.Events;

namespace RcloneMountManager.GUI.ViewModels;

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

  private readonly Func<IEnumerable<MountProfile>, CancellationToken, Task<IReadOnlyList<ProfileRuntimeState>>>
    _runtimeStateBatchVerifier;

  private readonly Func<MountProfile, string, CancellationToken, Task<bool>> _sourceRemoteExistsInRcloneConfigRunner;
  private readonly Func<MountProfile, CancellationToken, Task> _repairMissingSourceRemoteRunner;
  private readonly Func<MountProfile, CancellationToken, Task>? _testConnectionRunner;
  private readonly bool _isStartupSupported;
  public MountOptionsViewModel MountOptionsVm { get; } = new();
  private readonly string _profilesFilePath;
  private readonly Dictionary<string, List<ProfileLogEvent>> _profileLogs = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, string> _profileScripts = new(StringComparer.OrdinalIgnoreCase);

  private readonly Dictionary<string, StartupPreflightReport> _profileStartupPreflightReports =
    new(StringComparer.OrdinalIgnoreCase);

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

  [ObservableProperty] private MountProfile _selectedProfile = new();

  [ObservableProperty] private bool _isBusy;

  [ObservableProperty] private string _generatedScript = string.Empty;

  [ObservableProperty] private string _statusText = "Ready";

  [ObservableProperty] private string _selectedThemeMode = "Follow system";

  [ObservableProperty] private WindowCloseBehavior _selectedWindowCloseBehavior = WindowCloseBehavior.Quit;

  [ObservableProperty] private RcloneBackendInfo? _selectedBackend;

  [ObservableProperty] private string _newRemoteName = string.Empty;

  [ObservableProperty] private bool _isWizardActive;

  [ObservableProperty] private bool _isManualMode;

  [ObservableProperty] private ConfigWizardStep? _currentWizardStep;

  [ObservableProperty] private string _wizardAnswer = string.Empty;

  [ObservableProperty] private WizardStepOptionInput? _wizardStepInput;

  [ObservableProperty] private bool _wizardStepBoolValue;

  [ObservableProperty] private ConfigWizardExample? _wizardSelectedExample;

  [ObservableProperty] private bool _isWizardWaitingForOAuth;

  [ObservableProperty] private string _wizardOAuthUrl = string.Empty;

  [ObservableProperty] private int _wizardStepNumber;

  [ObservableProperty] private bool _showAdvancedBackendOptions;

  [ObservableProperty] private bool _hasAdvancedBackendOptions;

  [ObservableProperty] private bool _hasPendingChanges;

  [ObservableProperty] private string _startupPreflightSummary = "Startup preflight has not been run.";

  [ObservableProperty] private string _startupPreflightReport = string.Empty;

  [ObservableProperty] private string? _selectedDiagnosticsProfileId;

  [ObservableProperty] private DiagnosticsProfileFilterOption? _selectedDiagnosticsProfileFilterOption;

  [ObservableProperty] private bool _startupTimelineOnly;

  [ObservableProperty] private bool _showDiagnosticsView;

  [ObservableProperty] private bool _showSettingsView;

  [ObservableProperty] private bool _showDashboard = true;

  [ObservableProperty] private bool _isConfigurationMode;

  [ObservableProperty] private bool _isSourceRemoteMissingFromRcloneConfig;

  [ObservableProperty] private bool _canRepairMissingSourceRemote;

  [ObservableProperty] private string _sourceRemoteConfigStatus = string.Empty;

  [ObservableProperty] private string _selectedDiagnosticsCategoryFilter = "All";

  [ObservableProperty] private string _diagnosticsSearchText = string.Empty;

  [ObservableProperty] private string _selectedReliabilityPresetId = ReliabilityPolicyPreset.NormalId;

  [ObservableProperty] private MountProfile? _selectedRemoteProfile;

  [ObservableProperty] private MountProfile? _selectedMountProfile;

  [ObservableProperty] private bool _showRemoteEditor;

  [ObservableProperty] private bool _isDeleteBlockedDialogVisible;

  [ObservableProperty] private string _deleteBlockedDialogMessage = string.Empty;

  [ObservableProperty] private bool _isTestDialogVisible;

  [ObservableProperty] private string _testDialogTitle = string.Empty;

  [ObservableProperty] private bool? _testDialogSuccess;

  private string? _testDialogProfileId;
  private string? _wizardState;

  [ObservableProperty] private MountProfile? _selectedMountRemoteProfile;

  public ObservableCollection<MountProfile> Profiles { get; } = new();
  public ObservableCollection<MountProfile> MountProfiles { get; } = new();
  public ObservableCollection<DashboardMountCardViewModel> DashboardMountCards { get; } = new();
  public ObservableCollection<MountType> MountTypes { get; } = new(Enum.GetValues<MountType>());
  public ObservableCollection<QuickConnectMode> QuickConnectModes { get; } = new(Enum.GetValues<QuickConnectMode>());
  public ObservableCollection<string> ThemeModes { get; } = new() {"Follow system", "Dark", "Light"};
  public ObservableCollection<WindowCloseBehavior> WindowCloseBehaviorModes { get; } = new(Enum.GetValues<WindowCloseBehavior>());
  public ObservableCollection<string> DiagnosticsCategoryFilters { get; } = new() {"All", "Remotes", "Mounts"};
  public ObservableCollection<DiagnosticsProfileFilterOption> DiagnosticsProfileFilters { get; } = new();
  public ObservableCollection<string> Logs { get; } = new();
  public ObservableCollection<DiagnosticsTimelineRow> DiagnosticsRows { get; } = new();
  public ObservableCollection<string> TestDialogLines { get; } = new();
  public ObservableCollection<RcloneBackendInfo> AvailableBackends { get; } = new();
  public ObservableCollection<RcloneBackendOptionInput> BackendOptionInputs { get; } = new();
  public ObservableCollection<RcloneBackendOptionInput> AdvancedBackendOptionInputs { get; } = new();

  public ObservableCollection<ReliabilityPolicyPreset> ReliabilityPresets { get; } =
    new(ReliabilityPolicyPreset.Catalog);

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

  public string WorkspaceTitle =>
    ShowDashboard ? "Overview" :
    ShowDiagnosticsView ? "Diagnostics" :
    ShowSettingsView ? "Preferences" :
    IsConfigurationMode ? SelectedProfile?.Name ?? "Configuration" :
    SelectedProfile?.Name ?? "Mount";

  public string WorkspaceSubtitle =>
    ShowDashboard ? "Your mounts at a glance" :
    ShowDiagnosticsView ? "Application logs and events" :
    ShowSettingsView ? "Application settings" :
    IsConfigurationMode ? "Configuration" :
    ShowRemoteEditor ? "Remote configuration" :
    "Mount profile";

  public bool ShowRemoteEditorContent => ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView;
  public bool ShowWizardContent => IsWizardActive && ShowRemoteEditorContent;
  public bool ShowManualRemoteForm => IsManualMode && ShowRemoteEditorContent;
  public bool ShowRemoteChooser => !IsWizardActive && !IsManualMode && ShowRemoteEditorContent;
  public bool ShowStandardRemoteForm => !IsWizardActive && ShowRemoteEditorContent;
  public bool ShowWizardOAuthSpinner => IsWizardWaitingForOAuth;
  public string WizardStepTitle => CurrentWizardStep?.Name ?? string.Empty;
  public string WizardStepHelp => CurrentWizardStep?.Help?.Replace("\n", " ").Trim() ?? string.Empty;
  public bool WizardHasExamples => CurrentWizardStep is {Examples.Count: > 0, Exclusive: true};
  public bool WizardStepIsBool => CurrentWizardStep is {Type: "bool"};
  public bool WizardStepIsComboBox => CurrentWizardStep is {Examples.Count: > 0} && !WizardStepIsBool;
  public bool WizardStepIsTextBox => !WizardStepIsBool && !WizardStepIsComboBox;
  public string WizardStepPasswordChar => CurrentWizardStep is {IsPassword: true} ? "\u2022" : string.Empty;

  public bool ShowMountOperationsContent =>
    !ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView && !ShowDashboard && !IsConfigurationMode;

  public bool ShowMountConfigContent =>
    !ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView && !ShowDashboard && IsConfigurationMode;

  public bool ShowDashboardContent => ShowDashboard;
  public bool ShowMountContent => ShowMountOperationsContent || ShowMountConfigContent;
  public bool ShowConfigModeTestConnectionButton => ShowMountConfigContent;

  public string RevealInFileManagerLabel => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
      System.Runtime.InteropServices.OSPlatform.Windows) ? "Show in File Explorer"
    : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
      System.Runtime.InteropServices.OSPlatform.OSX) ? "Reveal in Finder"
    : "Show in File Manager";

  public bool ShowSettingsContent => ShowSettingsView;
  public bool ShowEditorScrollViewer => !ShowDiagnosticsView && !ShowDashboard;
  public bool IsRemoteListActive => ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView;
  public bool IsMountListActive => !ShowRemoteEditor && !ShowDiagnosticsView && !ShowSettingsView && !ShowDashboard;

  public bool ShowConfigureButton =>
    ShowMountOperationsContent && SelectedProfile is not null && !SelectedProfile.IsRemoteDefinition;

  public bool ShowBackButton => ShowMountConfigContent;

  public string DiagnosticsInfoCount => DiagnosticsRows
    .Count(r => string.Equals(r.SeverityText, "information", StringComparison.OrdinalIgnoreCase)).ToString();

  public string DiagnosticsWarningCount => DiagnosticsRows
    .Count(r => string.Equals(r.SeverityText, "warning", StringComparison.OrdinalIgnoreCase)).ToString();

  public string DiagnosticsErrorCount => DiagnosticsRows
    .Count(r => string.Equals(r.SeverityText, "error", StringComparison.OrdinalIgnoreCase)).ToString();

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
    Func<MountProfile, string, CancellationToken, Task<bool>>? sourceRemoteExistsInRcloneConfigRunner = null,
    Func<MountProfile, CancellationToken, Task>? repairMissingSourceRemoteRunner = null,
    Func<MountProfile, bool>? startupEnabledProbe = null,
    Func<TimeSpan, CancellationToken, Task<bool>>? runtimeRefreshWaiter = null,
    Func<IEnumerable<MountProfile>, CancellationToken, Task<IReadOnlyList<ProfileRuntimeState>>>?
      runtimeStateBatchVerifier = null,
    bool loadStartupData = true,
    ILogger<MainWindowViewModel>? logger = null)
  {
    _mountManagerService = mountManagerService ?? new MountManagerService(NullLogger<MountManagerService>.Instance);
    _launchAgentService = launchAgentService ?? new LaunchAgentService(NullLogger<LaunchAgentService>.Instance);
    _rcloneBackendService = rcloneBackendService ?? new RcloneBackendService(NullLogger<RcloneBackendService>.Instance);
    _rcloneConfigWizardService = rcloneConfigWizardService ??
                                 new RcloneConfigWizardService(NullLogger<RcloneConfigWizardService>.Instance);
    _startupPreflightService = startupPreflightService ??
                               new StartupPreflightService(NullLogger<StartupPreflightService>.Instance);
    _mountHealthService = mountHealthService ?? new MountHealthService(NullLogger<MountHealthService>.Instance);
    _logger = logger ?? NullLogger<MainWindowViewModel>.Instance;
    _mountStartRunner = mountStartRunner ?? _mountManagerService.StartAsync;
    _mountStopRunner = mountStopRunner ?? _mountManagerService.StopAsync;
    _testConnectionRunner = testConnectionRunner;
    _mountedProbe = mountedProbe ?? ((profile, cancellationToken) =>
      _mountManagerService.IsMountedAsync(profile.MountPoint, cancellationToken));
    _runtimeStateVerifier = runtimeStateVerifier ?? _mountHealthService.VerifyAsync;
    _startupPreflightRunner = startupPreflightRunner ?? _startupPreflightService.RunAsync;
    _startupEnableRunner = startupEnableRunner ?? _launchAgentService.EnableAsync;
    _startupDisableRunner = startupDisableRunner ?? _launchAgentService.DisableAsync;
    _sourceRemoteExistsInRcloneConfigRunner =
      sourceRemoteExistsInRcloneConfigRunner ?? SourceRemoteExistsInRcloneConfigAsync;
    _repairMissingSourceRemoteRunner = repairMissingSourceRemoteRunner ?? RepairMissingSourceRemoteInRcloneConfigAsync;
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
      MountProfile defaultProfile = CreateDefaultProfile();
      Profiles.Add(defaultProfile);
      _profileLogs[defaultProfile.Id] = new List<ProfileLogEvent>();
      _profileScripts[defaultProfile.Id] = string.Empty;
    }

    EnsureRemoteDefinitionsForMountSources();
    RefreshRemoteProfiles();
    RefreshMountProfiles();

    if (Profiles.Count > 0)
    {
      MountProfile? firstMount = MountProfiles.FirstOrDefault();
      MountProfile? firstRemote = RemoteProfiles.FirstOrDefault();
      SelectedProfile = firstMount ?? Profiles[0];
      _rememberedMountProfile = firstMount;
      _rememberedRemoteProfile = firstRemote;
      ShowRemoteEditor = false;
      _syncingSidebarSelection = true;
      SelectedMountProfile = null;
      SelectedRemoteProfile = null;
      _syncingSidebarSelection = false;
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
    {
      using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Initialization))
      {
        _logger.LogInformation("Profiles file: {ProfilesFilePath}", _profilesFilePath);
      }
    }

    if (!loadStartupData)
    {
      return;
    }

    _ = Task.Run((Func<Task?>) (async () =>
    {
      try
      {
        string binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
        await MountOptionsVm.LoadOptionsAsync(
          binary,
          SelectedProfile?.MountOptions ?? new Dictionary<string, string>(),
          CancellationToken.None,
          SelectedProfile?.PinnedMountOptions);
      }
      catch (Exception ex)
      {
        if (SelectedProfile is { } selectedProfile)
        {
          using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Initialization))
          {
            _logger.LogError(ex, "Could not load mount options: {ErrorMessage}", ex.Message);
          }
        }
      }
    }));
  }

  public bool HasSelectedMountRemote => SelectedMountRemoteProfile is not null
                                        && SelectedProfile is {IsRemoteDefinition: false, Type: not MountType.MacOsNfs};

  public string MountRemotePath
  {
    get
    {
      if (SelectedProfile is null)
      {
        return string.Empty;
      }

      if (!TryGetRemoteAliasFromSource(SelectedProfile.Source, out _, out string suffix))
      {
        return SelectedProfile.Source;
      }

      return suffix;
    }
    set
    {
      if (SelectedProfile is null)
      {
        return;
      }

      string? remoteAlias = SelectedMountRemoteProfile is not null
        ? GetRemoteAlias(SelectedMountRemoteProfile)
        : null;
      if (!string.IsNullOrWhiteSpace(remoteAlias))
      {
        string path = string.IsNullOrWhiteSpace(value) ? "/" : value;
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

  public string QuickConnectEndpointLabel =>
    SelectedProfile?.QuickConnectMode is QuickConnectMode.WebDav ? "Endpoint URL" : "Host";

  public string QuickConnectEndpointHint => SelectedProfile?.QuickConnectMode is QuickConnectMode.WebDav
    ? "https://example.com/remote.php/webdav"
    : "Example: ftp.example.com";

  public string QuickStartHelp =>
    "Quick start: choose a preset, change mount path, then click Start mount.";

  public bool IsStartupSupported => _isStartupSupported;

  public string StartupButtonText =>
    SelectedProfile?.StartAtLogin is true ? "Disable start at login" : "Enable start at login";

  public string SaveChangesButtonText => HasPendingChanges ? "Save mount *" : "Save mount";

  public string SelectedProfileLifecycleText =>
    FormatLifecycle(SelectedProfile?.RuntimeState.Lifecycle ?? MountLifecycleState.Idle);

  public string SelectedProfileHealthText =>
    FormatHealth(SelectedProfile?.RuntimeState.Health ?? MountHealthState.Unknown);

  public bool HasBackendOptions => BackendOptionInputs.Count > 0;
  public bool HasAdvancedBackendOptionInputs => AdvancedBackendOptionInputs.Count > 0;

  public string SelectedBackendDescription => SelectedBackend?.Details ?? string.Empty;

  [RelayCommand]
  private void AddMount()
  {
    string id = Guid.NewGuid().ToString("N");
    MountProfile profile = new()
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
    ShowDashboard = false;
    IsConfigurationMode = false;
    ShowRemoteEditor = false;
    SelectedProfile = profile;
    SelectedMountProfile = profile;
    UpdateSelectedMountRemoteFromSource(profile);
    MarkDirty();
  }

  [RelayCommand]
  private void AddRemote()
  {
    int remoteNumber = RemoteProfiles.Count + 1;
    string remoteAlias = $"remote{remoteNumber}";
    MountProfile profile = CreateRemoteDefinitionProfile(remoteAlias, $"Remote {remoteNumber}");

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
  private void SelectDashboard()
  {
    ShowDashboard = true;
  }

  [RelayCommand]
  private void EnterConfigurationMode()
  {
    IsConfigurationMode = true;
  }

  [RelayCommand]
  private void ExitConfigurationMode()
  {
    IsConfigurationMode = false;
  }

  [RelayCommand]
  private async Task CopyDiagnosticsAsync()
  {
    List<DiagnosticsTimelineRow> rows = DiagnosticsRows.ToList();
    if (rows.Count == 0)
    {
      return;
    }

    string text = string.Join(
      Environment.NewLine,
      rows.Select(r => $"{r.TimestampText}\t{r.ProfileName}\t{r.SeverityText}\t{r.StageText}\t{r.MessageText}"));

    if (Application.Current?.ApplicationLifetime
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
    if (selectedItems is not System.Collections.IList items || items.Count == 0)
    {
      return;
    }

    List<DiagnosticsTimelineRow> rows = items.OfType<DiagnosticsTimelineRow>().ToList();
    if (rows.Count == 0)
    {
      return;
    }

    string text = string.Join(
      Environment.NewLine,
      rows.Select(r => $"{r.TimestampText}\t{r.ProfileName}\t{r.SeverityText}\t{r.StageText}\t{r.MessageText}"));

    if (Application.Current?.ApplicationLifetime
          is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
        && desktop.MainWindow?.Clipboard is { } clipboard)
    {
      await clipboard.SetTextAsync(text);
      StatusText = $"Copied {rows.Count} selected log entries to clipboard.";
    }
  }

  private void LoadBackendsSync(bool enabled)
  {
    if (!enabled)
    {
      return;
    }

    try
    {
      string binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
      IReadOnlyList<RcloneBackendInfo> backends = Task
        .Run(() => _rcloneBackendService.GetBackendsAsync(binary, CancellationToken.None)).GetAwaiter().GetResult();

      AvailableBackends.Clear();
      foreach (RcloneBackendInfo backend in backends)
      {
        AvailableBackends.Add(backend);
      }

      if (AvailableBackends.Count > 0)
      {
        SelectedBackend = AvailableBackends[0];
      }

      if (SelectedProfile is { } selectedProfile)
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
        {
          _logger.LogInformation("Loaded {BackendCount} rclone backend types.", AvailableBackends.Count);
        }
      }
    }
    catch (Exception ex)
    {
      if (SelectedProfile is { } selectedProfile)
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Initialization))
        {
          _logger.LogError(ex, "Could not load backend list: {ErrorMessage}", ex.Message);
        }
      }
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

      List<string> missingRequired = BackendOptionInputs
        .Where(o => o.Required && string.IsNullOrWhiteSpace(o.Value))
        .Select(o => o.Name)
        .ToList();

      if (missingRequired.Count > 0)
      {
        throw new InvalidOperationException($"Missing required fields: {string.Join(", ", missingRequired)}");
      }

      string binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
      MountProfile activeProfile = SelectedProfile ?? throw new InvalidOperationException("No profile selected.");
      if (!activeProfile.IsRemoteDefinition)
      {
        throw new InvalidOperationException("Create remote is only available for REMOTES entries.");
      }

      bool remoteExistsInRclone =
        await RemoteExistsInRcloneConfigAsync(binary, NewRemoteName.Trim(), cancellationToken);

      if (remoteExistsInRclone)
      {
        await _rcloneBackendService.UpdateRemoteAsync(
          binary,
          NewRemoteName,
          BackendOptionInputs,
          cancellationToken);
      }
      else
      {
        await _rcloneBackendService.CreateRemoteAsync(
          binary,
          NewRemoteName,
          SelectedBackend.Name,
          BackendOptionInputs,
          cancellationToken);
      }

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
      {
        _logger.LogInformation("Created remote '{RemoteName}' ({BackendName}).", NewRemoteName, SelectedBackend.Name);
      }

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
      if (SelectedBackend is null || SelectedProfile is null)
      {
        return;
      }

      string binary = SelectedProfile.RcloneBinaryPath ?? "rclone";
      string remoteName = string.IsNullOrWhiteSpace(NewRemoteName)
        ? $"{SelectedBackend.Name}-remote"
        : NewRemoteName;

      try
      {
        ConfigWizardStep step = await _rcloneConfigWizardService.StartAsync(
          binary,
          remoteName,
          SelectedBackend.Name,
          cancellationToken);

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

  private bool CanStartWizard()
  {
    return !IsBusy &&
           HasProfiles &&
           SelectedBackend is not null &&
           !string.IsNullOrWhiteSpace(NewRemoteName) &&
           SelectedProfile is not null &&
           SelectedProfile.IsRemoteDefinition;
  }

  [RelayCommand]
  private async Task SubmitWizardAnswerAsync()
  {
    await RunBusyActionAsync(async cancellationToken =>
    {
      if (_wizardState is null || SelectedProfile is null || SelectedBackend is null)
      {
        return;
      }

      string binary = SelectedProfile.RcloneBinaryPath ?? "rclone";
      string remoteName = NewRemoteName;

      try
      {
        WizardStepNumber++;
        string answer;
        if (WizardStepIsBool)
        {
          answer = WizardStepBoolValue ? "true" : "false";
        }
        else if (WizardStepIsComboBox && WizardSelectedExample is not null)
        {
          answer = WizardSelectedExample.Value;
        }
        else
        {
          answer = WizardAnswer;
        }

        ConfigWizardStep step = await _rcloneConfigWizardService.ContinueAsync(
          binary,
          remoteName,
          _wizardState,
          answer,
          cancellationToken);

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
        string binary = SelectedProfile.RcloneBinaryPath ?? "rclone";
        await _rcloneConfigWizardService.DeleteRemoteAsync(binary, NewRemoteName, CancellationToken.None);
      }
      catch
      {
        // Best-effort cleanup
      }
    }

    ResetWizardState();
  }

  private async Task HandleWizardStepAsync(
    ConfigWizardStep step,
    string binary,
    string remoteName,
    CancellationToken cancellationToken)
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
        ConfigWizardStep nextStep = await _rcloneConfigWizardService.ContinueOAuthAsync(
          binary,
          remoteName,
          step.State,
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
      ConfigWizardStep nextStep = await _rcloneConfigWizardService.ContinueAsync(
        binary,
        remoteName,
        step.State,
        "false",
        cancellationToken);
      await HandleWizardStepAsync(nextStep, binary, remoteName, cancellationToken);
      return;
    }

    CurrentWizardStep = step;
    _wizardState = step.State;
    WizardStepInput = new WizardStepOptionInput(step);
    WizardAnswer = step.DefaultValue;
    WizardStepBoolValue = string.Equals(step.DefaultValue, "true", StringComparison.OrdinalIgnoreCase);
    WizardSelectedExample = step.Examples.FirstOrDefault(e =>
                                                           string.Equals(
                                                             e.Value,
                                                             step.DefaultValue,
                                                             StringComparison.OrdinalIgnoreCase));
    OnPropertyChanged(nameof(WizardStepIsBool));
    OnPropertyChanged(nameof(WizardStepIsComboBox));
    OnPropertyChanged(nameof(WizardStepIsTextBox));
    OnPropertyChanged(nameof(WizardStepPasswordChar));
  }

  private async Task ReadBackWizardConfigAsync(string binary, string remoteName, CancellationToken cancellationToken)
  {
    Dictionary<string, string> config =
      await _rcloneConfigWizardService.ReadRemoteConfigAsync(binary, remoteName, cancellationToken);

    if (SelectedProfile is not null)
    {
      SelectedProfile.Name = remoteName;
      SelectedProfile.Source = $"{remoteName}:";
      if (SelectedBackend is not null)
      {
        SelectedProfile.BackendName = SelectedBackend.Name;
      }

      Dictionary<string, string> backendOptions = new();
      foreach (KeyValuePair<string, string> kvp in config)
      {
        if (string.Equals(kvp.Key, "type", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        backendOptions[kvp.Key] = kvp.Value;
      }

      SelectedProfile.BackendOptions = backendOptions;

      foreach (RcloneBackendOptionInput input in BackendOptionInputs.Concat(AdvancedBackendOptionInputs))
      {
        if (config.TryGetValue(input.Name, out string? value))
        {
          input.Value = value;
        }
      }

      SaveProfiles();
    }
  }

  [RelayCommand]
  private void EnterManualMode()
  {
    IsManualMode = true;
  }

  [RelayCommand]
  private void ExitManualMode()
  {
    IsManualMode = false;
  }

  private void ResetWizardState()
  {
    IsWizardActive = false;
    IsWizardWaitingForOAuth = false;
    CurrentWizardStep = null;
    WizardStepInput = null;
    WizardAnswer = string.Empty;
    WizardStepBoolValue = false;
    WizardSelectedExample = null;
    WizardOAuthUrl = string.Empty;
    WizardStepNumber = 0;
    _wizardState = null;
  }

  private static async Task OpenBrowserAsync(string url)
  {
    try
    {
      if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
          && desktop.MainWindow is { } window)
      {
        TopLevel? topLevel = TopLevel.GetTopLevel(window);
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
  private async Task OpenSponsorLinkAsync()
  {
    await OpenBrowserAsync("https://ko-fi.com/frankhommers");
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

    MountProfile profileToRemove = SelectedProfile;
    if (profileToRemove.IsRemoteDefinition)
    {
      string? remoteAlias = GetRemoteAlias(profileToRemove);
      if (!string.IsNullOrWhiteSpace(remoteAlias))
      {
        List<MountProfile> dependentMounts = Profiles
          .Where(IsMountProfileCandidate)
          .Where(RequiresRemoteAssociation)
          .Where(mount => TryGetRemoteAliasFromSource(mount.Source, out string mountAlias, out _) &&
                          string.Equals(mountAlias, remoteAlias, StringComparison.OrdinalIgnoreCase))
          .ToList();

        if (dependentMounts.Count > 0)
        {
          string mountNames = string.Join(", ", dependentMounts.Select(m => m.Name));
          string message =
            $"Cannot delete remote '{profileToRemove.Name}'. It is still used by {dependentMounts.Count} mount(s): {mountNames}. Remove or reassign those mounts first.";
          DeleteBlockedDialogMessage = message;
          IsDeleteBlockedDialogVisible = true;
          StatusText = message;
          return;
        }
      }
    }

    int index = Profiles.IndexOf(profileToRemove);
    string removedId = profileToRemove.Id;
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

    MountProfile fallback = Profiles[Math.Max(0, index - 1)];
    SelectedProfile = fallback;
    MarkDirty();
    SaveProfiles();
    HasPendingChanges = false;
    StatusText = $"Removed {GetProfileTypeLabel(profileToRemove)} '{profileToRemove.Name}'.";
  }

  [RelayCommand(CanExecute = nameof(CanRunActions))]
  private async Task StartMountAsync()
  {
    SyncMountOptionsToProfile();
    await RunBusyActionAsync(async cancellationToken =>
    {
      MountProfile profile = SelectedProfile;
      string profileId = profile.Id;
      using (ProfileScope(profileId, ProfileLogCategory.ManualStart, ProfileLogStage.Initialization))
      {
        _logger.LogInformation("Starting mount...");
      }

      ApplyRuntimeState(
        profile,
        new ProfileRuntimeState(MountLifecycleState.Mounting, MountHealthState.Unknown, DateTimeOffset.UtcNow, null));

      try
      {
        using (ProfileScope(profileId, ProfileLogCategory.ManualStart, ProfileLogStage.Execution))
        {
          await Task.Run(() => _mountStartRunner(profile, cancellationToken), cancellationToken);
        }

        await RefreshRuntimeStateInternalAsync(profile, cancellationToken);
      }
      catch (Exception ex)
      {
        ApplyRuntimeState(
          profile,
          new ProfileRuntimeState(
            MountLifecycleState.Failed,
            MountHealthState.Failed,
            DateTimeOffset.UtcNow,
            ex.Message));
        throw;
      }
    });
  }

  [RelayCommand(CanExecute = nameof(CanRunActions))]
  private async Task StopMountAsync()
  {
    await RunBusyActionAsync(async cancellationToken =>
    {
      MountProfile profile = SelectedProfile;
      string profileId = profile.Id;
      using (ProfileScope(profileId, ProfileLogCategory.ManualStop, ProfileLogStage.Initialization))
      {
        _logger.LogInformation("Stopping mount...");
      }

      try
      {
        using (ProfileScope(profileId, ProfileLogCategory.ManualStop, ProfileLogStage.Execution))
        {
          await Task.Run(() => _mountStopRunner(profile, cancellationToken), cancellationToken);
        }

        if (!await Task.Run(() => _mountedProbe(profile, cancellationToken), cancellationToken))
        {
          ApplyRuntimeState(
            profile,
            new ProfileRuntimeState(MountLifecycleState.Idle, MountHealthState.Unknown, DateTimeOffset.UtcNow, null));
          return;
        }

        await RefreshRuntimeStateInternalAsync(profile, cancellationToken);
      }
      catch (Exception ex)
      {
        ApplyRuntimeState(
          profile,
          new ProfileRuntimeState(
            MountLifecycleState.Failed,
            MountHealthState.Failed,
            DateTimeOffset.UtcNow,
            ex.Message));
        throw;
      }
    });
  }

  [RelayCommand(CanExecute = nameof(CanRevealInFileManager))]
  private async Task RevealInFileManagerAsync()
  {
    string? mountPoint = SelectedProfile?.MountPoint;
    if (string.IsNullOrWhiteSpace(mountPoint) || !Directory.Exists(mountPoint))
    {
      return;
    }

    try
    {
      if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
          && desktop.MainWindow is { } window)
      {
        TopLevel? topLevel = TopLevel.GetTopLevel(window);
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

  private bool CanRevealInFileManager()
  {
    return HasProfiles &&
           !IsBusy &&
           SelectedProfile is not null &&
           !SelectedProfile.IsRemoteDefinition &&
           !string.IsNullOrWhiteSpace(SelectedProfile.MountPoint) &&
           SelectedProfile.RuntimeState.Lifecycle is MountLifecycleState.Mounted &&
           SelectedProfile.RuntimeState.Health is MountHealthState.Healthy or MountHealthState.Degraded;
  }

  [RelayCommand(CanExecute = nameof(CanRunActions))]
  private async Task RefreshStatusAsync()
  {
    await RunBusyActionAsync(RefreshStatusInternalAsync);
  }

  [RelayCommand(CanExecute = nameof(CanRefreshCache))]
  private async Task RefreshCacheAsync()
  {
    MountProfile? profile = SelectedProfile;
    if (profile is null)
    {
      return;
    }

    string? relativePath = await PickMountSubfolderAsync(profile.MountPoint);
    if (relativePath is null)
    {
      return;
    }

    await RunBusyActionAsync(async cancellationToken =>
    {
      int rcPort = ResolveEffectiveRcPort(profile);
      if (rcPort <= 0)
      {
        throw new InvalidOperationException("Remote control is not available for this mount.");
      }

      string? dir = string.IsNullOrEmpty(relativePath) ? null : relativePath;
      string displayPath = dir ?? "/";

      using (ProfileScope(profile.Id, ProfileLogCategory.General, ProfileLogStage.Execution))
      {
        _logger.LogInformation("Refreshing directory cache for '{Dir}' via RC port {RcPort}...", displayPath, rcPort);
      }

      RcloneRcClient rcClient = new(new HttpClient());
      StatusText = $"Refreshing cache for {displayPath}...";
      await rcClient.VfsRefreshAsync(rcPort, dir, cancellationToken);

      using (ProfileScope(profile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
      {
        _logger.LogInformation("Directory cache refreshed for '{Dir}'.", displayPath);
      }

      StatusText = $"Cache refreshed for {displayPath}.";
    });
  }

  [RelayCommand]
  private void DashboardNavigateToMount(MountProfile profile)
  {
    SelectedProfile = profile;
    ShowDashboard = false;
  }

  [RelayCommand]
  private void DashboardEditMount(MountProfile profile)
  {
    SelectedProfile = profile;
    ShowDashboard = false;
    ShowRemoteEditor = false;
    IsConfigurationMode = true;
  }

  [RelayCommand]
  private void DashboardEditRemote(MountProfile profile)
  {
    MountProfile? linkedRemote = ResolveAssociatedRemote(profile);
    if (linkedRemote is null)
    {
      return;
    }

    SelectedProfile = linkedRemote;
    ShowDashboard = false;
    ShowRemoteEditor = true;
    IsConfigurationMode = false;
  }

  private bool DashboardHasLinkedRemote(MountProfile profile)
  {
    return ResolveAssociatedRemote(profile) is not null;
  }

  [RelayCommand]
  private async Task DashboardStartMountAsync(MountProfile profile)
  {
    SelectedProfile = profile;
    if (StartMountCommand.CanExecute(null))
    {
      await StartMountCommand.ExecuteAsync(null);
    }
  }

  [RelayCommand]
  private async Task DashboardStopMountAsync(MountProfile profile)
  {
    SelectedProfile = profile;
    if (StopMountCommand.CanExecute(null))
    {
      await StopMountCommand.ExecuteAsync(null);
    }
  }

  [RelayCommand]
  private async Task DashboardRefreshCacheAsync(MountProfile profile)
  {
    SelectedProfile = profile;
    if (RefreshCacheCommand.CanExecute(null))
    {
      await RefreshCacheCommand.ExecuteAsync(null);
    }
  }

  [RelayCommand]
  private async Task DashboardRevealInFinderAsync(MountProfile profile)
  {
    SelectedProfile = profile;
    if (RevealInFileManagerCommand.CanExecute(null))
    {
      await RevealInFileManagerCommand.ExecuteAsync(null);
    }
  }

  [RelayCommand(CanExecute = nameof(CanRepairMissingSourceRemoteCommand))]
  private async Task RepairMissingSourceRemoteAsync()
  {
    MountProfile? profile = SelectedProfile;
    if (profile is null || profile.IsRemoteDefinition)
    {
      return;
    }

    await RunBusyActionAsync(async cancellationToken =>
    {
      await _repairMissingSourceRemoteRunner(profile, cancellationToken);
      await RefreshSourceRemoteConfigHealthAsync(profile, cancellationToken);
      StatusText = "Remote config repaired.";
    });
  }

  private static async Task<string?> PickMountSubfolderAsync(string mountPoint)
  {
    if (Application.Current?.ApplicationLifetime
          is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
        || desktop.MainWindow is not { } window)
    {
      return string.Empty;
    }

    TopLevel? topLevel = TopLevel.GetTopLevel(window);
    if (topLevel?.StorageProvider is not { } storageProvider)
    {
      return string.Empty;
    }

    IStorageFolder? startFolder = await storageProvider.TryGetFolderFromPathAsync(new Uri($"file://{mountPoint}"));

    IReadOnlyList<IStorageFolder> result = await storageProvider.OpenFolderPickerAsync(
      new FolderPickerOpenOptions
      {
        Title = "Select folder to refresh",
        SuggestedStartLocation = startFolder,
        AllowMultiple = false,
      });

    if (result.Count == 0)
    {
      return null;
    }

    string? selectedPath = result[0].TryGetLocalPath();
    if (string.IsNullOrEmpty(selectedPath))
    {
      return null;
    }

    if (!selectedPath.StartsWith(mountPoint, StringComparison.Ordinal))
    {
      return null;
    }

    string relative = selectedPath[mountPoint.Length..].TrimStart('/');
    return relative;
  }

  private bool CanRefreshCache()
  {
    return HasProfiles &&
           !IsBusy &&
           SelectedProfile is not null &&
           !SelectedProfile.IsRemoteDefinition &&
           SelectedProfile.RuntimeState.Lifecycle is MountLifecycleState.Mounted &&
           ResolveEffectiveRcPort(SelectedProfile) > 0;
  }

  private static int ResolveEffectiveRcPort(MountProfile profile)
  {
    if (profile.RcPort > 0)
    {
      return profile.RcPort;
    }

    if (profile.MountOptions.TryGetValue("rc_addr", out string? rcAddr) &&
        !string.IsNullOrWhiteSpace(rcAddr))
    {
      int lastColon = rcAddr.LastIndexOf(':');
      if (lastColon >= 0 && int.TryParse(rcAddr[(lastColon + 1)..], out int port) && port > 0)
      {
        return port;
      }
    }

    return 0;
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
      MountProfile profile = SelectedProfile;
      string profileId = profile.Id;
      _testDialogProfileId = profileId;
      using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Initialization))
      {
        _logger.LogInformation("Testing connection...");
      }

      if (_testConnectionRunner is not null)
      {
        using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Execution))
        {
          await _testConnectionRunner(profile, CancellationToken.None);
        }
      }
      else if (profile.IsRemoteDefinition &&
               !string.IsNullOrWhiteSpace(profile.Source) &&
               !profile.Source.StartsWith(":", StringComparison.Ordinal))
      {
        using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Execution))
        {
          await _mountManagerService.TestConnectionAsync(profile, CancellationToken.None);
        }
      }
      else if (profile.IsRemoteDefinition && SelectedBackend is not null)
      {
        string binary = profile.RcloneBinaryPath ?? "rclone";
        using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Execution))
        {
          await _mountManagerService.TestBackendConnectionAsync(
            binary,
            SelectedBackend.Name,
            BackendOptionInputs.Concat(AdvancedBackendOptionInputs),
            CancellationToken.None);
        }
      }
      else
      {
        using (ProfileScope(profileId, ProfileLogCategory.General, ProfileLogStage.Execution))
        {
          await _mountManagerService.TestConnectionAsync(profile, CancellationToken.None);
        }
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
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Execution))
        {
          _logger.LogError(ex, "{ErrorMessage}", ex.Message);
        }
      }
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
    {
      _logger.LogInformation("Generated shell script preview.");
    }
  }

  [RelayCommand]
  private void ApplyReliabilityPreset()
  {
    MountProfile profile = SelectedProfile;
    if (profile.IsRemoteDefinition || profile.Type is not MountType.RcloneAuto)
    {
      StatusText = "Reliability presets apply to rclone profiles only.";
      return;
    }

    SyncMountOptionsToProfile();

    ReliabilityPolicyPreset preset = ReliabilityPolicyPreset.GetByIdOrDefault(SelectedReliabilityPresetId);
    Dictionary<string, string> patchedOptions = new(profile.MountOptions, StringComparer.OrdinalIgnoreCase);

    foreach (string key in ReliabilityPolicyPreset.ManagedReliabilityKeys)
    {
      patchedOptions.Remove(key);
    }

    foreach ((string key, string value) in preset.OptionOverrides)
    {
      patchedOptions[key] = value;
    }

    profile.SelectedReliabilityPresetId = preset.Id;
    profile.MountOptions = patchedOptions;
    MountOptionsVm.UpdateFromProfile(profile.MountOptions, profile.PinnedMountOptions);
    SelectedReliabilityPresetId = preset.Id;

    StatusText = $"Applied reliability preset: {preset.DisplayName}.";
    using (ProfileScope(profile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
    {
      _logger.LogInformation("Applied reliability preset '{PresetDisplayName}'.", preset.DisplayName);
    }

    MarkDirty();
  }

  [RelayCommand(CanExecute = nameof(CanSaveScript))]
  private async Task SaveScriptAsync()
  {
    SyncMountOptionsToProfile();
    string scriptPath = _launchAgentService.GetScriptPath(SelectedProfile);
    Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);

    GeneratedScript = _mountManagerService.GenerateScript(SelectedProfile);
    _profileScripts[SelectedProfile.Id] = GeneratedScript;

    await File.WriteAllTextAsync(scriptPath, GeneratedScript);

    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
    {
      File.SetUnixFileMode(
        scriptPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    using (ProfileScope(SelectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
    {
      _logger.LogInformation("Script saved to: {ScriptPath}", scriptPath);
    }
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
      MountProfile profile = SelectedProfile;
      string profileId = profile.Id;

      if (!IsStartupSupported)
      {
        throw new InvalidOperationException("Start at login is currently supported on macOS only.");
      }

      if (profile.StartAtLogin)
      {
        using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Execution))
        {
          await _startupDisableRunner(profile, cancellationToken);
        }

        profile.StartAtLogin = false;
        SaveProfiles();
        HasPendingChanges = false;
        completionStatus = "Start at login disabled and saved.";
        using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Completion))
        {
          _logger.LogInformation("Persisted startup preference after disable.");
        }
      }
      else
      {
        StartupPreflightReport report = await Task.Run(
          () => _startupPreflightRunner(profile, cancellationToken),
          cancellationToken);
        RecordStartupPreflightReport(profileId, report);
        AppendStartupPreflightChecksToLog(profileId, report);

        if (!report.CriticalChecksPassed)
        {
          completionStatus = "Start at login blocked: startup preflight failed.";
          using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Verification))
          {
            _logger.LogWarning("Startup enable blocked by critical preflight failures.");
          }

          return;
        }

        string resolved = MountManagerService.ResolveAbsoluteBinaryPath(profile.RcloneBinaryPath);
        if (!string.Equals(profile.RcloneBinaryPath, resolved, StringComparison.Ordinal))
        {
          profile.RcloneBinaryPath = resolved;
          using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Execution))
          {
            _logger.LogInformation("Resolved rclone binary path to '{ResolvedBinaryPath}'.", resolved);
          }
        }

        string script = _mountManagerService.GenerateScript(profile);
        using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Execution))
        {
          await _startupEnableRunner(profile, script, cancellationToken);
        }

        profile.StartAtLogin = true;
        SaveProfiles();
        HasPendingChanges = false;
        completionStatus = "Start at login enabled and saved.";
        using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Completion))
        {
          _logger.LogInformation("Persisted startup preference after enable.");
        }
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
      MountProfile profile = SelectedProfile;
      string profileId = profile.Id;
      StartupPreflightReport report = await Task.Run(
        () => _startupPreflightRunner(profile, cancellationToken),
        cancellationToken);
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
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Verification))
        {
          _logger.LogWarning("Save blocked: one or more mounts have no associated remote.");
        }
      }

      return;
    }

    SaveProfiles();
    HasPendingChanges = false;
    StatusText = "Profile changes saved.";
    if (SelectedProfile is { } activeProfile)
    {
      using (ProfileScope(activeProfile.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
      {
        _logger.LogInformation("Saved profile changes.");
      }
    }

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
        try
        {
          await RefreshAllRuntimeStatesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
          throw;
        }
        catch (Exception ex)
        {
          if (SelectedProfile is { } sp)
          {
            using (ProfileScope(sp.Id, ProfileLogCategory.RuntimeRefresh, ProfileLogStage.Execution))
            {
              _logger.LogWarning(ex, "Runtime refresh iteration failed: {ErrorMessage}", ex.Message);
            }
          }
        }
      }
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
      if (SelectedProfile is { } selectedProfile)
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.RuntimeRefresh, ProfileLogStage.Execution))
        {
          _logger.LogError(ex, "Runtime monitoring loop failed: {ErrorMessage}", ex.Message);
        }
      }
    }
  }

  private async Task AdoptOrphanMountsAsync(CancellationToken cancellationToken)
  {
    List<MountProfile> mountProfiles = Profiles
      .Where(p => !p.IsRemoteDefinition && p.EnableRemoteControl && p.RcPort > 0)
      .ToList();

    if (mountProfiles.Count == 0)
    {
      return;
    }

    if (SelectedProfile is { } selectedProfile)
    {
      using (ProfileScope(selectedProfile.Id, ProfileLogCategory.Startup, ProfileLogStage.Initialization))
      {
        _logger.LogInformation(
          "Probing {MountProfileCount} mount profiles for running orphans...",
          mountProfiles.Count);
      }
    }

    foreach (MountProfile profile in mountProfiles)
    {
      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
        {
          _logger.LogInformation(
            "Probing orphan: {ProfileName} (RC port {RcPort}, mount {MountPoint})",
            profile.Name,
            profile.RcPort,
            profile.MountPoint);
        }

        int? pid = await _mountManagerService.ProbeRcPidAsync(profile.RcPort, cancellationToken);

        using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
        {
          _logger.LogInformation(
            "RC probe result for {ProfileName}: PID={Pid}",
            profile.Name,
            pid?.ToString() ?? "null");
        }

        bool isMounted = await _mountManagerService.IsMountedAsync(profile.MountPoint, cancellationToken);

        using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
        {
          _logger.LogInformation("IsMounted result for {ProfileName}: {IsMounted}", profile.Name, isMounted);
        }

        if (pid.HasValue && isMounted)
        {
          _mountManagerService.AdoptMount(profile.MountPoint, pid.Value, profile.RcPort);
          using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
          {
            _logger.LogInformation("Adopted running mount (PID {Pid}, RC port {RcPort}).", pid.Value, profile.RcPort);
          }
        }
        else if (pid.HasValue && !isMounted)
        {
          using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
          {
            _logger.LogWarning("Stale rclone on port {RcPort} (PID {Pid}), sending quit.", profile.RcPort, pid.Value);
          }

          await _mountManagerService.StopViaRcAsync(profile.RcPort, cancellationToken);
        }
        else if (!pid.HasValue && isMounted)
        {
          using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
          {
            _logger.LogWarning("Mount point is active but no RC connection. Unmanaged external mount.");
          }
        }
        else
        {
          using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
          {
            _logger.LogInformation("No orphan found for {ProfileName}: not mounted, no RC.", profile.Name);
          }
        }
      }
      catch (Exception ex)
      {
        using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
        {
          _logger.LogWarning(ex, "Failed to probe orphan mount: {ErrorMessage}", ex.Message);
        }
      }
    }
  }

  private async Task VerifyStartupProfilesAsync(CancellationToken cancellationToken)
  {
    List<MountProfile> startupProfiles = Profiles
      .Where(profile => !profile.IsRemoteDefinition && profile.StartAtLogin)
      .ToList();

    if (startupProfiles.Count == 0)
    {
      if (SelectedProfile is { } selectedProfile)
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
        {
          _logger.LogInformation("Startup runtime verification skipped: no start-at-login profiles.");
        }
      }

      return;
    }

    foreach (MountProfile profile in startupProfiles)
    {
      using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Initialization))
      {
        _logger.LogInformation("Startup monitor initialization started.");
      }
    }

    IReadOnlyList<ProfileRuntimeState> states = await _runtimeStateBatchVerifier(startupProfiles, cancellationToken);

    await RunOnUiThreadAsync(() =>
    {
      for (int index = 0; index < startupProfiles.Count; index++)
      {
        MountProfile profile = startupProfiles[index];
        ProfileRuntimeState state = states[index];
        ApplyRuntimeState(profile, state);
        using (ProfileScope(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification))
        {
          _logger.LogInformation(
            "Startup verification: lifecycle={Lifecycle}, health={Health}",
            FormatLifecycle(state.Lifecycle),
            FormatHealth(state.Health));
        }
      }
    });
  }

  private async Task RefreshAllRuntimeStatesAsync(CancellationToken cancellationToken)
  {
    List<MountProfile> profilesSnapshot = Profiles.Where(IsMountProfileCandidate).ToList();
    if (profilesSnapshot.Count == 0)
    {
      return;
    }

    IReadOnlyList<ProfileRuntimeState> states = await _runtimeStateBatchVerifier(profilesSnapshot, cancellationToken);
    await RunOnUiThreadAsync(() =>
    {
      for (int index = 0; index < profilesSnapshot.Count; index++)
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

  private static async Task<bool> DefaultRuntimeRefreshWaiterAsync(
    TimeSpan cadence,
    CancellationToken cancellationToken)
  {
    using PeriodicTimer timer = new(cadence);
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

    using CancellationTokenSource cancellationTokenSource = new();

    try
    {
      string statusBeforeAction = StatusText;
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
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Execution))
        {
          _logger.LogError(ex, "{ErrorMessage}", ex.Message);
        }
      }
    }
    finally
    {
      IsBusy = false;
      NotifyCommandStateChanged();
    }
  }

  private async Task RefreshStatusInternalAsync(CancellationToken cancellationToken)
  {
    MountProfile profile = SelectedProfile;
    await RefreshRuntimeStateInternalAsync(profile, cancellationToken);
  }

  private async Task RefreshRuntimeStateInternalAsync(MountProfile profile, CancellationToken cancellationToken)
  {
    ProfileRuntimeState state = await _runtimeStateVerifier(profile, cancellationToken);
    ApplyRuntimeState(profile, state);
    using (ProfileScope(profile.Id, ProfileLogCategory.RuntimeRefresh, ProfileLogStage.Completion))
    {
      _logger.LogInformation("Status: {LastStatus}", profile.LastStatus);
    }
  }

  private void ApplyRuntimeState(MountProfile profile, ProfileRuntimeState state)
  {
    profile.RuntimeState = state;
    profile.IsMounted = state.Lifecycle is MountLifecycleState.Mounted;
    profile.IsRunning = state.Lifecycle is MountLifecycleState.Mounted or MountLifecycleState.Mounting;
    profile.LastStatus = BuildStatusText(state);

    using (ProfileScope(profile.Id, ProfileLogCategory.RuntimeRefresh, ProfileLogStage.Execution))
    {
      _logger.LogDebug(
        "ApplyRuntimeState: profile={Name}, lifecycle={Lifecycle}, isSelected={IsSelected}",
        profile.Name,
        state.Lifecycle,
        ReferenceEquals(SelectedProfile, profile));
    }

    if (ReferenceEquals(SelectedProfile, profile))
    {
      StatusText = profile.LastStatus;
      OnPropertyChanged(nameof(SelectedProfileLifecycleText));
      OnPropertyChanged(nameof(SelectedProfileHealthText));
      NotifyCommandStateChanged();
    }
  }

  private static string BuildStatusText(ProfileRuntimeState state)
  {
    string text = $"Lifecycle: {FormatLifecycle(state.Lifecycle)} | Health: {FormatHealth(state.Health)}";
    return string.IsNullOrWhiteSpace(state.ErrorText)
      ? text
      : $"{text} | Detail: {state.ErrorText}";
  }

  private static string FormatLifecycle(MountLifecycleState lifecycle)
  {
    return lifecycle.ToString().ToLowerInvariant();
  }

  private static string FormatHealth(MountHealthState health)
  {
    return health.ToString().ToLowerInvariant();
  }

  private IDisposable ProfileScope(string profileId, ProfileLogCategory category, ProfileLogStage stage)
  {
    string profileName = Profiles.FirstOrDefault(p =>
                                                   string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase))
      ?.Name ?? profileId;

    return _logger.BeginScope(
      new Dictionary<string, object>
      {
        ["ProfileId"] = profileId,
        ["ProfileName"] = profileName,
        ["LogCategory"] = category.ToString(),
        ["LogStage"] = stage.ToString(),
      })!;
  }

  private void OnSerilogEvent(LogEvent logEvent)
  {
    string profileId = DiagnosticsSink.ExtractProfileId(logEvent);
    string message = DiagnosticsSink.RenderMessage(logEvent);

    ProfileLogSeverity severity = logEvent.Level switch
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

    ProfileLogCategory category =
      ExtractEnumProperty<ProfileLogCategory>(logEvent, "LogCategory") ?? ProfileLogCategory.General;
    ProfileLogStage stage = ExtractEnumProperty<ProfileLogStage>(logEvent, "LogStage") ?? ProfileLogStage.Execution;

    ProfileLogEvent entry = new(profileId, DateTimeOffset.UtcNow, category, stage, severity, message, errorText);

    lock (_profileLogs)
    {
      if (!_profileLogs.TryGetValue(profileId, out List<ProfileLogEvent>? logEntries))
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
      void AddLine()
      {
        TestDialogLines.Add(message);
      }

      if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
      {
        AddLine();
      }
      else
      {
        Dispatcher.UIThread.Post(AddLine);
      }
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
        && value is ScalarValue {Value: string text}
        && Enum.TryParse<T>(text, true, out T parsed))
    {
      return parsed;
    }

    return null;
  }

  private DiagnosticsTimelineRow ToDiagnosticsRow(ProfileLogEvent entry)
  {
    string timestamp = entry.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    string category = entry.Category.ToString().ToLowerInvariant();
    string stage = entry.Stage.ToString().ToLowerInvariant();
    string severity = entry.Severity.ToString().ToLowerInvariant();
    string stageText = $"{category}/{stage}";

    string profileName;
    string profileType;

    if (string.Equals(entry.ProfileId, DiagnosticsSink.SystemProfileId, StringComparison.OrdinalIgnoreCase))
    {
      profileName = "System";
      profileType = "system";
    }
    else
    {
      MountProfile? profile = Profiles.FirstOrDefault(p => string.Equals(
                                                        p.Id,
                                                        entry.ProfileId,
                                                        StringComparison.OrdinalIgnoreCase));
      profileName = profile?.Name ?? entry.ProfileId;
      profileType = profile?.IsRemoteDefinition == true ? "remote" : "mount";
    }

    return new DiagnosticsTimelineRow(
      entry.ProfileId,
      profileName,
      profileType,
      timestamp,
      severity,
      stageText,
      entry.Message);
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
    if (value)
    {
      ShowDashboard = false;
    }

    NotifyViewStateChanged();
    EnsureSingleActiveSidebarSelection();
    NotifyCommandStateChanged();
  }

  partial void OnShowSettingsViewChanged(bool value)
  {
    if (value)
    {
      ShowDashboard = false;
    }

    NotifyViewStateChanged();
    EnsureSingleActiveSidebarSelection();
    NotifyCommandStateChanged();
  }

  partial void OnIsWizardActiveChanged(bool value)
  {
    OnPropertyChanged(nameof(ShowWizardContent));
    OnPropertyChanged(nameof(ShowStandardRemoteForm));
    OnPropertyChanged(nameof(ShowManualRemoteForm));
    OnPropertyChanged(nameof(ShowRemoteChooser));
    OnPropertyChanged(nameof(ShowManualRemoteForm));
    OnPropertyChanged(nameof(ShowRemoteChooser));
  }

  partial void OnIsManualModeChanged(bool value)
  {
    OnPropertyChanged(nameof(ShowManualRemoteForm));
    OnPropertyChanged(nameof(ShowRemoteChooser));
    OnPropertyChanged(nameof(ShowStandardRemoteForm));
    OnPropertyChanged(nameof(ShowManualRemoteForm));
    OnPropertyChanged(nameof(ShowRemoteChooser));
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
    string resolvedPresetId = ReliabilityPolicyPreset.GetByIdOrDefault(value).Id;
    if (!string.Equals(value, resolvedPresetId, StringComparison.OrdinalIgnoreCase))
    {
      SelectedReliabilityPresetId = resolvedPresetId;
      return;
    }

    if (!string.Equals(
          SelectedProfile.SelectedReliabilityPresetId,
          resolvedPresetId,
          StringComparison.OrdinalIgnoreCase))
    {
      SelectedProfile.SelectedReliabilityPresetId = resolvedPresetId;
    }
  }

  private void SyncDiagnosticsFilters()
  {
    DiagnosticsProfileFilters.Clear();
    DiagnosticsProfileFilters.Add(new DiagnosticsProfileFilterOption(string.Empty, "All", "all"));
    foreach (MountProfile profile in Profiles)
    {
      string profileType = profile.IsRemoteDefinition ? "remote" : "mount";
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

    string currentId = SelectedDiagnosticsProfileFilterOption.ProfileId;
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
      HashSet<string> remoteIds = Profiles.Where(p => p.IsRemoteDefinition).Select(p => p.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      events = events.Where(e => remoteIds.Contains(e.ProfileId));
    }
    else if (string.Equals(SelectedDiagnosticsCategoryFilter, "Mounts", StringComparison.OrdinalIgnoreCase))
    {
      HashSet<string> mountIds = Profiles.Where(p => !p.IsRemoteDefinition).Select(p => p.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      events = events.Where(e => mountIds.Contains(e.ProfileId));
    }

    if (!string.IsNullOrWhiteSpace(SelectedDiagnosticsProfileId))
    {
      events = events.Where(entry => string.Equals(
                              entry.ProfileId,
                              SelectedDiagnosticsProfileId,
                              StringComparison.OrdinalIgnoreCase));
    }

    if (StartupTimelineOnly)
    {
      events = events.Where(IsStartupTimelineEvent);
    }

    if (!string.IsNullOrWhiteSpace(DiagnosticsSearchText))
    {
      string search = DiagnosticsSearchText;
      events = events.Where(entry => entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    List<DiagnosticsTimelineRow> rows = events
      .OrderBy(entry => entry.Timestamp)
      .ThenBy(entry => entry.ProfileId, StringComparer.OrdinalIgnoreCase)
      .ThenBy(entry => entry.Category)
      .ThenBy(entry => entry.Stage)
      .ThenBy(entry => entry.Message, StringComparer.Ordinal)
      .Select(ToDiagnosticsRow)
      .ToList();

    DiagnosticsRows.Clear();
    Logs.Clear();
    foreach (DiagnosticsTimelineRow row in rows)
    {
      DiagnosticsRows.Add(row);
      Logs.Add(row.DisplayText);
    }
  }

  private static bool IsStartupTimelineEvent(ProfileLogEvent entry)
  {
    return entry.Category is ProfileLogCategory.Startup;
  }

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
    foreach (StartupCheckResult check in report.Checks)
    {
      ProfileLogSeverity severity = check.Severity switch
      {
        StartupCheckSeverity.Pass => ProfileLogSeverity.Information,
        StartupCheckSeverity.Warning => ProfileLogSeverity.Warning,
        _ => ProfileLogSeverity.Error,
      };

      using (ProfileScope(profileId, ProfileLogCategory.Startup, ProfileLogStage.Verification))
      {
        if (severity is ProfileLogSeverity.Error)
        {
          _logger.LogError(
            "Startup preflight {CheckSeverity}: [{CheckKey}] {CheckMessage}",
            check.Severity.ToString().ToLowerInvariant(),
            check.CheckKey,
            check.Message);
        }
        else if (severity is ProfileLogSeverity.Warning)
        {
          _logger.LogWarning(
            "Startup preflight {CheckSeverity}: [{CheckKey}] {CheckMessage}",
            check.Severity.ToString().ToLowerInvariant(),
            check.CheckKey,
            check.Message);
        }
        else
        {
          _logger.LogInformation(
            "Startup preflight {CheckSeverity}: [{CheckKey}] {CheckMessage}",
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
    RefreshCacheCommand.NotifyCanExecuteChanged();
    TestConnectionCommand.NotifyCanExecuteChanged();
    GenerateScriptCommand.NotifyCanExecuteChanged();
    SaveScriptCommand.NotifyCanExecuteChanged();
    ToggleStartupCommand.NotifyCanExecuteChanged();
    CreateRemoteCommand.NotifyCanExecuteChanged();
    StartWizardCommand.NotifyCanExecuteChanged();
    SaveChangesCommand.NotifyCanExecuteChanged();
    RunStartupPreflightCommand.NotifyCanExecuteChanged();
    RepairMissingSourceRemoteCommand.NotifyCanExecuteChanged();
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

  partial void OnSelectedWindowCloseBehaviorChanged(WindowCloseBehavior value)
  {
    _ = value;

    if (_isLoadingProfiles)
    {
      return;
    }

    SaveProfiles();
  }

  private bool CanRemoveProfile()
  {
    return HasProfiles && !IsBusy;
  }

  private static string GetProfileTypeLabel(MountProfile profile)
  {
    return profile.IsRemoteDefinition ? "remote" : "mount";
  }

  private bool CanRunActions()
  {
    return HasProfiles &&
           !IsBusy &&
           SelectedProfile is not null &&
           !SelectedProfile.IsRemoteDefinition &&
           HasValidRemoteAssociation(SelectedProfile) &&
           !IsSourceRemoteMissingFromRcloneConfig &&
           !string.IsNullOrWhiteSpace(SelectedProfile.Source) &&
           !string.IsNullOrWhiteSpace(SelectedProfile.MountPoint);
  }

  private bool CanRepairMissingSourceRemoteCommand()
  {
    return !IsBusy && CanRepairMissingSourceRemote && SelectedProfile is {IsRemoteDefinition: false};
  }

  private bool CanTestConnection()
  {
    return HasProfiles &&
           !IsBusy &&
           SelectedProfile is not null &&
           (SelectedProfile.IsRemoteDefinition
             ? SelectedBackend is not null || !string.IsNullOrWhiteSpace(SelectedProfile.Source)
             : SelectedProfile.Type is MountType.RcloneAuto or MountType.RcloneFuse or MountType.RcloneNfs &&
               HasValidRemoteAssociation(SelectedProfile) &&
               !string.IsNullOrWhiteSpace(SelectedProfile.Source));
  }

  private bool CanSaveScript()
  {
    return HasProfiles && !IsBusy && !string.IsNullOrWhiteSpace(GeneratedScript);
  }

  private bool CanToggleStartup()
  {
    return HasProfiles && !IsBusy && SelectedProfile is not null && !SelectedProfile.IsRemoteDefinition &&
           IsStartupSupported;
  }

  private bool CanRunStartupPreflight()
  {
    return HasProfiles && !IsBusy && SelectedProfile is not null && !SelectedProfile.IsRemoteDefinition &&
           IsStartupSupported;
  }

  private bool CanSaveChanges()
  {
    return !IsBusy && HasPendingChanges && !HasAnyInvalidMountRemoteAssociations();
  }


  private bool CanCreateRemote()
  {
    return !IsBusy &&
           HasProfiles &&
           SelectedBackend is not null &&
           !string.IsNullOrWhiteSpace(NewRemoteName) &&
           SelectedProfile is not null &&
           SelectedProfile.IsRemoteDefinition;
  }

  partial void OnSelectedBackendChanged(RcloneBackendInfo? value)
  {
    OnPropertyChanged(nameof(SelectedBackendDescription));

    Dictionary<string, (string Value, bool IsPassword)> previousValues = BackendOptionInputs
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

    foreach (RcloneBackendOptionInput input in BackendOptionInputs.Concat(AdvancedBackendOptionInputs))
    {
      if (previousValues.TryGetValue(input.Name, out (string Value, bool IsPassword) saved))
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

    RcloneBackendInfo? backend = AvailableBackends.FirstOrDefault(b =>
                                                                    string.Equals(
                                                                      b.Name,
                                                                      profile.BackendName,
                                                                      StringComparison.OrdinalIgnoreCase));

    if (backend is null)
    {
      return;
    }

    SelectedBackend = backend;

    // Restore saved option values into the populated inputs
    if (profile.BackendOptions.Count > 0)
    {
      foreach (RcloneBackendOptionInput input in BackendOptionInputs.Concat(AdvancedBackendOptionInputs))
      {
        if (profile.BackendOptions.TryGetValue(input.Name, out string? savedValue))
        {
          input.Value = savedValue;
          if (input.IsPassword)
          {
            input.ConfirmValue = savedValue;
          }

          if (input.ControlType is OptionControlType.EditableComboBox
              or OptionControlType.ComboBox)
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

    foreach (RcloneBackendOption option in backend.Options
               .Where(o => o.Required || !o.Advanced)
               .OrderByDescending(o => o.Required))
    {
      BackendOptionInputs.Add(new RcloneBackendOptionInput(option));
    }

    if (ShowAdvancedBackendOptions)
    {
      foreach (RcloneBackendOption option in backend.Options
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

    if (SelectedProfile is {IsRemoteDefinition: true} && !string.IsNullOrWhiteSpace(value))
    {
      MountProfile remote = SelectedProfile;
      string newAlias = value.Trim();
      string? previousAlias = GetRemoteAlias(remote);

      remote.Name = newAlias;
      remote.Source = $"{newAlias}:/";

      if (!string.IsNullOrWhiteSpace(previousAlias) &&
          !string.Equals(previousAlias, newAlias, StringComparison.OrdinalIgnoreCase))
      {
        foreach (MountProfile mount in Profiles.Where(IsMountProfileCandidate))
        {
          if (!TryGetRemoteAliasFromSource(mount.Source, out string mountAlias, out string suffix) ||
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

    if (_profileStartupPreflightReports.TryGetValue(value.Id, out StartupPreflightReport? preflightReport))
    {
      StartupPreflightSummary = preflightReport.ToSummaryText();
      StartupPreflightReport = preflightReport.ToUserFacingMessage();
    }
    else
    {
      StartupPreflightSummary = "Startup preflight has not been run.";
      StartupPreflightReport = string.Empty;
    }

    EnsureDiagnosticsFilterSelection(value.Id);
    RefreshDiagnosticsTimeline();

    if (_profileScripts.TryGetValue(value.Id, out string? script))
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
    NotifyViewStateChanged();
    _ = RefreshSourceRemoteConfigHealthAsync(value);
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
    ShowDashboard = false;
    ResetWizardState();
    IsManualMode = false;
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
    ShowDashboard = false;
    IsConfigurationMode = false;
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

    MountProfile? mountProfile = SelectedMountProfile;
    if (mountProfile is null || mountProfile.IsRemoteDefinition)
    {
      return;
    }

    string? remoteAlias = GetRemoteAlias(value);
    if (string.IsNullOrWhiteSpace(remoteAlias))
    {
      return;
    }

    string suffix = "/";
    if (TryGetRemoteAliasFromSource(mountProfile.Source, out _, out string existingSuffix) &&
        !string.IsNullOrWhiteSpace(existingSuffix))
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
    _ = RefreshSourceRemoteConfigHealthAsync(mountProfile);
  }

  partial void OnShowRemoteEditorChanged(bool value)
  {
    if (value)
    {
      ShowDashboard = false;
    }

    NotifyViewStateChanged();

    EnsureSingleActiveSidebarSelection();
    NotifyCommandStateChanged();
  }

  partial void OnShowDashboardChanged(bool value)
  {
    if (value)
    {
      ShowDiagnosticsView = false;
      ShowSettingsView = false;
      ShowRemoteEditor = false;
      IsConfigurationMode = false;
    }

    NotifyViewStateChanged();
    EnsureSingleActiveSidebarSelection();
    NotifyCommandStateChanged();
  }

  partial void OnIsConfigurationModeChanged(bool value)
  {
    _ = value;
    NotifyViewStateChanged();
    NotifyCommandStateChanged();
  }

  private void OnObservedProfileChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(MountProfile.Type) && _observedProfile is not null &&
        _observedProfile.Type is MountType.MacOsNfs)
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

      if (_observedProfile is not null)
      {
        _ = RefreshSourceRemoteConfigHealthAsync(_observedProfile);
      }
    }

    if (e.PropertyName is nameof(MountProfile.RuntimeState) or nameof(MountProfile.LastStatus))
    {
      _ = RunOnUiThreadAsync(() =>
      {
        NotifyCommandStateChanged();
        OnPropertyChanged(nameof(SelectedProfileLifecycleText));
        OnPropertyChanged(nameof(SelectedProfileHealthText));

        if (_observedProfile is not null && !string.IsNullOrWhiteSpace(_observedProfile.LastStatus))
        {
          StatusText = _observedProfile.LastStatus;
        }
      });
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
    OnPropertyChanged(nameof(MountProfiles));
    OnPropertyChanged(nameof(DashboardMountCards));
    OnPropertyChanged(nameof(HasMountProfiles));
    OnPropertyChanged(nameof(HasProfiles));
    NotifyCommandStateChanged();

    if (SelectedProfile is not null)
    {
      _ = RefreshSourceRemoteConfigHealthAsync(SelectedProfile);
    }
  }

  private void NotifyViewStateChanged()
  {
    OnPropertyChanged(nameof(WorkspaceTitle));
    OnPropertyChanged(nameof(WorkspaceSubtitle));
    OnPropertyChanged(nameof(ShowRemoteEditorContent));
    OnPropertyChanged(nameof(ShowWizardContent));
    OnPropertyChanged(nameof(ShowStandardRemoteForm));
    OnPropertyChanged(nameof(ShowManualRemoteForm));
    OnPropertyChanged(nameof(ShowRemoteChooser));
    OnPropertyChanged(nameof(ShowMountOperationsContent));
    OnPropertyChanged(nameof(ShowMountConfigContent));
    OnPropertyChanged(nameof(ShowMountContent));
    OnPropertyChanged(nameof(ShowDashboardContent));
    OnPropertyChanged(nameof(ShowSettingsContent));
    OnPropertyChanged(nameof(ShowEditorScrollViewer));
    OnPropertyChanged(nameof(IsRemoteListActive));
    OnPropertyChanged(nameof(IsMountListActive));
    OnPropertyChanged(nameof(SidebarSelectedRemoteProfile));
    OnPropertyChanged(nameof(SidebarSelectedMountProfile));
    OnPropertyChanged(nameof(ShowConfigureButton));
    OnPropertyChanged(nameof(ShowBackButton));
    OnPropertyChanged(nameof(ShowConfigModeTestConnectionButton));
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

      string json = File.ReadAllText(_profilesFilePath);
      List<PersistedProfile>? savedProfiles = JsonSerializer.Deserialize<List<PersistedProfile>>(json);
      if (savedProfiles is null || savedProfiles.Count == 0)
      {
        return;
      }

      WindowCloseBehavior? savedCloseBehavior = savedProfiles
        .Select(p => p.WindowCloseBehavior)
        .FirstOrDefault(v => v.HasValue);

      if (savedCloseBehavior.HasValue)
      {
        SelectedWindowCloseBehavior = savedCloseBehavior.Value;
      }

      Profiles.Clear();
      _profileLogs.Clear();
      _profileScripts.Clear();
      foreach (PersistedProfile saved in savedProfiles)
      {
        MountProfile profile = new()
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

        if (!profile.IsRemoteDefinition && profile.MountOptions.TryGetValue("rc_addr", out string? rcAddrValue))
        {
          if (rcAddrValue.Contains(':'))
          {
            string portStr = rcAddrValue.Split(':').Last();
            if (int.TryParse(portStr, out int port) && port > 0)
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
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Initialization))
        {
          _logger.LogError(ex, "Could not load profiles: {ErrorMessage}", ex.Message);
        }
      }
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
      string? directory = Path.GetDirectoryName(_profilesFilePath);
      if (!string.IsNullOrWhiteSpace(directory))
      {
        Directory.CreateDirectory(directory);
      }

      SyncMountOptionsToProfile();

      List<PersistedProfile> payload = Profiles
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
          WindowCloseBehavior = SelectedWindowCloseBehavior,
        })
        .ToList();

      string json = JsonSerializer.Serialize(
        payload,
        new JsonSerializerOptions
        {
          WriteIndented = true,
        });

      File.WriteAllText(_profilesFilePath, json);
    }
    catch (Exception ex)
    {
      if (SelectedProfile is { } selectedProfile)
      {
        using (ProfileScope(selectedProfile.Id, ProfileLogCategory.General, ProfileLogStage.Execution))
        {
          _logger.LogError(ex, "Could not save profiles: {ErrorMessage}", ex.Message);
        }
      }
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
    string id = Guid.NewGuid().ToString("N");
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
    int port = FindFreePort();
    Dictionary<string, string> rcDefaults = new()
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
    using TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint) listener.LocalEndpoint).Port;
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

  private static bool IsRemoteProfileCandidate(MountProfile profile)
  {
    return profile.IsRemoteDefinition;
  }

  private static bool IsMountProfileCandidate(MountProfile profile)
  {
    return !profile.IsRemoteDefinition;
  }

  private static bool TryGetRemoteAliasFromSource(string source, out string remoteAlias, out string suffix)
  {
    remoteAlias = string.Empty;
    suffix = string.Empty;
    if (string.IsNullOrWhiteSpace(source))
    {
      return false;
    }

    int separator = source.IndexOf(':');
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
    if (!TryGetRemoteAliasFromSource(profile.Source, out string remoteAlias, out _))
    {
      return null;
    }

    return remoteAlias;
  }

  private async Task<bool> RemoteExistsInRcloneConfigAsync(
    string binary,
    string remoteName,
    CancellationToken cancellationToken)
  {
    try
    {
      Dictionary<string, string> config =
        await _rcloneConfigWizardService.ReadRemoteConfigAsync(binary, remoteName, cancellationToken);
      return config.Count > 0;
    }
    catch
    {
      return false;
    }
  }

  private Task<bool> SourceRemoteExistsInRcloneConfigAsync(
    MountProfile mountProfile,
    string remoteAlias,
    CancellationToken cancellationToken)
  {
    string binary = string.IsNullOrWhiteSpace(mountProfile.RcloneBinaryPath)
      ? "rclone"
      : mountProfile.RcloneBinaryPath;

    return RemoteExistsInRcloneConfigAsync(binary, remoteAlias, cancellationToken);
  }

  private async Task RepairMissingSourceRemoteInRcloneConfigAsync(
    MountProfile mountProfile,
    CancellationToken cancellationToken)
  {
    MountProfile? remote = ResolveAssociatedRemote(mountProfile);
    if (remote is null)
    {
      throw new InvalidOperationException("No linked remote profile is available for repair.");
    }

    string? remoteAlias = GetRemoteAlias(remote);
    if (string.IsNullOrWhiteSpace(remoteAlias))
    {
      throw new InvalidOperationException("Could not extract remote alias for repair.");
    }

    if (string.IsNullOrWhiteSpace(remote.BackendName))
    {
      throw new InvalidOperationException("Linked remote is missing backend type.");
    }

    if (remote.BackendOptions.Count == 0)
    {
      throw new InvalidOperationException("Linked remote has no backend options to write.");
    }

    string binary = string.IsNullOrWhiteSpace(remote.RcloneBinaryPath)
      ? string.IsNullOrWhiteSpace(mountProfile.RcloneBinaryPath) ? "rclone" : mountProfile.RcloneBinaryPath
      : remote.RcloneBinaryPath;

    List<RcloneBackendOptionInput> repairOptions = remote.BackendOptions
      .Select(option =>
      {
        RcloneBackendOption backendOption = new()
        {
          Name = option.Key,
          IsPassword = IsLikelyPasswordOption(option.Key),
        };

        RcloneBackendOptionInput input = new(backendOption)
        {
          Value = option.Value,
        };

        if (input.IsPassword)
        {
          input.ConfirmValue = option.Value;
        }

        return input;
      })
      .ToList();

    bool remoteExists = await RemoteExistsInRcloneConfigAsync(binary, remoteAlias, cancellationToken);
    if (remoteExists)
    {
      await _rcloneBackendService.UpdateRemoteAsync(binary, remoteAlias, repairOptions, cancellationToken);
      return;
    }

    await _rcloneBackendService.CreateRemoteAsync(
      binary,
      remoteAlias,
      remote.BackendName,
      repairOptions,
      cancellationToken);
  }

  private static bool IsLikelyPasswordOption(string optionName)
  {
    return optionName.Contains("pass", StringComparison.OrdinalIgnoreCase)
           || optionName.Contains("password", StringComparison.OrdinalIgnoreCase)
           || optionName.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || optionName.Contains("token", StringComparison.OrdinalIgnoreCase);
  }

  private async Task RefreshSourceRemoteConfigHealthAsync(
    MountProfile profile,
    CancellationToken cancellationToken = default)
  {
    if (profile.IsRemoteDefinition || !RequiresRemoteAssociation(profile))
    {
      IsSourceRemoteMissingFromRcloneConfig = false;
      CanRepairMissingSourceRemote = false;
      SourceRemoteConfigStatus = string.Empty;
      NotifyCommandStateChanged();
      return;
    }

    if (!TryGetRemoteAliasFromSource(profile.Source, out string remoteAlias, out _)
        || string.IsNullOrWhiteSpace(remoteAlias))
    {
      IsSourceRemoteMissingFromRcloneConfig = true;
      CanRepairMissingSourceRemote = false;
      SourceRemoteConfigStatus = "Mount source has no valid remote alias.";
      NotifyCommandStateChanged();
      return;
    }

    MountProfile? linkedRemote = ResolveAssociatedRemote(profile);
    if (linkedRemote is null)
    {
      IsSourceRemoteMissingFromRcloneConfig = false;
      CanRepairMissingSourceRemote = false;
      SourceRemoteConfigStatus = string.Empty;
      NotifyCommandStateChanged();
      return;
    }

    bool existsInRcloneConfig = await _sourceRemoteExistsInRcloneConfigRunner(profile, remoteAlias, cancellationToken);

    if (!ReferenceEquals(SelectedProfile, profile))
    {
      return;
    }

    IsSourceRemoteMissingFromRcloneConfig = !existsInRcloneConfig;
    CanRepairMissingSourceRemote = !existsInRcloneConfig
                                   && linkedRemote.IsRemoteDefinition
                                   && !string.IsNullOrWhiteSpace(linkedRemote.BackendName)
                                   && linkedRemote.BackendOptions.Count > 0;
    SourceRemoteConfigStatus = existsInRcloneConfig
      ? string.Empty
      : $"Remote '{remoteAlias}' is missing from rclone config.";
    NotifyCommandStateChanged();
  }

  private MountProfile? ResolveAssociatedRemote(MountProfile profile)
  {
    if (!TryGetRemoteAliasFromSource(profile.Source, out string remoteAlias, out _))
    {
      return null;
    }

    return RemoteProfiles.FirstOrDefault(remote =>
                                           string.Equals(
                                             GetRemoteAlias(remote),
                                             remoteAlias,
                                             StringComparison.OrdinalIgnoreCase));
  }

  private bool RequiresRemoteAssociation(MountProfile profile)
  {
    return !profile.IsRemoteDefinition &&
           profile.Type is MountType.RcloneAuto or MountType.RcloneFuse or MountType.RcloneNfs &&
           profile.QuickConnectMode is QuickConnectMode.None;
  }

  private bool HasValidRemoteAssociation(MountProfile profile)
  {
    return !RequiresRemoteAssociation(profile) || ResolveAssociatedRemote(profile) is not null;
  }

  private bool HasAnyInvalidMountRemoteAssociations()
  {
    return Profiles.Where(IsMountProfileCandidate).Any(profile => !HasValidRemoteAssociation(profile));
  }

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
      if (ShowDiagnosticsView || ShowSettingsView || ShowDashboard)
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
          MountProfile? candidate = _rememberedRemoteProfile;
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
          MountProfile? candidate = _rememberedMountProfile;
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
    MountProfile? previousRemote = SelectedRemoteProfile ?? _rememberedRemoteProfile;

    RemoteProfiles.Clear();
    foreach (MountProfile profile in Profiles.Where(IsRemoteProfileCandidate))
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

    MountProfile replacement = previousRemote is not null && RemoteProfiles.Contains(previousRemote)
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
    MountProfile? previousMount = SelectedMountProfile ?? _rememberedMountProfile;

    MountProfiles.Clear();
    DashboardMountCards.Clear();
    foreach (MountProfile profile in Profiles.Where(IsMountProfileCandidate))
    {
      MountProfiles.Add(profile);
      DashboardMountCards.Add(
        new DashboardMountCardViewModel(
          profile,
          DashboardNavigateToMount,
          DashboardEditMount,
          DashboardEditRemote,
          DashboardHasLinkedRemote,
          DashboardStartMountAsync,
          DashboardStopMountAsync,
          DashboardRefreshCacheAsync,
          DashboardRevealInFinderAsync));
    }

    OnPropertyChanged(nameof(DashboardMountCards));
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

    MountProfile replacement = previousMount is not null && MountProfiles.Contains(previousMount)
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
    HashSet<string> knownAliases = new(StringComparer.OrdinalIgnoreCase);
    foreach (MountProfile remote in Profiles.Where(IsRemoteProfileCandidate))
    {
      string? alias = GetRemoteAlias(remote);
      if (!string.IsNullOrWhiteSpace(alias))
      {
        knownAliases.Add(alias);
      }
    }

    List<string> missingAliases = Profiles
      .Where(IsMountProfileCandidate)
      .Select(GetRemoteAlias)
      .Where(alias => !string.IsNullOrWhiteSpace(alias))
      .Cast<string>()
      .Where(alias => !knownAliases.Contains(alias))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    foreach (string alias in missingAliases)
    {
      MountProfile remoteProfile = CreateRemoteDefinitionProfile(alias);
      Profiles.Add(remoteProfile);
      _profileLogs[remoteProfile.Id] = new List<ProfileLogEvent>();
      _profileScripts[remoteProfile.Id] = string.Empty;
      knownAliases.Add(alias);
    }
  }

  private static string DefaultMountPoint(string name)
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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
    public WindowCloseBehavior? WindowCloseBehavior { get; set; }
  }
}
