using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneMountManager.Core.Helpers;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.Generic;

namespace RcloneMountManager.Core.ViewModels;

public abstract partial class TypedOptionViewModel : ObservableObject
{
    private bool _syncing;

    protected abstract IRcloneOptionDefinition Option { get; }

    public string Name => Option.Name;
    public string Help => Option.Help;
    public string DefaultStr => Option.DefaultStr;
    public bool IsAdvanced => Option.Advanced;
    public OptionControlType ControlType => Option.GetControlType();
    public IReadOnlyList<string>? EnumValues => Option.GetEnumValues();
    public static IReadOnlyList<string> SizeSuffixUnits => SizeSuffixHelper.Units;

    public abstract string Label { get; }

    [ObservableProperty]
    private string _value = string.Empty;

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

    public virtual bool HasNonDefaultValue =>
        !string.IsNullOrEmpty(Value) &&
        !string.Equals(Value, DefaultStr, StringComparison.OrdinalIgnoreCase);

    protected void InitializeTypedValues(string? currentValue)
    {
        _syncing = true;
        try
        {
            Value = currentValue ?? string.Empty;
            switch (ControlType)
            {
                case OptionControlType.Toggle:
                    BoolValue = string.Equals(currentValue, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case OptionControlType.Numeric:
                    NumericValue = decimal.TryParse(currentValue, out var num) ? num : null;
                    break;
                case OptionControlType.Duration:
                    DurationValue = string.IsNullOrEmpty(currentValue) ? null : DurationHelper.Parse(currentValue);
                    break;
                case OptionControlType.SizeSuffix:
                    if (!string.IsNullOrEmpty(currentValue))
                    {
                        var (sv, su) = SizeSuffixHelper.Parse(currentValue);
                        SizeSuffixNumericValue = sv;
                        SizeSuffixUnit = su;
                    }
                    break;
                case OptionControlType.ComboBox:
                    SelectedEnumValue = string.IsNullOrEmpty(currentValue) ? null : currentValue;
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

    protected virtual void SyncToString(string newValue)
    {
        _syncing = true;
        try
        {
            Value = newValue;
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

        OnPropertyChanged(nameof(HasNonDefaultValue));
        OnValueChangedExtra(value);

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

    protected virtual void OnValueChangedExtra(string value) { }

    [RelayCommand]
    protected virtual void ResetToDefault()
    {
        Value = string.Empty;
        OnPropertyChanged(nameof(HasNonDefaultValue));
    }
}
