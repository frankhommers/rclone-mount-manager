using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public partial class MountProfile : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = "New profile";

    [ObservableProperty]
    private MountType _type = MountType.RcloneAuto;

    [ObservableProperty]
    private string _source = "remote:bucket";

    [ObservableProperty]
    private string _mountPoint = "~/Mounts/rclone";

    [ObservableProperty]
    private string _extraOptions = string.Empty;

    [ObservableProperty]
    private string _selectedReliabilityPresetId = ReliabilityPolicyPreset.NormalId;

    [ObservableProperty]
    private Dictionary<string, string> _mountOptions = new();

    [ObservableProperty]
    private HashSet<string> _pinnedMountOptions = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _rcloneBinaryPath = "rclone";

    [ObservableProperty]
    private QuickConnectMode _quickConnectMode;

    [ObservableProperty]
    private string _quickConnectEndpoint = string.Empty;

    [ObservableProperty]
    private string _quickConnectPort = string.Empty;

    [ObservableProperty]
    private string _quickConnectUsername = string.Empty;

    [ObservableProperty]
    private string _quickConnectPassword = string.Empty;

    [ObservableProperty]
    private bool _allowInsecurePasswordsInScript;

    [ObservableProperty]
    private bool _startAtLogin;

    [ObservableProperty]
    private bool _isRemoteDefinition;

    [ObservableProperty]
    private string _backendName = string.Empty;

    [ObservableProperty]
    private Dictionary<string, string> _backendOptions = new();

    [ObservableProperty]
    private int _rcPort;

    [ObservableProperty]
    private bool _enableRemoteControl = true;

    [ObservableProperty]
    private bool _isMounted;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _lastStatus = "Idle";

    [ObservableProperty]
    private ProfileRuntimeState _runtimeState = ProfileRuntimeState.Unknown;

    public string DisplayName => $"{Name} ({Type})";

    public string RemoteSidebarBackend =>
        !string.IsNullOrWhiteSpace(BackendName) ? BackendName : string.Empty;

    public bool HasRemoteSidebarBackend => !string.IsNullOrWhiteSpace(RemoteSidebarBackend);

    public string RemoteSidebarSubtitle
    {
        get
        {
            if (!IsRemoteDefinition)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(QuickConnectEndpoint))
            {
                return QuickConnectEndpoint;
            }

            string[] hostKeys = ["url", "host", "endpoint", "server"];
            foreach (string key in hostKeys)
            {
                if (BackendOptions.TryGetValue(key, out string? value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }

    public bool HasRemoteSidebarSubtitle => !string.IsNullOrWhiteSpace(RemoteSidebarSubtitle);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnTypeChanged(MountType value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(RemoteSidebarSubtitle));
        OnPropertyChanged(nameof(HasRemoteSidebarSubtitle));
    }

    partial void OnSourceChanged(string value)
    {
        OnPropertyChanged(nameof(RemoteSidebarSubtitle));
        OnPropertyChanged(nameof(HasRemoteSidebarSubtitle));
    }

    partial void OnBackendNameChanged(string value)
    {
        OnPropertyChanged(nameof(RemoteSidebarBackend));
        OnPropertyChanged(nameof(HasRemoteSidebarBackend));
    }

    partial void OnQuickConnectEndpointChanged(string value)
    {
        OnPropertyChanged(nameof(RemoteSidebarSubtitle));
        OnPropertyChanged(nameof(HasRemoteSidebarSubtitle));
    }

    partial void OnBackendOptionsChanged(Dictionary<string, string> value)
    {
        OnPropertyChanged(nameof(RemoteSidebarSubtitle));
        OnPropertyChanged(nameof(HasRemoteSidebarSubtitle));
    }

    partial void OnIsRemoteDefinitionChanged(bool value)
    {
        OnPropertyChanged(nameof(RemoteSidebarSubtitle));
        OnPropertyChanged(nameof(HasRemoteSidebarSubtitle));
    }

    partial void OnRuntimeStateChanged(ProfileRuntimeState value)
    {
        OnPropertyChanged(nameof(SidebarStatusColor));
    }

    public string SidebarStatusColor => RuntimeState.Lifecycle switch
    {
        MountLifecycleState.Mounted when RuntimeState.Health is MountHealthState.Healthy => "#22C55E",
        MountLifecycleState.Mounted when RuntimeState.Health is MountHealthState.Degraded => "#EAB308",
        MountLifecycleState.Mounted => "#22C55E",
        MountLifecycleState.Mounting => "#F97316",
        MountLifecycleState.Failed => "#EF4444",
        _ => "#6B7280",
    };

    public override string ToString() => DisplayName;
}
