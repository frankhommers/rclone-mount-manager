using Avalonia;
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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly MountManagerService _mountManagerService = new();
    private readonly LaunchAgentService _launchAgentService = new();
    private readonly RcloneBackendService _rcloneBackendService = new();
    public MountOptionsViewModel MountOptionsVm { get; } = new();
    private readonly string _profilesFilePath;
    private readonly Dictionary<string, List<string>> _profileLogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _profileScripts = new(StringComparer.OrdinalIgnoreCase);
    private MountProfile? _observedProfile;
    private bool _isLoadingProfiles;

    [ObservableProperty]
    private MountProfile _selectedProfile;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _generatedScript = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _showAdvanced;

    [ObservableProperty]
    private string _selectedThemeMode = "Follow system";

    [ObservableProperty]
    private RcloneBackendInfo? _selectedBackend;

    [ObservableProperty]
    private string _newRemoteName = string.Empty;

    [ObservableProperty]
    private bool _showAdvancedBackendOptions;

    [ObservableProperty]
    private bool _hasPendingChanges;

    public ObservableCollection<MountProfile> Profiles { get; } = new();
    public ObservableCollection<MountType> MountTypes { get; } = new(Enum.GetValues<MountType>());
    public ObservableCollection<QuickConnectMode> QuickConnectModes { get; } = new(Enum.GetValues<QuickConnectMode>());
    public ObservableCollection<string> ThemeModes { get; } = new() { "Follow system", "Dark", "Light" };
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<RcloneBackendInfo> AvailableBackends { get; } = new();
    public ObservableCollection<RcloneBackendOptionInput> BackendOptionInputs { get; } = new();

    public MainWindowViewModel()
    {
        _profilesFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RcloneMountManager",
            "profiles.json");

        Profiles.CollectionChanged += OnProfilesCollectionChanged;
        LoadProfiles();

        if (Profiles.Count == 0)
        {
            var defaultProfile = CreateDefaultProfile();
            Profiles.Add(defaultProfile);
            _profileLogs[defaultProfile.Id] = new List<string>();
            _profileScripts[defaultProfile.Id] = string.Empty;
        }

        SelectedProfile = Profiles[0];
        HasPendingChanges = false;
        AppendLog($"Profiles file: {_profilesFilePath}");

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshBackendsAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"ERR: Could not load backend list: {ex.Message}");
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
                await MountOptionsVm.LoadOptionsAsync(binary, SelectedProfile?.MountOptions ?? new Dictionary<string, string>(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppendLog($"ERR: Could not load mount options: {ex.Message}");
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

    public bool IsStartupSupported => _launchAgentService.IsSupported;

    public string StartupButtonText => SelectedProfile?.StartAtLogin is true ? "Disable start at login" : "Enable start at login";

    public string SaveChangesButtonText => HasPendingChanges ? "Save changes *" : "Save changes";

    public bool HasBackendOptions => BackendOptionInputs.Count > 0;

    public string SelectedBackendDescription => SelectedBackend?.Details ?? string.Empty;

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new MountProfile
        {
            Name = $"Profile {Profiles.Count + 1}",
            Type = MountType.RcloneAuto,
            Source = "remote:bucket",
            MountPoint = DefaultMountPoint("new-mount"),
            ExtraOptions = "--vfs-cache-mode full",
            MountOptions = GetDefaultMountOptions(MountType.RcloneAuto),
            QuickConnectMode = QuickConnectMode.None,
        };

        Profiles.Add(profile);
        _profileLogs[profile.Id] = new List<string>();
        _profileScripts[profile.Id] = string.Empty;
        SelectedProfile = profile;
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshBackends))]
    private async Task RefreshBackendsAsync()
    {
        var binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
        var backends = await _rcloneBackendService.GetBackendsAsync(binary, CancellationToken.None);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvailableBackends.Clear();
            foreach (var backend in backends)
            {
                AvailableBackends.Add(backend);
            }

            if (AvailableBackends.Count > 0)
            {
                SelectedBackend = AvailableBackends[0];
            }

            AppendLog($"Loaded {AvailableBackends.Count} rclone backend types.");
        });
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
            await _rcloneBackendService.CreateRemoteAsync(
                binary,
                NewRemoteName,
                SelectedBackend.Name,
                BackendOptionInputs,
                cancellationToken);

            activeProfile.Type = MountType.RcloneAuto;
            activeProfile.QuickConnectMode = QuickConnectMode.None;
            activeProfile.Source = $"{NewRemoteName.Trim()}:/";

            AppendLog($"Created remote '{NewRemoteName}' ({SelectedBackend.Name}).");
            StatusText = $"Remote '{NewRemoteName}' created.";
            MarkDirty();
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
        ShowAdvanced = false;
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
        ShowAdvanced = false;
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
        ShowAdvanced = false;
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
        ShowAdvanced = false;
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
        ShowAdvanced = false;
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
        ShowAdvanced = false;
        StatusText = "Preset loaded: NFS.";
        NotifyLabelsChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveProfile))]
    private void RemoveProfile()
    {
        if (Profiles.Count <= 1)
        {
            return;
        }

        var index = Profiles.IndexOf(SelectedProfile);
        var removedId = SelectedProfile.Id;
        Profiles.Remove(SelectedProfile);
        _profileLogs.Remove(removedId);
        _profileScripts.Remove(removedId);
        SelectedProfile = Profiles[Math.Max(0, index - 1)];
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task StartMountAsync()
    {
        await RunBusyActionAsync(async cancellationToken =>
        {
            AppendLog($"Starting mount '{SelectedProfile.Name}'...");
            await _mountManagerService.StartAsync(SelectedProfile, AppendLog, cancellationToken);
            await RefreshStatusInternalAsync(cancellationToken);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task StopMountAsync()
    {
        await RunBusyActionAsync(async cancellationToken =>
        {
            AppendLog($"Stopping mount '{SelectedProfile.Name}'...");
            await _mountManagerService.StopAsync(SelectedProfile, AppendLog, cancellationToken);
            await RefreshStatusInternalAsync(cancellationToken);
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
        await RunBusyActionAsync(async cancellationToken =>
        {
            AppendLog($"Testing connection for '{SelectedProfile.Name}'...");
            await _mountManagerService.TestConnectionAsync(SelectedProfile, AppendLog, cancellationToken);
            StatusText = "Connectivity test passed.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private void GenerateScript()
    {
        GeneratedScript = _mountManagerService.GenerateScript(SelectedProfile);
        _profileScripts[SelectedProfile.Id] = GeneratedScript;
        AppendLog("Generated shell script preview.");
    }

    [RelayCommand(CanExecute = nameof(CanSaveScript))]
    private async Task SaveScriptAsync()
    {
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

        AppendLog($"Script saved to: {scriptPath}");
    }

    [RelayCommand(CanExecute = nameof(CanToggleStartup))]
    private async Task ToggleStartupAsync()
    {
        await RunBusyActionAsync(async cancellationToken =>
        {
            if (!IsStartupSupported)
            {
                throw new InvalidOperationException("Start at login is currently supported on macOS only.");
            }

            if (SelectedProfile.StartAtLogin)
            {
                await _launchAgentService.DisableAsync(SelectedProfile, AppendLog, cancellationToken);
                SelectedProfile.StartAtLogin = false;
                StatusText = "Start at login disabled.";
            }
            else
            {
                var script = _mountManagerService.GenerateScript(SelectedProfile);
                await _launchAgentService.EnableAsync(SelectedProfile, script, AppendLog, cancellationToken);
                SelectedProfile.StartAtLogin = true;
                StatusText = "Start at login enabled.";
            }

            OnPropertyChanged(nameof(StartupButtonText));
        });
    }

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private void SaveChanges()
    {
        SaveProfiles();
        HasPendingChanges = false;
        StatusText = "Profile changes saved.";
        AppendLog("Saved profile changes.");
        OnPropertyChanged(nameof(SaveChangesButtonText));
        NotifyCommandStateChanged();
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
            await action(cancellationTokenSource.Token);
            StatusText = "Operation completed.";
        }
        catch (Exception ex)
        {
            StatusText = "Operation failed.";
            AppendLog($"ERR: {ex.Message}");
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
        var mounted = await _mountManagerService.IsMountedAsync(profile.MountPoint, cancellationToken);
        var running = _mountManagerService.IsRunning(profile.MountPoint);

        profile.IsMounted = mounted;
        profile.IsRunning = running;
        profile.LastStatus = $"Mounted: {mounted}, Running: {running}";

        StatusText = profile.LastStatus;
        AppendLog($"Status for '{profile.Name}': {profile.LastStatus}");
    }

    private void AppendLog(string line)
    {
        var profileId = SelectedProfile?.Id;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        AppendLog(profileId, line);
    }

    private void AppendLog(string profileId, string line)
    {
        if (line.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase))
        {
            Log.Error("{Message}", line);
        }
        else
        {
            Log.Information("{Message}", line);
        }

        var timeStamped = $"[{DateTime.Now:HH:mm:ss}] {line}";

        if (!_profileLogs.TryGetValue(profileId, out var logEntries))
        {
            logEntries = new List<string>();
            _profileLogs[profileId] = logEntries;
        }

        logEntries.Add(timeStamped);
        while (logEntries.Count > 250)
        {
            logEntries.RemoveAt(0);
        }

        if (SelectedProfile is null || !string.Equals(SelectedProfile.Id, profileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Logs.Add(timeStamped);
        while (Logs.Count > 250)
        {
            Logs.RemoveAt(0);
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
        RefreshBackendsCommand.NotifyCanExecuteChanged();
        CreateRemoteCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowAdvancedChanged(bool value)
    {
        NotifyCommandStateChanged();
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

    private bool CanRemoveProfile() => Profiles.Count > 1 && !IsBusy;

    private bool CanRunActions() =>
        !IsBusy &&
        SelectedProfile is not null &&
        !string.IsNullOrWhiteSpace(SelectedProfile.Source) &&
        !string.IsNullOrWhiteSpace(SelectedProfile.MountPoint);

    private bool CanTestConnection() =>
        !IsBusy &&
        SelectedProfile is not null &&
        SelectedProfile.Type is MountType.RcloneAuto &&
        !string.IsNullOrWhiteSpace(SelectedProfile.Source);

    private bool CanSaveScript() => !IsBusy && !string.IsNullOrWhiteSpace(GeneratedScript);

    private bool CanToggleStartup() => !IsBusy && SelectedProfile is not null && IsStartupSupported;

    private bool CanSaveChanges() => !IsBusy && HasPendingChanges;

    private bool CanRefreshBackends() => !IsBusy;

    private bool CanCreateRemote() => !IsBusy && SelectedBackend is not null && !string.IsNullOrWhiteSpace(NewRemoteName);

    partial void OnSelectedBackendChanged(RcloneBackendInfo? value)
    {
        OnPropertyChanged(nameof(SelectedBackendDescription));
        BackendOptionInputs.Clear();
        if (value is null)
        {
            OnPropertyChanged(nameof(HasBackendOptions));
            NotifyCommandStateChanged();
            return;
        }

        if (string.IsNullOrWhiteSpace(NewRemoteName))
        {
            NewRemoteName = $"{value.Name}-remote";
        }

        foreach (var option in value.Options
                     .Where(o => o.Required || !o.Advanced || ShowAdvancedBackendOptions)
                     .OrderByDescending(o => o.Required)
                     .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            BackendOptionInputs.Add(new RcloneBackendOptionInput(option));
        }

        OnPropertyChanged(nameof(HasBackendOptions));
        NotifyCommandStateChanged();
    }

    partial void OnShowAdvancedBackendOptionsChanged(bool value)
    {
        OnSelectedBackendChanged(SelectedBackend);
    }

    partial void OnNewRemoteNameChanged(string value)
    {
        NotifyCommandStateChanged();
    }

    partial void OnSelectedProfileChanged(MountProfile value)
    {
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

        if (IsStartupSupported)
        {
            value.StartAtLogin = _launchAgentService.IsEnabled(value);
        }

        Logs.Clear();
        if (_profileLogs.TryGetValue(value.Id, out var profileEntries))
        {
            foreach (var entry in profileEntries)
            {
                Logs.Add(entry);
            }
        }

        if (_profileScripts.TryGetValue(value.Id, out var script))
        {
            GeneratedScript = script;
        }
        else
        {
            GeneratedScript = string.Empty;
        }

        MountOptionsVm.UpdateFromProfile(value.MountOptions);

        OnPropertyChanged(nameof(StartupButtonText));
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
            or nameof(MountProfile.StartAtLogin))
        {
            MarkDirty();
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
                    MountOptions = saved.MountOptions ?? new Dictionary<string, string>(),
                    RcloneBinaryPath = saved.RcloneBinaryPath,
                    QuickConnectMode = QuickConnectMode.None,
                    QuickConnectEndpoint = string.Empty,
                    QuickConnectPort = string.Empty,
                    QuickConnectUsername = string.Empty,
                    QuickConnectPassword = string.Empty,
                    AllowInsecurePasswordsInScript = saved.AllowInsecurePasswordsInScript,
                    StartAtLogin = saved.StartAtLogin,
                };

                Profiles.Add(profile);
                _profileLogs[profile.Id] = new List<string>();
                _profileScripts[profile.Id] = string.Empty;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"ERR: Could not load profiles: {ex.Message}");
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

            // Sync typed options from ViewModel back to current profile
            if (SelectedProfile is not null)
            {
                SelectedProfile.MountOptions = MountOptionsVm.GetNonDefaultValues();
            }

            var payload = Profiles
                .Select(profile => new PersistedProfile
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Type = profile.Type,
                    Source = profile.Source,
                    MountPoint = profile.MountPoint,
                    ExtraOptions = profile.ExtraOptions,
                    MountOptions = profile.MountOptions,
                    RcloneBinaryPath = profile.RcloneBinaryPath,
                    QuickConnectMode = profile.QuickConnectMode,
                    QuickConnectEndpoint = profile.QuickConnectEndpoint,
                    QuickConnectPort = profile.QuickConnectPort,
                    QuickConnectUsername = profile.QuickConnectUsername,
                    QuickConnectPassword = profile.QuickConnectPassword,
                    AllowInsecurePasswordsInScript = profile.AllowInsecurePasswordsInScript,
                    StartAtLogin = profile.StartAtLogin,
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
            AppendLog($"ERR: Could not save profiles: {ex.Message}");
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
        return type switch
        {
            MountType.MacOsNfs => new Dictionary<string, string>(),
            _ => new Dictionary<string, string>
            {
                ["vfs_cache_mode"] = "full",
                ["dir_cache_time"] = "10m",
            },
        };
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

    private void ResetQuickConnectFields()
    {
        SelectedProfile.QuickConnectEndpoint = string.Empty;
        SelectedProfile.QuickConnectPort = string.Empty;
        SelectedProfile.QuickConnectUsername = string.Empty;
        SelectedProfile.QuickConnectPassword = string.Empty;
    }

    private static string BuildSafeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(fileName.Length);

        foreach (var ch in fileName)
        {
            builder.Append(invalidChars.Contains(ch) ? '-' : ch);
        }

        return builder.Length == 0 ? "mount-script" : builder.ToString();
    }

    private static string DefaultMountPoint(string name)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Mounts", name);
    }

    private sealed class PersistedProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Profile";
        public MountType Type { get; set; } = MountType.RcloneAuto;
        public string Source { get; set; } = string.Empty;
        public string MountPoint { get; set; } = string.Empty;
        public string ExtraOptions { get; set; } = string.Empty;
        public Dictionary<string, string> MountOptions { get; set; } = new();
        public string RcloneBinaryPath { get; set; } = "rclone";
        public QuickConnectMode QuickConnectMode { get; set; } = QuickConnectMode.None;
        public string QuickConnectEndpoint { get; set; } = string.Empty;
        public string QuickConnectPort { get; set; } = string.Empty;
        public string QuickConnectUsername { get; set; } = string.Empty;
        public string QuickConnectPassword { get; set; } = string.Empty;
        public bool AllowInsecurePasswordsInScript { get; set; }
        public bool StartAtLogin { get; set; }
    }
}
