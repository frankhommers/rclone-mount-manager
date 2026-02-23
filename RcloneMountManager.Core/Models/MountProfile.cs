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
    private bool _isMounted;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _lastStatus = "Idle";

    [ObservableProperty]
    private ProfileRuntimeState _runtimeState = ProfileRuntimeState.Unknown;

    public string DisplayName => $"{Name} ({Type})";

    public string RemoteSidebarSubtitle
    {
        get
        {
            if (!IsRemoteDefinition || string.IsNullOrWhiteSpace(Source))
            {
                return string.Empty;
            }

            var separatorIndex = Source.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= Source.Length - 1)
            {
                return string.Empty;
            }

            var target = Source[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(target) || string.Equals(target, "/", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return $"Path: {target}";
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

    partial void OnIsRemoteDefinitionChanged(bool value)
    {
        OnPropertyChanged(nameof(RemoteSidebarSubtitle));
        OnPropertyChanged(nameof(HasRemoteSidebarSubtitle));
    }

    public override string ToString() => DisplayName;
}
