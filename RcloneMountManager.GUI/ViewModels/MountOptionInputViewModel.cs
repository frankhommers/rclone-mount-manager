using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneMountManager.Core.Helpers;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.Generic;

namespace RcloneMountManager.ViewModels;

public partial class MountOptionInputViewModel : ObservableObject
{
    private readonly RcloneOption _option;
    private bool _syncing;

    public MountOptionInputViewModel(RcloneOption option, string? currentValue = null)
    {
        _option = option;
        _value = currentValue ?? string.Empty;
        _isSet = !string.IsNullOrEmpty(currentValue);
        InitializeTypedValues(currentValue);
    }

    public string Name => _option.Name;
    public string FlagName => "--" + _option.Name.Replace('_', '-');
    public string Help => _option.Help;
    public string DefaultStr => _option.DefaultStr;
    public bool IsAdvanced => _option.Advanced;
    public OptionControlType ControlType => _option.GetControlType();
    public IReadOnlyList<string>? EnumValues => _option.GetEnumValues();
    public static IReadOnlyList<string> SizeSuffixUnits => SizeSuffixHelper.Units;

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

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private decimal? _numericValue;

    [ObservableProperty]
    private TimeSpan? _durationValue;

    [ObservableProperty]
    private decimal? _sizeSuffixNumericValue;

    [ObservableProperty]
    private string _sizeSuffixUnit = "B";

    [ObservableProperty]
    private string? _selectedEnumValue;

    public bool HasNonDefaultValue =>
        IsSet && !string.IsNullOrEmpty(Value) &&
        !string.Equals(Value, DefaultStr, StringComparison.OrdinalIgnoreCase);

    private void InitializeTypedValues(string? currentValue)
    {
        _syncing = true;
        try
        {
            switch (ControlType)
            {
                case OptionControlType.Toggle:
                    _boolValue = string.Equals(currentValue, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case OptionControlType.Numeric:
                    _numericValue = decimal.TryParse(currentValue, out var num) ? num : null;
                    break;
                case OptionControlType.Duration:
                    _durationValue = string.IsNullOrEmpty(currentValue) ? null : DurationHelper.Parse(currentValue);
                    break;
                case OptionControlType.SizeSuffix:
                    if (!string.IsNullOrEmpty(currentValue))
                    {
                        var (sv, su) = SizeSuffixHelper.Parse(currentValue);
                        _sizeSuffixNumericValue = sv;
                        _sizeSuffixUnit = su;
                    }
                    break;
                case OptionControlType.ComboBox:
                    _selectedEnumValue = string.IsNullOrEmpty(currentValue) ? null : currentValue;
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnBoolValueChanged(bool value)
    {
        if (_syncing) return;
        SyncToString(value ? "true" : "false");
    }

    partial void OnNumericValueChanged(decimal? value)
    {
        if (_syncing) return;
        SyncToString(value?.ToString() ?? string.Empty);
    }

    partial void OnDurationValueChanged(TimeSpan? value)
    {
        if (_syncing) return;
        SyncToString(value.HasValue ? DurationHelper.Format(value.Value) : string.Empty);
    }

    partial void OnSizeSuffixNumericValueChanged(decimal? value)
    {
        if (_syncing) return;
        SyncSizeSuffix();
    }

    partial void OnSizeSuffixUnitChanged(string value)
    {
        if (_syncing) return;
        SyncSizeSuffix();
    }

    partial void OnSelectedEnumValueChanged(string? value)
    {
        if (_syncing) return;
        SyncToString(value ?? string.Empty);
    }

    private void SyncSizeSuffix()
    {
        var val = SizeSuffixNumericValue ?? 0m;
        SyncToString(SizeSuffixHelper.Format(val, SizeSuffixUnit));
    }

    private void SyncToString(string newValue)
    {
        _syncing = true;
        try
        {
            Value = newValue;
            if (!string.IsNullOrEmpty(newValue) &&
                !string.Equals(newValue, DefaultStr, StringComparison.OrdinalIgnoreCase))
            {
                IsSet = true;
            }
            OnPropertyChanged(nameof(HasNonDefaultValue));
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnValueChanged(string value)
    {
        if (_syncing) return;

        if (!string.IsNullOrEmpty(value) &&
            !string.Equals(value, DefaultStr, StringComparison.OrdinalIgnoreCase))
        {
            IsSet = true;
        }
        OnPropertyChanged(nameof(HasNonDefaultValue));

        _syncing = true;
        try
        {
            switch (ControlType)
            {
                case OptionControlType.Toggle:
                    BoolValue = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case OptionControlType.Numeric:
                    NumericValue = decimal.TryParse(value, out var n) ? n : null;
                    break;
                case OptionControlType.Duration:
                    DurationValue = string.IsNullOrEmpty(value) ? null : DurationHelper.Parse(value);
                    break;
                case OptionControlType.SizeSuffix:
                    if (!string.IsNullOrEmpty(value))
                    {
                        var (sv, su) = SizeSuffixHelper.Parse(value);
                        SizeSuffixNumericValue = sv;
                        SizeSuffixUnit = su;
                    }
                    else
                    {
                        SizeSuffixNumericValue = null;
                        SizeSuffixUnit = "B";
                    }
                    break;
                case OptionControlType.ComboBox:
                    SelectedEnumValue = string.IsNullOrEmpty(value) ? null : value;
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        Value = string.Empty;
        IsSet = false;
        OnPropertyChanged(nameof(HasNonDefaultValue));
    }
}
