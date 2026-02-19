# Rclone Mount Manager v2 - Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Transform the single-project prototype into a multi-project solution with typed parameter controls for all rclone mount options, macOS distribution, tests, and open-source governance.

**Architecture:** Multi-project .NET 10 solution (Core/GUI/Tests) using Avalonia UI with CommunityToolkit.Mvvm. Mount parameters discovered via `rclone rc --loopback options/info` JSON API, rendered with type-aware UI controls. Distribution via macOS .app bundle and DMG.

**Tech Stack:** C# / .NET 10, Avalonia UI 11.3, CommunityToolkit.Mvvm 8.4, CliWrap 3.10, Serilog, xunit

---

### Task 1: Git Init and .gitignore

**Files:**
- Create: `.gitignore`

**Step 1: Initialize git repository**

Run: `git init`
Expected: "Initialized empty Git repository"

**Step 2: Create .gitignore**

Use the same comprehensive .gitignore from git-auto-sync (covers .NET, Visual Studio, JetBrains, macOS, build artifacts). Key entries:

```gitignore
[Dd]ebug/
[Rr]elease/
[Bb]in/
[Oo]bj/
[Ll]og/
[Ll]ogs/
.vs/
*.user
*.suo
.DS_Store
dist/
.artifacts/
nuget.config
```

Copy the full .gitignore from `/Users/frankhommers/Repos/git-auto-sync/.gitignore`.

**Step 3: Commit**

```bash
git add .gitignore
git commit -m "init: add .gitignore"
```

---

### Task 2: Solution File and Directory.Build.props

**Files:**
- Create: `RcloneMountManager.slnx`
- Create: `Directory.Build.props`

**Step 1: Create Directory.Build.props**

```xml
<Project>
  <PropertyGroup>
    <VersionPrefix>0.1.0</VersionPrefix>
    <Version Condition="'$(Version)' == ''">$(VersionPrefix)</Version>
    <AssemblyVersion Condition="'$(AssemblyVersion)' == ''">$(Version).0</AssemblyVersion>
    <FileVersion Condition="'$(FileVersion)' == ''">$(Version).0</FileVersion>
  </PropertyGroup>
</Project>
```

**Step 2: Create solution file**

```xml
<Solution>
  <Project Path="RcloneMountManager.Core/RcloneMountManager.Core.csproj"/>
  <Project Path="RcloneMountManager.GUI/RcloneMountManager.GUI.csproj"/>
  <Project Path="RcloneMountManager.Tests/RcloneMountManager.Tests.csproj"/>
</Solution>
```

**Step 3: Commit**

```bash
git add Directory.Build.props RcloneMountManager.slnx
git commit -m "init: add solution file and centralized versioning"
```

---

### Task 3: Create Core Project and Move Models + Services

**Files:**
- Create: `RcloneMountManager.Core/RcloneMountManager.Core.csproj`
- Move: `RcloneMountManager/Models/` -> `RcloneMountManager.Core/Models/`
- Move: `RcloneMountManager/Services/` -> `RcloneMountManager.Core/Services/`

**Step 1: Create Core project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CliWrap" Version="3.10.0" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="Serilog" Version="4.2.0" />
  </ItemGroup>
</Project>
```

**Step 2: Move Models and Services**

Move these files from `RcloneMountManager/` to `RcloneMountManager.Core/`:
- `Models/MountProfile.cs`
- `Models/MountType.cs`
- `Models/QuickConnectMode.cs`
- `Models/RcloneBackendInfo.cs`
- `Models/RcloneBackendOption.cs`
- `Models/RcloneBackendOptionInput.cs`
- `Services/MountManagerService.cs`
- `Services/RcloneBackendService.cs`
- `Services/LaunchAgentService.cs`

**Step 3: Update namespaces**

Change all namespaces from `RcloneMountManager.Models` to `RcloneMountManager.Core.Models` and `RcloneMountManager.Services` to `RcloneMountManager.Core.Services`.

**Step 4: Verify build**

Run: `dotnet build RcloneMountManager.Core/RcloneMountManager.Core.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add RcloneMountManager.Core/
git commit -m "refactor: create Core project with models and services"
```

---

### Task 4: Restructure GUI Project

**Files:**
- Modify: `RcloneMountManager/RcloneMountManager.csproj` (rename to RcloneMountManager.GUI)
- Modify: All files in `RcloneMountManager/` that reference moved types

**Step 1: Rename project directory**

Rename `RcloneMountManager/` to `RcloneMountManager.GUI/`.

**Step 2: Rename csproj**

Rename `RcloneMountManager.csproj` to `RcloneMountManager.GUI.csproj`.

**Step 3: Update csproj to reference Core**

Add to the csproj:

```xml
<ItemGroup>
  <ProjectReference Include="..\RcloneMountManager.Core\RcloneMountManager.Core.csproj" />
</ItemGroup>
```

Remove from csproj the package references that moved to Core (CliWrap, Serilog - keep only what GUI uses directly that isn't in Core already). Actually, CliWrap and CommunityToolkit.Mvvm are in Core, but Serilog.Sinks.Console and Serilog.Sinks.File are GUI-only. Keep:

```xml
<ItemGroup>
  <PackageReference Include="Avalonia" Version="11.3.12" />
  <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
  <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
  <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.12" />
  <PackageReference Include="Avalonia.Diagnostics" Version="11.3.12">
    <IncludeAssets Condition="'$(Configuration)' != 'Debug'">None</IncludeAssets>
    <PrivateAssets Condition="'$(Configuration)' != 'Debug'">All</PrivateAssets>
  </PackageReference>
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
  <PackageReference Include="Serilog" Version="4.2.0" />
  <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
  <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
</ItemGroup>
```

**Step 4: Remove old Models/ and Services/ directories from GUI**

They now live in Core. Remove the `Models/` and `Services/` folders from `RcloneMountManager.GUI/`.

**Step 5: Update using statements in GUI files**

In `MainWindowViewModel.cs`, `Program.cs`, `App.axaml.cs`, `ViewLocator.cs`:
- Add `using RcloneMountManager.Core.Models;`
- Add `using RcloneMountManager.Core.Services;`
- Remove old `using RcloneMountManager.Models;` and `using RcloneMountManager.Services;`

Update the namespace for files that stay in GUI:
- `RcloneMountManager.ViewModels` stays as is (or becomes `RcloneMountManager.GUI.ViewModels`)
- `RcloneMountManager.Views` stays as is (or becomes `RcloneMountManager.GUI.Views`)

Keep the root namespace as `RcloneMountManager` for GUI to minimize AXAML changes. Just update the using statements.

Actually, since AXAML uses `xmlns:vm="using:RcloneMountManager.ViewModels"`, we should keep that namespace. The simplest approach: keep `RcloneMountManager` as the root namespace for the GUI project. Only Core gets `RcloneMountManager.Core.*` namespaces.

**Step 6: Verify build**

Run: `dotnet build RcloneMountManager.slnx`
Expected: Build succeeded with 0 errors.

**Step 7: Commit**

```bash
git add -A
git commit -m "refactor: restructure into Core/GUI multi-project solution"
```

---

### Task 5: Add RcloneOption and RcloneOptionGroup Models

**Files:**
- Create: `RcloneMountManager.Core/Models/RcloneOption.cs`
- Create: `RcloneMountManager.Core/Models/RcloneOptionGroup.cs`

**Step 1: Create RcloneOption model**

```csharp
namespace RcloneMountManager.Core.Models;

public sealed class RcloneOption
{
    public string Name { get; set; } = string.Empty;
    public string Help { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public object? Default { get; set; }
    public string DefaultStr { get; set; } = string.Empty;
    public bool Advanced { get; set; }
    public bool Required { get; set; }
    public bool IsPassword { get; set; }
    public string? Groups { get; set; }

    /// <summary>
    /// For enum-like types (e.g. "memory|disk|symlink"), returns the choices.
    /// For known enum types (CacheMode, LogLevel), returns their values.
    /// Returns null for non-enum types.
    /// </summary>
    public IReadOnlyList<string>? GetEnumValues()
    {
        // Known named enums
        return Type switch
        {
            "CacheMode" => ["off", "minimal", "writes", "full"],
            "LogLevel" => ["DEBUG", "INFO", "NOTICE", "ERROR"],
            "Tristate" => ["unset", "true", "false"],
            _ when Type.Contains('|') => Type.Split('|'),
            _ => null,
        };
    }

    /// <summary>
    /// Returns the UI control type appropriate for this option.
    /// </summary>
    public OptionControlType GetControlType()
    {
        if (GetEnumValues() is not null) return OptionControlType.ComboBox;

        return Type switch
        {
            "bool" => OptionControlType.Toggle,
            "int" or "int64" or "uint32" or "float64" => OptionControlType.Numeric,
            "Duration" => OptionControlType.Duration,
            "SizeSuffix" => OptionControlType.SizeSuffix,
            "FileMode" => OptionControlType.Text,
            "string" => OptionControlType.Text,
            "stringArray" or "SpaceSepList" => OptionControlType.Text,
            "BwTimetable" or "DumpFlags" or "Bits" or "Time" => OptionControlType.Text,
            _ => OptionControlType.Text,
        };
    }
}

public enum OptionControlType
{
    Text,
    Toggle,
    Numeric,
    Duration,
    SizeSuffix,
    ComboBox,
}
```

**Step 2: Create RcloneOptionGroup model**

```csharp
namespace RcloneMountManager.Core.Models;

public sealed class RcloneOptionGroup
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public IReadOnlyList<RcloneOption> Options { get; set; } = [];
}
```

**Step 3: Verify build**

Run: `dotnet build RcloneMountManager.Core/RcloneMountManager.Core.csproj`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add RcloneMountManager.Core/Models/RcloneOption.cs RcloneMountManager.Core/Models/RcloneOptionGroup.cs
git commit -m "feat: add RcloneOption and RcloneOptionGroup models for typed parameters"
```

---

### Task 6: Create RcloneOptionsService

**Files:**
- Create: `RcloneMountManager.Core/Services/RcloneOptionsService.cs`

**Step 1: Implement the service**

```csharp
using CliWrap;
using CliWrap.Buffered;
using RcloneMountManager.Core.Models;
using System.Text.Json;

namespace RcloneMountManager.Core.Services;

public sealed class RcloneOptionsService
{
    /// <summary>
    /// Groups relevant for mount operations, in display order.
    /// </summary>
    private static readonly (string Key, string Display)[] MountRelevantGroups =
    [
        ("mount", "Mount"),
        ("vfs", "VFS Cache"),
        ("nfs", "NFS"),
        ("filter", "Filters"),
        ("main", "General"),
    ];

    public async Task<IReadOnlyList<RcloneOptionGroup>> GetMountOptionsAsync(
        string rcloneBinaryPath,
        CancellationToken cancellationToken)
    {
        var binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;

        var result = await Cli.Wrap(binary)
            .WithArguments(["rc", "--loopback", "options/info"])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? "Could not retrieve rclone options."
                    : result.StandardError.Trim());
        }

        return ParseOptionsJson(result.StandardOutput);
    }

    public static IReadOnlyList<RcloneOptionGroup> ParseOptionsJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var groups = new List<RcloneOptionGroup>();

        foreach (var (key, display) in MountRelevantGroups)
        {
            if (!root.TryGetProperty(key, out var groupElement))
                continue;

            var options = new List<RcloneOption>();
            foreach (var optElement in groupElement.EnumerateArray())
            {
                var option = new RcloneOption
                {
                    Name = optElement.GetProperty("Name").GetString() ?? string.Empty,
                    Help = optElement.GetProperty("Help").GetString() ?? string.Empty,
                    Type = optElement.GetProperty("Type").GetString() ?? "string",
                    DefaultStr = optElement.GetProperty("DefaultStr").GetString() ?? string.Empty,
                    Advanced = optElement.TryGetProperty("Advanced", out var adv) && adv.GetBoolean(),
                    Required = optElement.TryGetProperty("Required", out var req) && req.GetBoolean(),
                    IsPassword = optElement.TryGetProperty("IsPassword", out var pwd) && pwd.GetBoolean(),
                    Groups = optElement.TryGetProperty("Groups", out var grp) ? grp.GetString() : null,
                };

                if (!string.IsNullOrWhiteSpace(option.Name))
                    options.Add(option);
            }

            groups.Add(new RcloneOptionGroup
            {
                Name = key,
                DisplayName = display,
                Options = options,
            });
        }

        return groups;
    }
}
```

**Step 2: Verify build**

Run: `dotnet build RcloneMountManager.Core/RcloneMountManager.Core.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add RcloneMountManager.Core/Services/RcloneOptionsService.cs
git commit -m "feat: add RcloneOptionsService for discovering mount options via rclone rc"
```

---

### Task 7: Update MountProfile to Store Typed Options

**Files:**
- Modify: `RcloneMountManager.Core/Models/MountProfile.cs`

**Step 1: Add MountOptions dictionary**

Add to `MountProfile.cs` after the existing `_extraOptions` field:

```csharp
[ObservableProperty]
private Dictionary<string, string> _mountOptions = new();
```

Note: `Dictionary` doesn't raise per-key change notifications, but that's fine - the ViewModel will handle it.

**Step 2: Update PersistedProfile in MainWindowViewModel**

The `PersistedProfile` class in `MainWindowViewModel.cs` also needs a `MountOptions` property:

```csharp
public Dictionary<string, string> MountOptions { get; set; } = new();
```

And the `LoadProfiles` and `SaveProfiles` methods need to map it.

**Step 3: Verify build**

Run: `dotnet build RcloneMountManager.slnx`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add MountOptions dictionary to MountProfile for typed parameter storage"
```

---

### Task 8: Create MountOptionInputViewModel

**Files:**
- Create: `RcloneMountManager.GUI/ViewModels/MountOptionInputViewModel.cs`

This is the ViewModel for a single option row in the parameter editor.

**Step 1: Implement the ViewModel**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using RcloneMountManager.Core.Models;
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

    /// <summary>
    /// Returns true if the current value differs from the default.
    /// </summary>
    public bool HasNonDefaultValue =>
        IsSet && !string.IsNullOrEmpty(Value) &&
        !string.Equals(Value, DefaultStr, System.StringComparison.OrdinalIgnoreCase);

    partial void OnValueChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !string.Equals(value, DefaultStr, System.StringComparison.OrdinalIgnoreCase))
        {
            IsSet = true;
        }

        OnPropertyChanged(nameof(HasNonDefaultValue));
    }

    public void ResetToDefault()
    {
        Value = string.Empty;
        IsSet = false;
    }
}
```

**Step 2: Verify build**

Run: `dotnet build RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add RcloneMountManager.GUI/ViewModels/MountOptionInputViewModel.cs
git commit -m "feat: add MountOptionInputViewModel for typed parameter editing"
```

---

### Task 9: Create MountOptionsViewModel

**Files:**
- Create: `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs`

**Step 1: Implement the ViewModel**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>
    /// Collects all non-default option values into a dictionary.
    /// </summary>
    public Dictionary<string, string> GetNonDefaultValues()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                if (option.HasNonDefaultValue)
                {
                    result[option.Name] = option.Value;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Converts non-default values to command-line arguments.
    /// </summary>
    public IReadOnlyList<string> ToCommandLineArguments()
    {
        var args = new List<string>();
        foreach (var (name, value) in GetNonDefaultValues())
        {
            var flag = "--" + name.Replace('_', '-');
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                args.Add(flag);
            }
            else if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                args.Add(flag + "=false");
            }
            else
            {
                args.Add(flag);
                args.Add(value);
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

    public string Header => $"{DisplayName} ({VisibleOptions.Count(o => o.HasNonDefaultValue)}/{VisibleOptions.Count()})";

    partial void OnShowAdvancedChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleOptions));
        OnPropertyChanged(nameof(Header));
    }
}
```

**Step 2: Verify build**

Run: `dotnet build RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs
git commit -m "feat: add MountOptionsViewModel for grouped typed parameter editing"
```

---

### Task 10: Create MountOptionsView (AXAML)

**Files:**
- Create: `RcloneMountManager.GUI/Views/MountOptionsView.axaml`
- Create: `RcloneMountManager.GUI/Views/MountOptionsView.axaml.cs`

**Step 1: Create the UserControl AXAML**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:RcloneMountManager.ViewModels"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             x:Class="RcloneMountManager.Views.MountOptionsView"
             x:DataType="vm:MountOptionsViewModel">

    <StackPanel Spacing="8">
        <Grid ColumnDefinitions="*,Auto" Margin="0,0,0,8">
            <TextBlock Text="Mount Parameters" FontWeight="SemiBold" FontSize="14" VerticalAlignment="Center"/>
            <CheckBox Grid.Column="1" Content="Show advanced options" IsChecked="{Binding ShowAdvancedOptions}"/>
        </Grid>

        <TextBlock Text="Loading rclone options..."
                   IsVisible="{Binding IsLoading}"
                   Opacity="0.7"
                   FontStyle="Italic"/>

        <ItemsControl ItemsSource="{Binding Groups}" IsVisible="{Binding !IsLoading}">
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:MountOptionGroupViewModel">
                    <Expander IsExpanded="{Binding IsExpanded}"
                              Header="{Binding Header}"
                              Margin="0,0,0,4">
                        <ItemsControl ItemsSource="{Binding VisibleOptions}" Margin="0,4,0,0">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="vm:MountOptionInputViewModel">
                                    <Border Padding="0,4" Opacity="0.95">
                                        <Grid ColumnDefinitions="240,*,Auto" RowDefinitions="Auto,Auto">
                                            <!-- Label -->
                                            <TextBlock Grid.Row="0" Grid.Column="0"
                                                       Text="{Binding Label}"
                                                       VerticalAlignment="Center"
                                                       FontSize="12"/>

                                            <!-- Control: TextBox (default for string, Duration, SizeSuffix, etc.) -->
                                            <TextBox Grid.Row="0" Grid.Column="1"
                                                     Text="{Binding Value}"
                                                     Watermark="{Binding DefaultStr}"
                                                     Margin="8,0,0,0"
                                                     IsVisible="{Binding ControlType, Converter={x:Static ObjectConverters.IsNotNull}}"
                                                     />
                                            <!-- Note: In a future iteration, we can use a DataTemplateSelector
                                                 to render different controls per ControlType. For now, TextBox
                                                 with watermark showing default covers all types functionally. -->

                                            <!-- Reset button -->
                                            <Button Grid.Row="0" Grid.Column="2"
                                                    Content="x"
                                                    Command="{Binding ResetToDefaultCommand}"
                                                    Padding="4,2"
                                                    Margin="4,0,0,0"
                                                    FontSize="10"
                                                    IsVisible="{Binding HasNonDefaultValue}"/>

                                            <!-- Help text -->
                                            <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                                                       Text="{Binding Help}"
                                                       TextWrapping="Wrap"
                                                       FontSize="11"
                                                       Opacity="0.6"
                                                       Margin="0,2,0,4"/>
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Expander>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</UserControl>
```

**Step 2: Create code-behind**

```csharp
using Avalonia.Controls;

namespace RcloneMountManager.Views;

public partial class MountOptionsView : UserControl
{
    public MountOptionsView()
    {
        InitializeComponent();
    }
}
```

**Step 3: Add ResetToDefaultCommand to MountOptionInputViewModel**

Add to `MountOptionInputViewModel.cs`:

```csharp
[RelayCommand]
private void ResetToDefault()
{
    Value = string.Empty;
    IsSet = false;
}
```

(Remove the existing non-command `ResetToDefault()` method and add `[RelayCommand]` attribute, plus `using CommunityToolkit.Mvvm.Input;`)

**Step 4: Verify build**

Run: `dotnet build RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add RcloneMountManager.GUI/Views/MountOptionsView.axaml RcloneMountManager.GUI/Views/MountOptionsView.axaml.cs
git commit -m "feat: add MountOptionsView with grouped expanders for typed parameters"
```

---

### Task 11: Integrate MountOptionsView into MainWindow

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
- Modify: `RcloneMountManager.GUI/Views/MainWindow.axaml`

**Step 1: Add MountOptionsViewModel to MainWindowViewModel**

Add field and property:

```csharp
public MountOptionsViewModel MountOptionsVm { get; } = new();
```

In the constructor, after loading backends, also load mount options:

```csharp
_ = Task.Run(async () =>
{
    try
    {
        var binary = SelectedProfile?.RcloneBinaryPath ?? "rclone";
        await MountOptionsVm.LoadOptionsAsync(binary, SelectedProfile?.MountOptions ?? new(), CancellationToken.None);
    }
    catch (Exception ex)
    {
        AppendLog($"ERR: Could not load mount options: {ex.Message}");
    }
});
```

In `OnSelectedProfileChanged`, update mount options:

```csharp
MountOptionsVm.UpdateFromProfile(value.MountOptions);
```

In `SaveProfiles`, collect options from ViewModel:

```csharp
// Before saving, sync typed options back to profile
if (SelectedProfile is not null)
{
    SelectedProfile.MountOptions = MountOptionsVm.GetNonDefaultValues();
}
```

**Step 2: Add MountOptionsView to MainWindow.axaml**

Insert after the source/mount path border and before the action buttons:

```xml
<!-- Mount Parameters (typed options) -->
<Border BorderThickness="1" CornerRadius="8" Padding="10" Margin="0,0,0,10">
    <views:MountOptionsView DataContext="{Binding MountOptionsVm}"/>
</Border>
```

Add the views namespace to the Window tag:

```xml
xmlns:views="using:RcloneMountManager.Views"
```

**Step 3: Update command generation to use typed options**

In `MountManagerService.StartRcloneAsync`, after `arguments.AddRange(ParseArguments(profile.ExtraOptions))`, also add typed options. The cleanest way is to have the ViewModel pass them to the service.

Alternatively, modify `MountManagerService.StartRcloneAsync` to also accept typed options. Better approach: have `MountManagerService` accept a `Dictionary<string, string>` for typed options:

Add to `StartRcloneAsync` signature (or make the existing method check `profile.MountOptions`):

```csharp
// In StartRcloneAsync, after adding extra options:
foreach (var (name, value) in profile.MountOptions)
{
    var flag = "--" + name.Replace('_', '-');
    if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
    {
        arguments.Add(flag);
    }
    else if (!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(value))
    {
        arguments.Add(flag);
        arguments.Add(value);
    }
}
```

Do the same in `GenerateScript`.

**Step 4: Verify build and manual test**

Run: `dotnet build RcloneMountManager.slnx`
Run: `dotnet run --project RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`
Expected: App starts, mount parameter sections appear with expanders for Mount, VFS, NFS, Filter, General groups.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: integrate typed mount parameters into main window"
```

---

### Task 12: Create Tests Project

**Files:**
- Create: `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj`
- Create: `RcloneMountManager.Tests/Services/RcloneOptionsServiceTests.cs`
- Create: `RcloneMountManager.Tests/Models/RcloneOptionTests.cs`

**Step 1: Create test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RcloneMountManager.Core\RcloneMountManager.Core.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Write RcloneOptionsServiceTests**

```csharp
using RcloneMountManager.Core.Services;

namespace RcloneMountManager.Tests.Services;

public class RcloneOptionsServiceTests
{
    private const string SampleJson = """
    {
      "mount": [
        {
          "Name": "debug_fuse",
          "Help": "Debug the FUSE internals",
          "Type": "bool",
          "Default": false,
          "DefaultStr": "false",
          "Advanced": false,
          "Required": false,
          "IsPassword": false
        },
        {
          "Name": "attr_timeout",
          "Help": "Time for which attributes are cached",
          "Type": "Duration",
          "Default": 1000000000,
          "DefaultStr": "1s",
          "Advanced": false,
          "Required": false,
          "IsPassword": false
        }
      ],
      "vfs": [
        {
          "Name": "vfs_cache_mode",
          "Help": "Cache mode off|minimal|writes|full",
          "Type": "CacheMode",
          "Default": 0,
          "DefaultStr": "off",
          "Advanced": false,
          "Required": false,
          "IsPassword": false
        }
      ],
      "nfs": [
        {
          "Name": "nfs_cache_type",
          "Help": "NFS cache type",
          "Type": "memory|disk|symlink",
          "Default": "memory",
          "DefaultStr": "memory",
          "Advanced": false,
          "Required": false,
          "IsPassword": false
        }
      ]
    }
    """;

    [Fact]
    public void ParseOptionsJson_ReturnsCorrectGroups()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJson);

        Assert.Equal(3, groups.Count);
        Assert.Equal("mount", groups[0].Name);
        Assert.Equal("Mount", groups[0].DisplayName);
        Assert.Equal("vfs", groups[1].Name);
        Assert.Equal("nfs", groups[2].Name);
    }

    [Fact]
    public void ParseOptionsJson_MountGroup_HasCorrectOptions()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJson);
        var mount = groups[0];

        Assert.Equal(2, mount.Options.Count);
        Assert.Equal("debug_fuse", mount.Options[0].Name);
        Assert.Equal("bool", mount.Options[0].Type);
        Assert.Equal("false", mount.Options[0].DefaultStr);
        Assert.Equal("attr_timeout", mount.Options[1].Name);
        Assert.Equal("Duration", mount.Options[1].Type);
    }

    [Fact]
    public void ParseOptionsJson_CacheMode_IsEnum()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJson);
        var vfs = groups[1];
        var cacheMode = vfs.Options[0];

        var enumValues = cacheMode.GetEnumValues();
        Assert.NotNull(enumValues);
        Assert.Equal(["off", "minimal", "writes", "full"], enumValues);
    }

    [Fact]
    public void ParseOptionsJson_PipeSeparatedEnum_IsParsed()
    {
        var groups = RcloneOptionsService.ParseOptionsJson(SampleJson);
        var nfs = groups[2];
        var cacheType = nfs.Options[0];

        var enumValues = cacheType.GetEnumValues();
        Assert.NotNull(enumValues);
        Assert.Equal(["memory", "disk", "symlink"], enumValues);
    }
}
```

**Step 3: Write RcloneOptionTests**

```csharp
using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Tests.Models;

public class RcloneOptionTests
{
    [Theory]
    [InlineData("bool", OptionControlType.Toggle)]
    [InlineData("int", OptionControlType.Numeric)]
    [InlineData("int64", OptionControlType.Numeric)]
    [InlineData("uint32", OptionControlType.Numeric)]
    [InlineData("float64", OptionControlType.Numeric)]
    [InlineData("string", OptionControlType.Text)]
    [InlineData("Duration", OptionControlType.Duration)]
    [InlineData("SizeSuffix", OptionControlType.SizeSuffix)]
    [InlineData("CacheMode", OptionControlType.ComboBox)]
    [InlineData("memory|disk|symlink", OptionControlType.ComboBox)]
    [InlineData("stringArray", OptionControlType.Text)]
    public void GetControlType_ReturnsCorrectType(string rcloneType, OptionControlType expected)
    {
        var option = new RcloneOption { Type = rcloneType };
        Assert.Equal(expected, option.GetControlType());
    }

    [Fact]
    public void GetEnumValues_ForBool_ReturnsNull()
    {
        var option = new RcloneOption { Type = "bool" };
        Assert.Null(option.GetEnumValues());
    }

    [Fact]
    public void GetEnumValues_ForCacheMode_ReturnsFourValues()
    {
        var option = new RcloneOption { Type = "CacheMode" };
        var values = option.GetEnumValues();
        Assert.NotNull(values);
        Assert.Equal(4, values.Count);
        Assert.Contains("full", values);
    }

    [Fact]
    public void GetEnumValues_ForTristate_ReturnsThreeValues()
    {
        var option = new RcloneOption { Type = "Tristate" };
        var values = option.GetEnumValues();
        Assert.NotNull(values);
        Assert.Equal(3, values.Count);
    }
}
```

**Step 4: Run tests**

Run: `dotnet test RcloneMountManager.Tests/RcloneMountManager.Tests.csproj -v normal`
Expected: All tests pass.

**Step 5: Commit**

```bash
git add RcloneMountManager.Tests/
git commit -m "test: add unit tests for RcloneOptionsService and RcloneOption"
```

---

### Task 13: Open-Source Governance Files

**Files:**
- Create: `README.md`
- Create: `LICENSE`
- Create: `CONTRIBUTING.md`
- Create: `CODE_OF_CONDUCT.md`

**Step 1: Create LICENSE (MIT)**

```
MIT License

Copyright (c) 2026 Frank Hommers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

**Step 2: Create README.md**

Cover: what it is, features, screenshot placeholder, installation, building from source, usage, contributing.

**Step 3: Create CONTRIBUTING.md**

Adapt from git-auto-sync: development setup (.NET 10, clone, build, test), branches, coding guidelines, commit messages.

**Step 4: Create CODE_OF_CONDUCT.md**

Standard Contributor Covenant v2.1.

**Step 5: Commit**

```bash
git add README.md LICENSE CONTRIBUTING.md CODE_OF_CONDUCT.md
git commit -m "docs: add README, LICENSE, CONTRIBUTING, and CODE_OF_CONDUCT"
```

---

### Task 14: macOS Build Scripts

**Files:**
- Create: `scripts/build-macos-app.sh`
- Create: `scripts/create-dmg.sh`

**Step 1: Create build-macos-app.sh**

Adapt from git-auto-sync's script. Key differences:
- Single project (GUI only, no daemon)
- Different app name: "Rclone Mount Manager"
- Different bundle ID: "tools.franks.rclone-mount-manager"
- Executable: `RcloneMountManager.GUI`
- Project path: `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`

**Step 2: Create create-dmg.sh**

Copy from git-auto-sync, update volume name to "Rclone Mount Manager".

**Step 3: Make executable**

Run: `chmod +x scripts/build-macos-app.sh scripts/create-dmg.sh`

**Step 4: Test build script**

Run: `bash scripts/build-macos-app.sh --rid osx-arm64 --output dist`
Expected: Creates `dist/Rclone Mount Manager.app`

**Step 5: Commit**

```bash
git add scripts/
git commit -m "feat: add macOS app bundle and DMG build scripts"
```

---

### Task 15: GitHub Actions CI/CD

**Files:**
- Create: `.github/workflows/release-dmg.yml`
- Create: `.github/ISSUE_TEMPLATE/bug_report.md`
- Create: `.github/ISSUE_TEMPLATE/feature_request.md`
- Create: `.github/pull_request_template.md`

**Step 1: Create release-dmg.yml**

Adapt from git-auto-sync: update project paths, app name, script paths.

**Step 2: Create issue templates and PR template**

Standard GitHub templates for bug reports, feature requests, and PR checklist.

**Step 3: Commit**

```bash
git add .github/
git commit -m "ci: add GitHub Actions release workflow and issue/PR templates"
```

---

### Task 16: Add Existing Source Files to Git

**Step 1: Add all remaining files**

At this point, add all existing project files that haven't been committed yet.

```bash
git add -A
git commit -m "feat: add all project source files"
```

---

### Task 17: Verify Everything Works End-to-End

**Step 1: Build solution**

Run: `dotnet build RcloneMountManager.slnx`
Expected: Build succeeded, 0 errors.

**Step 2: Run tests**

Run: `dotnet test RcloneMountManager.slnx -v normal`
Expected: All tests pass.

**Step 3: Run the app**

Run: `dotnet run --project RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`
Expected: App starts, shows mount parameter sections with typed controls.

**Step 4: Fix any remaining issues and commit**

```bash
git add -A
git commit -m "fix: resolve any remaining build or runtime issues"
```
