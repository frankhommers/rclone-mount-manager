using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.Generic;

namespace RcloneMountManager.ViewModels;

public partial class MountOptionInputViewModel : ObservableObject
{
    private readonly RcloneOption _option;

    public MountOptionInputViewModel(RcloneOption option, string? currentValue = null)
    {
        _option = option;
        _value = currentValue ?? string.Empty;
        _isSet = !string.IsNullOrEmpty(currentValue);
    }

    public string Name => _option.Name;
    public string FlagName => "--" + _option.Name.Replace('_', '-');
    public string Help => _option.Help;
    public string DefaultStr => _option.DefaultStr;
    public bool IsAdvanced => _option.Advanced;
    public OptionControlType ControlType => _option.GetControlType();
    public IReadOnlyList<string>? EnumValues => _option.GetEnumValues();

    public string Label
    {
        get
        {
            var flag = FlagName;
            if (_option.Required) flag += " (required)";
            return flag;
        }
    }

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _isSet;

    public bool HasNonDefaultValue =>
        IsSet && !string.IsNullOrEmpty(Value) &&
        !string.Equals(Value, DefaultStr, StringComparison.OrdinalIgnoreCase);

    partial void OnValueChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !string.Equals(value, DefaultStr, StringComparison.OrdinalIgnoreCase))
        {
            IsSet = true;
        }

        OnPropertyChanged(nameof(HasNonDefaultValue));
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        Value = string.Empty;
        IsSet = false;
        OnPropertyChanged(nameof(HasNonDefaultValue));
    }
}
