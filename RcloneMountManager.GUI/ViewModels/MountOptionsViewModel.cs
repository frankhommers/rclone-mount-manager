using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;

namespace RcloneMountManager.GUI.ViewModels;

public partial class MountOptionsViewModel : ObservableObject
{
  private readonly RcloneOptionsService _optionsService = new();
  private IReadOnlyList<RcloneOptionGroup>? _allGroups;

  [ObservableProperty] private bool _showAdvancedOptions = true;

  [ObservableProperty] private bool _isLoading;

  public ObservableCollection<MountOptionGroupViewModel> Groups { get; } = new();

  public async Task LoadOptionsAsync(
    string rcloneBinaryPath,
    Dictionary<string, string> currentValues,
    CancellationToken cancellationToken,
    HashSet<string>? pinnedNames = null)
  {
    IsLoading = true;
    try
    {
      _allGroups = await _optionsService.GetMountOptionsAsync(rcloneBinaryPath, cancellationToken);
      RebuildGroups(currentValues, pinnedNames);
    }
    finally
    {
      IsLoading = false;
    }
  }

  public void UpdateFromProfile(Dictionary<string, string> currentValues, HashSet<string>? pinnedNames = null)
  {
    if (_allGroups is null)
    {
      return;
    }

    RebuildGroups(currentValues, pinnedNames);
  }

  public Dictionary<string, string> GetNonDefaultValues()
  {
    Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
    foreach (MountOptionGroupViewModel group in Groups)
    {
      foreach (GUI.ViewModels.MountOptionInputViewModel option in group.AllOptions)
      {
        if (option.ShouldInclude)
        {
          result[option.Name] = option.Value;
        }
      }
    }

    return result;
  }

  public IReadOnlyList<string> ToCommandLineArguments()
  {
    List<string> args = new();
    foreach (KeyValuePair<string, string> kvp in GetNonDefaultValues())
    {
      string flag = "--" + kvp.Key.Replace('_', '-');
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

  public HashSet<string> GetPinnedOptionNames()
  {
    HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
    foreach (MountOptionGroupViewModel group in Groups)
    {
      foreach (GUI.ViewModels.MountOptionInputViewModel option in group.AllOptions)
      {
        if (option.IsPinned)
        {
          result.Add(option.Name);
        }
      }
    }

    return result;
  }

  partial void OnShowAdvancedOptionsChanged(bool value)
  {
    foreach (MountOptionGroupViewModel group in Groups)
    {
      group.ShowAdvanced = value;
    }
  }

  private void RebuildGroups(Dictionary<string, string> currentValues, HashSet<string>? pinnedNames = null)
  {
    Groups.Clear();
    if (_allGroups is null)
    {
      return;
    }

    foreach (RcloneOptionGroup group in _allGroups)
    {
      List<GUI.ViewModels.MountOptionInputViewModel> optionVms = group.Options
        .Select(o =>
        {
          GUI.ViewModels.MountOptionInputViewModel vm = new(o, currentValues.GetValueOrDefault(o.Name));
          if (pinnedNames is not null && pinnedNames.Contains(o.Name))
          {
            vm.IsPinned = true;
            if (!string.IsNullOrEmpty(currentValues.GetValueOrDefault(o.Name)))
            {
              vm.IsSet = true;
            }
          }

          return vm;
        })
        .ToList();

      Groups.Add(
        new MountOptionGroupViewModel
        {
          Name = group.Name,
          DisplayName = group.DisplayName,
          AllOptions = optionVms,
          ShowAdvanced = ShowAdvancedOptions,
          InfoText = group.Name == "rc"
            ? "Enable Remote Control to allow mount status monitoring and management."
            : null,
        });
    }
  }
}

public partial class MountOptionGroupViewModel : ObservableObject
{
  public string Name { get; init; } = string.Empty;
  public string DisplayName { get; init; } = string.Empty;
  public List<GUI.ViewModels.MountOptionInputViewModel> AllOptions { get; init; } = [];
  public string? InfoText { get; init; }
  public bool HasInfoText => !string.IsNullOrEmpty(InfoText);

  [ObservableProperty] private bool _showAdvanced;

  [ObservableProperty] private bool _isExpanded;

  public IEnumerable<GUI.ViewModels.MountOptionInputViewModel> VisibleOptions =>
    ShowAdvanced ? AllOptions : AllOptions.Where(o => !o.IsAdvanced);

  public bool HasVisibleOptions => VisibleOptions.Any();
  public int ModifiedCount => VisibleOptions.Count(o => o.ShouldInclude);
  public bool HasModifiedOptions => ModifiedCount > 0;

  public string Header => $"{DisplayName} ({VisibleOptions.Count(o => o.ShouldInclude)}/{VisibleOptions.Count()})";

  partial void OnShowAdvancedChanged(bool value)
  {
    OnPropertyChanged(nameof(VisibleOptions));
    OnPropertyChanged(nameof(HasVisibleOptions));
    OnPropertyChanged(nameof(ModifiedCount));
    OnPropertyChanged(nameof(HasModifiedOptions));
    OnPropertyChanged(nameof(Header));
  }
}