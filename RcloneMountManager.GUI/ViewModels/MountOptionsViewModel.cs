using CommunityToolkit.Mvvm.ComponentModel;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.ViewModels;

public partial class MountOptionsViewModel : ObservableObject
{
    private readonly RcloneOptionsService _optionsService = new();
    private IReadOnlyList<RcloneOptionGroup>? _allGroups;

    [ObservableProperty]
    private bool _showAdvancedOptions;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<MountOptionGroupViewModel> Groups { get; } = new();

    public async Task LoadOptionsAsync(string rcloneBinaryPath, Dictionary<string, string> currentValues, CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            _allGroups = await _optionsService.GetMountOptionsAsync(rcloneBinaryPath, cancellationToken);
            RebuildGroups(currentValues);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void UpdateFromProfile(Dictionary<string, string> currentValues)
    {
        if (_allGroups is null) return;
        RebuildGroups(currentValues);
    }

    public Dictionary<string, string> GetNonDefaultValues()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups)
        {
            foreach (var option in group.AllOptions)
            {
                if (option.HasNonDefaultValue)
                {
                    result[option.Name] = option.Value;
                }
            }
        }
        return result;
    }

    public IReadOnlyList<string> ToCommandLineArguments()
    {
        var args = new List<string>();
        foreach (var kvp in GetNonDefaultValues())
        {
            var flag = "--" + kvp.Key.Replace('_', '-');
            if (string.Equals(kvp.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                args.Add(flag);
            }
            else if (string.Equals(kvp.Value, "false", StringComparison.OrdinalIgnoreCase))
            {
                args.Add(flag + "=false");
            }
            else
            {
                args.Add(flag);
                args.Add(kvp.Value);
            }
        }
        return args;
    }

    partial void OnShowAdvancedOptionsChanged(bool value)
    {
        foreach (var group in Groups)
        {
            group.ShowAdvanced = value;
        }
    }

    private void RebuildGroups(Dictionary<string, string> currentValues)
    {
        Groups.Clear();
        if (_allGroups is null) return;

        foreach (var group in _allGroups)
        {
            var optionVms = group.Options
                .Select(o => new MountOptionInputViewModel(o, currentValues.GetValueOrDefault(o.Name)))
                .ToList();

            Groups.Add(new MountOptionGroupViewModel
            {
                Name = group.Name,
                DisplayName = group.DisplayName,
                AllOptions = optionVms,
                ShowAdvanced = ShowAdvancedOptions,
            });
        }
    }
}

public partial class MountOptionGroupViewModel : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public List<MountOptionInputViewModel> AllOptions { get; init; } = [];

    [ObservableProperty]
    private bool _showAdvanced;

    [ObservableProperty]
    private bool _isExpanded;

    public IEnumerable<MountOptionInputViewModel> VisibleOptions =>
        ShowAdvanced ? AllOptions : AllOptions.Where(o => !o.IsAdvanced);

    public bool HasVisibleOptions => VisibleOptions.Any();
    public int ModifiedCount => VisibleOptions.Count(o => o.HasNonDefaultValue);
    public bool HasModifiedOptions => ModifiedCount > 0;

    public string Header => $"{DisplayName} ({VisibleOptions.Count(o => o.HasNonDefaultValue)}/{VisibleOptions.Count()})";

    partial void OnShowAdvancedChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleOptions));
        OnPropertyChanged(nameof(HasVisibleOptions));
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(HasModifiedOptions));
        OnPropertyChanged(nameof(Header));
    }
}
