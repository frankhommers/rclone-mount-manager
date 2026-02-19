using CommunityToolkit.Mvvm.ComponentModel;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.ViewModels;
using System;

namespace RcloneMountManager.ViewModels;

public partial class MountOptionInputViewModel : TypedOptionViewModel
{
    private readonly RcloneOption _option;

    public MountOptionInputViewModel(RcloneOption option, string? currentValue = null)
    {
        _option = option;
        _isSet = !string.IsNullOrEmpty(currentValue);
        InitializeTypedValues(currentValue);
    }

    protected override IRcloneOptionDefinition Option => _option;

    public string FlagName => "--" + _option.Name.Replace('_', '-');

    public override string Label
    {
        get
        {
            var flag = FlagName;
            if (_option.Required) flag += " (required)";
            return flag;
        }
    }

    [ObservableProperty]
    private bool _isSet;

    public override bool HasNonDefaultValue =>
        IsSet && !string.IsNullOrEmpty(Value) &&
        !string.Equals(Value, DefaultStr, StringComparison.OrdinalIgnoreCase);

    protected override void SyncToString(string newValue)
    {
        base.SyncToString(newValue);
        if (!string.IsNullOrEmpty(newValue) &&
            !string.Equals(newValue, DefaultStr, StringComparison.OrdinalIgnoreCase))
        {
            IsSet = true;
        }
    }

    protected override void OnValueChangedExtra(string value)
    {
        if (!string.IsNullOrEmpty(value) &&
            !string.Equals(value, DefaultStr, StringComparison.OrdinalIgnoreCase))
        {
            IsSet = true;
        }
    }

    protected override void ResetToDefault()
    {
        base.ResetToDefault();
        IsSet = false;
    }
}
