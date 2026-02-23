# Test Connection Dialog + Diagnostics Sidebar Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the generic "Operation failed." test feedback with a live modal dialog showing streaming output and success/failure, and move diagnostics from inline to a dedicated sidebar entry.

**Architecture:** Add ViewModel properties for a test-result dialog (visible, title, success state, log lines collection) and a `ShowDiagnosticsView` mode. The dialog opens on test start, streams lines, and shows result. The sidebar gets a DIAGNOSTICS section with a "Logs" entry that toggles the main content to a diagnostics view.

**Tech Stack:** Avalonia XAML, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), xUnit tests.

---

### Task 1: Add Test Dialog ViewModel Properties

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs:61-119` (observable properties section)

**Step 1: Add the new observable properties**

After the existing `_deleteBlockedDialogMessage` field (line 119), add:

```csharp
[ObservableProperty]
private bool _isTestDialogVisible;

[ObservableProperty]
private string _testDialogTitle = string.Empty;

[ObservableProperty]
private bool? _testDialogSuccess;

public ObservableCollection<string> TestDialogLines { get; } = new();

public bool IsTestDialogRunning => TestDialogSuccess is null && IsTestDialogVisible;
```

**Step 2: Add the dismiss command**

After the existing `DismissDeleteBlockedDialog` method (around line 788), add:

```csharp
[RelayCommand]
private void DismissTestDialog()
{
    IsTestDialogVisible = false;
    TestDialogTitle = string.Empty;
    TestDialogLines.Clear();
    TestDialogSuccess = null;
}
```

**Step 3: Add property change notification for IsTestDialogRunning**

Add a partial method:

```csharp
partial void OnTestDialogSuccessChanged(bool? value)
{
    OnPropertyChanged(nameof(IsTestDialogRunning));
}
```

**Step 4: Build to verify compilation**

Run: `dotnet build RcloneMountManager.slnx`
Expected: Build succeeded. 0 Error(s)

**Step 5: Commit**

```
feat: add test dialog ViewModel properties
```

---

### Task 2: Wire Test Dialog into TestConnectionAsync

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs:688-714` (TestConnectionAsync method)

**Step 1: Write failing test**

Create test in `RcloneMountManager.Tests/ViewModels/MainWindowViewModelTestDialogTests.cs`:

```csharp
using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelTestDialogTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"test-dialog-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task TestConnection_OpensDialogAndShowsSuccess()
    {
        var logLines = new List<string>();
        var vm = CreateViewModel(
            testConnectionRunner: (profile, log, _) =>
            {
                log("Listing objects...");
                log("Connectivity test succeeded.");
                return Task.CompletedTask;
            });

        // Select a mount profile with valid source
        var profile = vm.SelectedProfile;
        profile.Source = "myremote:bucket";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.IsTestDialogRunning);
        Assert.True(vm.TestDialogSuccess);
        Assert.True(vm.IsTestDialogVisible);
        Assert.Equal("Connection test passed", vm.TestDialogTitle);
        Assert.Contains(vm.TestDialogLines, l => l.Contains("Connectivity test succeeded."));
    }

    [Fact]
    public async Task TestConnection_OpensDialogAndShowsFailure()
    {
        var vm = CreateViewModel(
            testConnectionRunner: (profile, log, _) =>
            {
                log("ERR: couldn't connect");
                throw new InvalidOperationException("Connectivity test failed with exit code 1.");
            });

        var profile = vm.SelectedProfile;
        profile.Source = "myremote:bucket";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.IsTestDialogRunning);
        Assert.False(vm.TestDialogSuccess);
        Assert.True(vm.IsTestDialogVisible);
        Assert.Equal("Connection test failed", vm.TestDialogTitle);
        Assert.Contains(vm.TestDialogLines, l => l.Contains("exit code 1"));
    }

    [Fact]
    public async Task DismissTestDialog_ClearsState()
    {
        var vm = CreateViewModel(
            testConnectionRunner: (_, log, _) =>
            {
                log("OK");
                return Task.CompletedTask;
            });

        vm.SelectedProfile.Source = "myremote:bucket";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.IsTestDialogVisible);

        vm.DismissTestDialogCommand.Execute(null);

        Assert.False(vm.IsTestDialogVisible);
        Assert.Empty(vm.TestDialogLines);
        Assert.Null(vm.TestDialogSuccess);
    }

    private MainWindowViewModel CreateViewModel(
        Func<MountProfile, Action<string>, CancellationToken, Task>? testConnectionRunner = null)
    {
        return new MainWindowViewModel(
            profilesFilePath: CreateProfilesPath(),
            mountStartRunner: (_, _, _) => Task.CompletedTask,
            testConnectionRunner: testConnectionRunner,
            runtimeStateVerifier: (_, _) => Task.FromResult(
                new ProfileRuntimeState(MountLifecycleState.Idle, MountHealthState.Unknown, DateTimeOffset.UtcNow, null)),
            startupEnabledProbe: _ => false,
            runtimeRefreshWaiter: (_, _) => Task.FromResult(false),
            runtimeStateBatchVerifier: (_, _) => Task.FromResult<IReadOnlyList<ProfileRuntimeState>>(Array.Empty<ProfileRuntimeState>()),
            loadStartupData: false);
    }

    private string CreateProfilesPath()
    {
        Directory.CreateDirectory(_tempRoot);
        return Path.Combine(_tempRoot, "profiles.json");
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test RcloneMountManager.Tests/ --filter "FullyQualifiedName~TestDialogTests"`
Expected: FAIL (testConnectionRunner parameter doesn't exist yet)

**Step 3: Add testConnectionRunner parameter to ViewModel constructor**

In `MainWindowViewModel.cs`, the constructor (around line 175) needs a new optional parameter. Find the constructor parameter list and add:

```csharp
Func<MountProfile, Action<string>, CancellationToken, Task>? testConnectionRunner = null,
```

Store it in a field:

```csharp
private readonly Func<MountProfile, Action<string>, CancellationToken, Task>? _testConnectionRunner;
```

Assign in constructor body:

```csharp
_testConnectionRunner = testConnectionRunner;
```

**Step 4: Rewrite TestConnectionAsync to use the dialog**

Replace the `TestConnectionAsync` method (lines 688-714) with:

```csharp
[RelayCommand(CanExecute = nameof(CanTestConnection))]
private async Task TestConnectionAsync()
{
    TestDialogLines.Clear();
    TestDialogSuccess = null;
    TestDialogTitle = "Testing connection...";
    IsTestDialogVisible = true;

    try
    {
        var profile = SelectedProfile;
        var profileId = profile.Id;
        AppendLog(profileId, ProfileLogCategory.General, ProfileLogStage.Initialization, $"Testing connection for '{profile.Name}'...");

        void LogLine(string line)
        {
            AppendLog(profileId, ProfileLogCategory.General, ProfileLogStage.Execution, line);
            TestDialogLines.Add(line);
        }

        if (_testConnectionRunner is not null)
        {
            await _testConnectionRunner(profile, LogLine, CancellationToken.None);
        }
        else if (profile.IsRemoteDefinition && SelectedBackend is not null)
        {
            var binary = profile.RcloneBinaryPath ?? "rclone";
            await _mountManagerService.TestBackendConnectionAsync(
                binary,
                SelectedBackend.Name,
                BackendOptionInputs.Concat(AdvancedBackendOptionInputs),
                LogLine,
                CancellationToken.None);
        }
        else
        {
            await _mountManagerService.TestConnectionAsync(profile, LogLine, CancellationToken.None);
        }

        TestDialogSuccess = true;
        TestDialogTitle = "Connection test passed";
        StatusText = "Connectivity test passed.";
    }
    catch (Exception ex)
    {
        TestDialogLines.Add(ex.Message);
        TestDialogSuccess = false;
        TestDialogTitle = "Connection test failed";
        StatusText = "Connectivity test failed.";
        AppendLog(ProfileLogCategory.General, ProfileLogStage.Execution, ex.Message, ProfileLogSeverity.Error, ex.Message);
    }
}
```

Note: This method no longer uses `RunBusyActionAsync` since the dialog itself serves as the progress indicator. We still set `IsBusy` behavior implicitly through the dialog visibility -- but we should NOT wrap in `RunBusyActionAsync` because that would swallow the exception before we can handle it in the dialog. The `CanTestConnection` guard already prevents double-invocation.

**Step 5: Run tests**

Run: `dotnet test RcloneMountManager.Tests/ --filter "FullyQualifiedName~TestDialogTests"`
Expected: All 3 tests pass

**Step 6: Run full test suite**

Run: `dotnet test RcloneMountManager.Tests/`
Expected: All tests pass

**Step 7: Commit**

```
feat: wire test connection dialog with live output and result state
```

---

### Task 3: Add Test Dialog XAML

**Files:**
- Modify: `RcloneMountManager.GUI/Views/MainWindow.axaml:524-543` (after the delete-blocked dialog)

**Step 1: Add the test connection dialog overlay**

After the existing delete-blocked dialog Border (line 543), add:

```xml
<Border Grid.RowSpan="2"
        IsVisible="{Binding IsTestDialogVisible}"
        Background="#66000000"
        ZIndex="100">
    <Grid HorizontalAlignment="Center" VerticalAlignment="Center" Width="560" MaxWidth="760" Margin="20">
        <Border Background="{DynamicResource SystemControlPageBackgroundChromeLowBrush}"
                BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}"
                BorderThickness="1"
                CornerRadius="12"
                Padding="16">
            <StackPanel Spacing="12">
                <Grid ColumnDefinitions="Auto,*">
                    <TextBlock Grid.Column="0"
                               Text="&#x2714;"
                               FontSize="20"
                               Foreground="Green"
                               IsVisible="{Binding TestDialogSuccess}"
                               Margin="0,0,8,0"
                               VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="0"
                               Text="&#x2718;"
                               FontSize="20"
                               Foreground="Red"
                               IsVisible="{Binding TestDialogSuccess, Converter={x:Static BoolConverters.Not}, FallbackValue=False}"
                               Margin="0,0,8,0"
                               VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="1"
                               Text="{Binding TestDialogTitle}"
                               FontSize="18"
                               FontWeight="SemiBold"
                               VerticalAlignment="Center"/>
                </Grid>
                <ListBox ItemsSource="{Binding TestDialogLines}"
                         ScrollViewer.VerticalScrollBarVisibility="Auto"
                         MaxHeight="300"
                         MinHeight="80"
                         FontFamily="Cascadia Mono, Consolas, Menlo"
                         FontSize="12"/>
                <Button Content="OK"
                        Command="{Binding DismissTestDialogCommand}"
                        HorizontalAlignment="Right"/>
            </StackPanel>
        </Border>
    </Grid>
</Border>
```

Note: The `BoolConverters.Not` approach for the red X may need adjustment since `TestDialogSuccess` is `bool?` (nullable). The green check should only show when `TestDialogSuccess == true` and red X when `TestDialogSuccess == false`. When `null` (running), neither should show. This may require a simple value converter or using `IsTestDialogRunning` with additional computed properties like `IsTestDialogSuccess` and `IsTestDialogFailure` (non-nullable bools). Adjust as needed during implementation -- the simplest approach is to add two computed properties:

In the ViewModel:
```csharp
public bool ShowTestDialogSuccessIcon => TestDialogSuccess == true;
public bool ShowTestDialogFailureIcon => TestDialogSuccess == false;
```

And in the `OnTestDialogSuccessChanged` partial method, add:
```csharp
OnPropertyChanged(nameof(ShowTestDialogSuccessIcon));
OnPropertyChanged(nameof(ShowTestDialogFailureIcon));
```

Then use `IsVisible="{Binding ShowTestDialogSuccessIcon}"` and `IsVisible="{Binding ShowTestDialogFailureIcon}"` in the XAML.

**Step 2: Build and verify**

Run: `dotnet build RcloneMountManager.slnx`
Expected: Build succeeded.

**Step 3: Commit**

```
feat: add test connection dialog XAML overlay
```

---

### Task 4: Add ShowDiagnosticsView ViewModel Property and Sidebar Logic

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`

**Step 1: Write failing test**

Add to `RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs`:

```csharp
[Fact]
public void ShowDiagnosticsView_DefaultsFalse()
{
    var viewModel = CreateViewModel();
    Assert.False(viewModel.ShowDiagnosticsView);
}

[Fact]
public void SelectDiagnostics_ShowsDiagnosticsView()
{
    var viewModel = CreateViewModel();
    viewModel.SelectDiagnosticsCommand.Execute(null);

    Assert.True(viewModel.ShowDiagnosticsView);
    Assert.Equal("Diagnostics", viewModel.WorkspaceTitle);
}

[Fact]
public void SelectRemoteAfterDiagnostics_HidesDiagnosticsView()
{
    var viewModel = CreateViewModel();
    viewModel.SelectDiagnosticsCommand.Execute(null);
    Assert.True(viewModel.ShowDiagnosticsView);

    viewModel.AddRemoteCommand.Execute(null);
    Assert.False(viewModel.ShowDiagnosticsView);
    Assert.True(viewModel.ShowRemoteEditor);
}

[Fact]
public void SelectMountAfterDiagnostics_HidesDiagnosticsView()
{
    var viewModel = CreateViewModel();
    viewModel.SelectDiagnosticsCommand.Execute(null);
    Assert.True(viewModel.ShowDiagnosticsView);

    viewModel.AddProfileCommand.Execute(null);
    Assert.False(viewModel.ShowDiagnosticsView);
    Assert.False(viewModel.ShowRemoteEditor);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test RcloneMountManager.Tests/ --filter "FullyQualifiedName~DiagnosticsTests"`
Expected: FAIL (ShowDiagnosticsView doesn't exist)

**Step 3: Add ViewModel properties and command**

Add observable property:

```csharp
[ObservableProperty]
private bool _showDiagnosticsView;
```

Add command:

```csharp
[RelayCommand]
private void SelectDiagnostics()
{
    ShowDiagnosticsView = true;
    ShowRemoteEditor = false;
}
```

Update `WorkspaceTitle` and `WorkspaceSubtitle` computed properties:

```csharp
public string WorkspaceTitle => ShowDiagnosticsView ? "Diagnostics" : ShowRemoteEditor ? "Remote Assistant" : "Mount Assistant";
public string WorkspaceSubtitle => ShowDiagnosticsView
    ? "View log entries across all profiles"
    : ShowRemoteEditor
        ? "Choose backend -> set options -> create remote"
        : "Preset -> credentials -> mount path -> Start mount";
```

In `OnSelectedProfileChanged` (line 1527), add at the start of the method body (after existing code):

```csharp
if (ShowDiagnosticsView)
{
    ShowDiagnosticsView = false;
}
```

Also ensure `OnShowDiagnosticsViewChanged` notifies the workspace labels:

```csharp
partial void OnShowDiagnosticsViewChanged(bool value)
{
    OnPropertyChanged(nameof(WorkspaceTitle));
    OnPropertyChanged(nameof(WorkspaceSubtitle));
}
```

**Step 4: Run tests**

Run: `dotnet test RcloneMountManager.Tests/ --filter "FullyQualifiedName~DiagnosticsTests"`
Expected: All new tests pass

**Step 5: Run full test suite**

Run: `dotnet test RcloneMountManager.Tests/`
Expected: All tests pass

**Step 6: Commit**

```
feat: add ShowDiagnosticsView property and SelectDiagnostics command
```

---

### Task 5: Update XAML -- Add Diagnostics Sidebar Section and Main Content View

**Files:**
- Modify: `RcloneMountManager.GUI/Views/MainWindow.axaml`

**Step 1: Add DIAGNOSTICS section to sidebar**

After the "No mounts yet" TextBlock (line 149), before the closing `</StackPanel>` of the scroll area (line 150), add:

```xml
<Grid ColumnDefinitions="*" Margin="4,8,4,0">
    <TextBlock Text="DIAGNOSTICS" FontSize="11" FontWeight="SemiBold" Opacity="0.75" VerticalAlignment="Center"/>
</Grid>
<ListBox Background="Transparent"
         BorderThickness="0"
         ScrollViewer.VerticalScrollBarVisibility="Disabled"
         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
         SelectionMode="Single">
    <ListBoxItem Command="{Binding SelectDiagnosticsCommand}">
        <StackPanel Margin="4,4,4,6" Spacing="2">
            <TextBlock Text="Logs" FontWeight="SemiBold"/>
            <TextBlock Text="View all log entries"
                       FontSize="11"
                       Opacity="0.75"/>
        </StackPanel>
    </ListBoxItem>
</ListBox>
```

**Step 2: Add diagnostics main content view**

In the main content area, after the `<StackPanel IsVisible="{Binding !ShowRemoteEditor}" ...>` closing tag (around line 494 -- the mount editor's closing `</StackPanel>`), and before the closing `</StackPanel>` of the outer wrapper, add a new panel for diagnostics:

```xml
<StackPanel IsVisible="{Binding ShowDiagnosticsView}" Spacing="10">
    <TextBlock Text="Log entries" FontWeight="SemiBold" FontSize="14"/>
    <Grid ColumnDefinitions="200,*" RowDefinitions="Auto,Auto" RowSpacing="6">
        <TextBlock Grid.Row="0" Grid.Column="0"
                   Text="Filter by profile"
                   VerticalAlignment="Center"/>
        <ComboBox Grid.Row="0" Grid.Column="1"
                  Margin="8,0,0,0"
                  ItemsSource="{Binding DiagnosticsProfileFilters}"
                  SelectedValue="{Binding SelectedDiagnosticsProfileId}"
                  SelectedValueBinding="{Binding ProfileId}">
            <ComboBox.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis"/>
                </DataTemplate>
            </ComboBox.ItemTemplate>
        </ComboBox>

        <TextBlock Grid.Row="1" Grid.Column="0"
                   Text="Startup events only"
                   VerticalAlignment="Center"/>
        <CheckBox Grid.Row="1" Grid.Column="1"
                  Margin="8,0,0,0"
                  IsChecked="{Binding StartupTimelineOnly}"/>
    </Grid>
    <TextBlock Text="Use filters to narrow down log entries."
               FontSize="11"
               Opacity="0.6"
               Margin="0,-2,0,0"/>
    <ListBox ItemsSource="{Binding DiagnosticsRows}"
             IsVisible="{Binding HasDiagnosticsRows}"
             ScrollViewer.VerticalScrollBarVisibility="Auto"
             ScrollViewer.HorizontalScrollBarVisibility="Auto"
             MinHeight="200"
             MaxHeight="500">
        <ListBox.ItemTemplate>
            <DataTemplate>
                <Grid ColumnDefinitions="170,90,180,*" ColumnSpacing="8">
                    <TextBlock Grid.Column="0"
                               Text="{Binding TimestampText}"
                               FontFamily="Cascadia Mono, Consolas, Menlo"/>
                    <TextBlock Grid.Column="1"
                               Text="{Binding SeverityText}"
                               FontFamily="Cascadia Mono, Consolas, Menlo"/>
                    <TextBlock Grid.Column="2"
                               Text="{Binding StageText}"
                               FontFamily="Cascadia Mono, Consolas, Menlo"/>
                    <TextBlock Grid.Column="3"
                               Text="{Binding MessageText}"
                               FontFamily="Cascadia Mono, Consolas, Menlo"
                               TextWrapping="Wrap"/>
                </Grid>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>
    <Border IsVisible="{Binding !HasDiagnosticsRows}"
            BorderThickness="1"
            BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}"
            CornerRadius="8"
            Padding="10"
            MinHeight="72">
        <TextBlock Text="{Binding DiagnosticsEmptyStateText}"
                   VerticalAlignment="Center"
                   FontSize="12"
                   Opacity="0.75"/>
    </Border>
</StackPanel>
```

**Step 3: Hide remote/mount editors when diagnostics is shown**

The existing `ShowRemoteEditor` and `!ShowRemoteEditor` panels need to also be hidden when `ShowDiagnosticsView` is true. The simplest approach: wrap both existing editor panels in a container that hides when diagnostics is active:

Change the remote editor `IsVisible`:
- From: `IsVisible="{Binding ShowRemoteEditor}"`
- To: Show only when `ShowRemoteEditor` is true AND `ShowDiagnosticsView` is false

Since Avalonia doesn't have multi-binding easily, add a computed property in ViewModel:

```csharp
public bool ShowRemoteEditorContent => ShowRemoteEditor && !ShowDiagnosticsView;
public bool ShowMountEditorContent => !ShowRemoteEditor && !ShowDiagnosticsView;
```

Update the `OnShowRemoteEditorChanged` and `OnShowDiagnosticsViewChanged` partial methods to notify these:

```csharp
OnPropertyChanged(nameof(ShowRemoteEditorContent));
OnPropertyChanged(nameof(ShowMountEditorContent));
```

Then update the XAML:
- Remote editor: `IsVisible="{Binding ShowRemoteEditorContent}"`
- Mount editor: `IsVisible="{Binding ShowMountEditorContent}"`

**Step 4: Remove old inline diagnostics**

Remove the entire "Diagnostics Timeline" section from the mount editor area (lines 426-492 in the original AXAML). This is the block starting with `<!-- Diagnostics Timeline -->` and ending with the empty-state Border.

**Step 5: Build and verify**

Run: `dotnet build RcloneMountManager.slnx`
Expected: Build succeeded.

**Step 6: Run full test suite**

Run: `dotnet test RcloneMountManager.Tests/`
Expected: All tests pass

**Step 7: Commit**

```
feat: add diagnostics sidebar section and move log view to dedicated content area
```

---

### Task 6: Final Verification

**Step 1: Full build**

Run: `dotnet build RcloneMountManager.slnx`
Expected: Build succeeded. 0 Warning(s) 0 Error(s)

**Step 2: Full test suite**

Run: `dotnet test RcloneMountManager.Tests/`
Expected: All tests pass

**Step 3: Verify the complete change set**

Run: `git diff --stat`
Review all changed files make sense.
