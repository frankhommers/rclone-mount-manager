using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneMountManager.Core.Helpers;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

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
    public ObservableCollection<StringListItemViewModel> StringListItems { get; } = [];
    public bool IsKeyValue => Option.IsKeyValue;
    public string ListSeparator => Option.ListSeparator;

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
        !string.Equals(Value, NormalizedDefaultStr, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// DefaultStr normalized through the same parse→format roundtrip as Value,
    /// so "5m0s" matches "5m" and "128Mi" matches "128Mi".
    /// </summary>
    protected string NormalizedDefaultStr
    {
        get
        {
            var raw = DefaultStr;
            if (string.IsNullOrEmpty(raw)) return raw;
            return ControlType switch
            {
                OptionControlType.Duration => DurationHelper.Format(DurationHelper.Parse(raw)),
                OptionControlType.SizeSuffix => SizeSuffixHelper.Format(SizeSuffixHelper.Parse(raw).Value, SizeSuffixHelper.Parse(raw).Unit),
                _ => raw,
            };
        }
    }

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
                    var numStr = !string.IsNullOrEmpty(currentValue) ? currentValue : DefaultStr;
                    NumericValue = decimal.TryParse(numStr, out var num) ? num : null;
                    break;
                case OptionControlType.Duration:
                    var durStr = !string.IsNullOrEmpty(currentValue) ? currentValue : DefaultStr;
                    DurationValue = string.IsNullOrEmpty(durStr) ? null : DurationHelper.Parse(durStr);
                    break;
                case OptionControlType.SizeSuffix:
                    var sizeStr = !string.IsNullOrEmpty(currentValue) ? currentValue : DefaultStr;
                    if (!string.IsNullOrEmpty(sizeStr))
                    {
                        var (sv, su) = SizeSuffixHelper.Parse(sizeStr);
                        SizeSuffixNumericValue = sv;
                        SizeSuffixUnit = su;
                    }
                    break;
                case OptionControlType.ComboBox:
                    SelectedEnumValue = !string.IsNullOrEmpty(currentValue)
                        ? currentValue
                        : !string.IsNullOrEmpty(DefaultStr) ? DefaultStr : null;
                    break;
                case OptionControlType.StringList:
                    InitializeStringListFromValue(currentValue);
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
        if (string.IsNullOrEmpty(value))
        {
            SizeSuffixUnit = "B";
            return;
        }
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
                case OptionControlType.StringList:
                    InitializeStringListFromValue(value);
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
    public void AddStringListItem()
    {
        StringListItems.Add(CreateStringListItem());
    }

    private StringListItemViewModel CreateStringListItem()
    {
        return new StringListItemViewModel(
            IsKeyValue,
            item =>
            {
                StringListItems.Remove(item);
                SyncStringListToValue();
            },
            SyncStringListToValue);
    }

    private void SyncStringListToValue()
    {
        if (_syncing) return;

        var newValue = string.Join(
            ListSeparator,
            StringListItems
                .Select(item => item.Serialize())
                .Where(serialized => !string.IsNullOrWhiteSpace(serialized)));

        SyncToString(newValue);
    }

    private void InitializeStringListFromValue(string? currentValue)
    {
        StringListItems.Clear();
        if (string.IsNullOrWhiteSpace(currentValue)) return;

        var values = currentValue.Split(
            ListSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var value in values)
        {
            var item = CreateStringListItem();
            item.Deserialize(value);
            StringListItems.Add(item);
        }
    }

    [RelayCommand]
    protected virtual void ResetToDefault()
    {
        Value = string.Empty;
        OnPropertyChanged(nameof(HasNonDefaultValue));
    }
}
