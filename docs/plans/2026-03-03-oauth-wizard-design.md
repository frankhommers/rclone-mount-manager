# OAuth Remote Creation Wizard — Design

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable non-technical users to create OAuth-based rclone remotes (OneDrive, Google Drive, Dropbox, etc.) through a step-by-step wizard in the GUI, using rclone's `--non-interactive` state machine.

**Architecture:** A new `RcloneConfigWizardService` drives rclone's `config create --non-interactive --continue` loop via CliWrap. Each step returns a JSON question; the service parses it into a `ConfigWizardStep` model. The ViewModel manages wizard state and renders each step as a card. The OAuth step opens the system browser via `Launcher.LaunchUriAsync` and streams stderr to detect the auth URL. After completion, `rclone config dump` reads back the config into the existing form.

**Tech Stack:** C# / .NET 10, CliWrap (buffered + streaming stderr), Avalonia UI, CommunityToolkit.Mvvm, xUnit

---

## Key Discovery: rclone's --non-interactive Protocol

```
rclone config create <name> <backend> --non-interactive [--all]
```

Returns JSON to stdout:
```json
{
  "State": "*oauth-islocal,choose_type,,",
  "Option": {
    "Name": "config_is_local",
    "Help": "Use web browser to automatically authenticate...",
    "Default": true,
    "Examples": [{"Value": "true", "Help": "Yes"}, ...],
    "Type": "bool",
    "Exclusive": true,
    "IsPassword": false,
    "Required": false
  },
  "Error": ""
}
```

Answer with:
```
rclone config update <name> --continue --state "<state>" --result "<answer>" --non-interactive
```

Repeat until `State == ""` (completion).

### OAuth Step Behavior

When `Option.Name == "config_is_local"` and we answer `"true"`:
- rclone starts a local HTTP server on port 53682
- Outputs NOTICE lines to **stderr** with the auth URL
- **Blocks stdout** until the user completes auth in the browser
- After auth, outputs the next JSON question to stdout

This means the OAuth step requires **streaming stderr** while waiting for stdout to unblock. CliWrap's `PipeTarget.ToDelegate` on stderr handles this.

---

## Task 1: ConfigWizardStep Model

**Files:**
- Create: `RcloneMountManager.Core/Models/ConfigWizardStep.cs`

**Step 1: Create the model**

```csharp
using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public sealed class ConfigWizardStep
{
  public string State { get; init; } = string.Empty;
  public string Name { get; init; } = string.Empty;
  public string Help { get; init; } = string.Empty;
  public string Type { get; init; } = "string";
  public string DefaultValue { get; init; } = string.Empty;
  public bool Required { get; init; }
  public bool IsPassword { get; init; }
  public bool Exclusive { get; init; }
  public string Error { get; init; } = string.Empty;
  public List<ConfigWizardExample> Examples { get; init; } = [];

  public bool IsComplete => string.IsNullOrEmpty(State);
  public bool IsOAuthBrowserPrompt => string.Equals(Name, "config_is_local", System.StringComparison.Ordinal);
  public bool IsAdvancedPrompt => string.Equals(Name, "config_fs_advanced", System.StringComparison.Ordinal);
}

public sealed class ConfigWizardExample
{
  public string Value { get; init; } = string.Empty;
  public string Help { get; init; } = string.Empty;
}
```

**Step 2: Commit**

```
git add RcloneMountManager.Core/Models/ConfigWizardStep.cs
git commit -m "feat: add ConfigWizardStep model for non-interactive rclone config"
```

---

## Task 2: RcloneConfigWizardService

**Files:**
- Create: `RcloneMountManager.Core/Services/RcloneConfigWizardService.cs`
- Test: `RcloneMountManager.Tests/Services/RcloneConfigWizardServiceTests.cs`

This service wraps the `rclone config create --non-interactive` state machine. It has two key methods:

1. `StartAsync(binary, remoteName, backendName)` — initial call, returns first `ConfigWizardStep`
2. `ContinueAsync(binary, remoteName, state, result)` — sends answer, returns next step

The OAuth step is special: when we detect `config_is_local`, we auto-answer `"true"` and then need to stream stderr to extract the auth URL while waiting for stdout.

3. `ContinueOAuthAsync(binary, remoteName, state, onAuthUrl, ct)` — handles the OAuth blocking step. Uses CliWrap streaming: `PipeTarget.ToDelegate` on stderr to detect the auth URL line, `ExecuteBufferedAsync` equivalent on stdout for the JSON result. Calls `onAuthUrl(string url)` callback when the auth URL is detected. Returns the next `ConfigWizardStep` after OAuth completes.

4. `ReadRemoteConfigAsync(binary, remoteName)` — reads `rclone config dump` and extracts the key-value pairs for the named remote.

**Step 1: Write failing tests**

Test `StartAsync` by mocking CliWrap output (inject a `Func<string[], Task<(string stdout, string stderr, int exitCode)>>` runner for testability, same pattern as `_testConnectionRunner` in the ViewModel).

Test `ContinueAsync` similarly.

Test `ReadRemoteConfigAsync` parsing.

**Step 2: Implement the service**

Key implementation details:
- `StartAsync` runs: `rclone config create <name> <backend> --non-interactive`
- `ContinueAsync` runs: `rclone config update <name> --continue --state "<state>" --result "<result>" --non-interactive`
- `ContinueOAuthAsync` runs the same command but uses `Cli.Wrap(...).WithStandardErrorPipe(PipeTarget.ToDelegate(line => ...))` to capture stderr lines and extract the URL matching `http://127.0.0.1:53682/auth?state=...`
- JSON deserialization uses a private `WizardResponseDto` matching rclone's output shape
- All passwords passed via `--result` are in cleartext (rclone docs: "when using --continue all passwords should be passed in the clear")

**Step 3: Commit**

```
git commit -m "feat: add RcloneConfigWizardService for non-interactive remote creation"
```

---

## Task 3: Add RequiresOAuth to RcloneBackendInfo

**Files:**
- Modify: `RcloneMountManager.Core/Models/RcloneBackendInfo.cs`
- Modify: `RcloneMountManager.Core/Services/RcloneBackendService.cs:45-69`

**Step 1: Add property**

```csharp
// RcloneBackendInfo.cs
public bool RequiresOAuth { get; set; }
```

**Step 2: Set it during parsing in GetBackendsAsync**

In the `.Select(p => new RcloneBackendInfo { ... })` block, add:
```csharp
RequiresOAuth = p.Options.Any(o =>
    string.Equals(o.Name, "token", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(o.Type, "string", StringComparison.OrdinalIgnoreCase)),
```

**Step 3: Commit**

```
git commit -m "feat: detect OAuth backends via token option in config providers"
```

---

## Task 4: Register Service in DI

**Files:**
- Modify: `RcloneMountManager.GUI/Program.cs:40`

**Step 1: Add registration**

```csharp
services.AddSingleton<RcloneConfigWizardService>();
```

**Step 2: Inject into MainWindowViewModel constructor**

Add parameter and field, following existing pattern with `RcloneBackendService`.

**Step 3: Commit**

```
git commit -m "feat: register RcloneConfigWizardService in DI container"
```

---

## Task 5: Wizard ViewModel State

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`

Add wizard state properties:

```csharp
[ObservableProperty] private bool _isWizardActive;
[ObservableProperty] private ConfigWizardStep? _currentWizardStep;
[ObservableProperty] private string _wizardAnswer = string.Empty;
[ObservableProperty] private bool _isWizardWaitingForOAuth;
[ObservableProperty] private string _wizardOAuthUrl = string.Empty;
[ObservableProperty] private int _wizardStepNumber;
private string? _wizardState;
```

Computed properties:
```csharp
public bool ShowWizardContent => IsWizardActive && ShowRemoteEditorContent;
public bool ShowWizardOAuthSpinner => IsWizardWaitingForOAuth;
public string WizardStepTitle => CurrentWizardStep?.Name ?? string.Empty;
public string WizardStepHelp => CurrentWizardStep?.Help ?? string.Empty;
public bool WizardHasExamples => CurrentWizardStep?.Examples.Count > 0;
```

**Step 1: Commit**

```
git commit -m "feat: add wizard state properties to MainWindowViewModel"
```

---

## Task 6: Wizard Commands

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`

### StartWizardCommand

```csharp
[RelayCommand(CanExecute = nameof(CanStartWizard))]
private async Task StartWizardAsync()
{
    // 1. Start rclone config create --non-interactive
    // 2. Parse first step
    // 3. If step.IsComplete → wizard done (sftp-like backends with no questions)
    // 4. If step.IsOAuthBrowserPrompt → auto-answer true, start OAuth flow
    // 5. Otherwise → show step in UI
    IsWizardActive = true;
    WizardStepNumber = 1;
    var step = await _wizardService.StartAsync(binary, NewRemoteName, SelectedBackend.Name, ct);
    await HandleWizardStep(step);
}
```

### SubmitWizardAnswerCommand

```csharp
[RelayCommand]
private async Task SubmitWizardAnswerAsync()
{
    // 1. Send answer via ContinueAsync
    // 2. Handle next step (same as above)
    WizardStepNumber++;
    var step = await _wizardService.ContinueAsync(binary, NewRemoteName, _wizardState, WizardAnswer, ct);
    await HandleWizardStep(step);
}
```

### HandleWizardStep (private)

```csharp
private async Task HandleWizardStep(ConfigWizardStep step)
{
    if (step.IsComplete)
    {
        // Read config back, populate form, deactivate wizard
        await ReadBackWizardConfig();
        IsWizardActive = false;
        return;
    }

    if (step.IsOAuthBrowserPrompt)
    {
        // Auto-answer true, start OAuth flow
        IsWizardWaitingForOAuth = true;
        var nextStep = await _wizardService.ContinueOAuthAsync(
            binary, NewRemoteName, step.State,
            url => {
                WizardOAuthUrl = url;
                // Open browser via Avalonia Launcher
                Dispatcher.UIThread.Post(async () =>
                    await TopLevel.GetTopLevel(...)!.Launcher.LaunchUriAsync(new Uri(url)));
            },
            ct);
        IsWizardWaitingForOAuth = false;
        await HandleWizardStep(nextStep);
        return;
    }

    if (step.IsAdvancedPrompt)
    {
        // Auto-answer false (skip advanced)
        var nextStep = await _wizardService.ContinueAsync(binary, NewRemoteName, step.State, "false", ct);
        await HandleWizardStep(nextStep);
        return;
    }

    // Normal question — show in UI
    CurrentWizardStep = step;
    _wizardState = step.State;
    WizardAnswer = step.DefaultValue;
}
```

### ReadBackWizardConfig (private)

```csharp
private async Task ReadBackWizardConfig()
{
    var config = await _wizardService.ReadRemoteConfigAsync(binary, NewRemoteName);
    // Map config key-value pairs back to BackendOptionInputs
    foreach (var input in BackendOptionInputs.Concat(AdvancedBackendOptionInputs))
    {
        if (config.TryGetValue(input.Name, out string? value))
            input.Value = value;
    }
    // Mark profile as wizard-configured
    // Save profiles
}
```

### CancelWizardCommand

```csharp
[RelayCommand]
private void CancelWizard()
{
    IsWizardActive = false;
    // Optionally: rclone config delete <name> to clean up partial config
}
```

**Step 1: Commit**

```
git commit -m "feat: add wizard commands (start, submit, cancel, OAuth handling)"
```

---

## Task 7: Wizard UI

**Files:**
- Modify: `RcloneMountManager.GUI/Views/MainWindow.axaml`

Add wizard panel inside the Remote Editor content area, toggled by `IsWizardActive`. Structure:

```xml
<!-- Wizard overlay / panel -->
<StackPanel IsVisible="{Binding ShowWizardContent}" Spacing="14">

    <!-- Step indicator -->
    <TextBlock Text="{Binding WizardStepTitle, StringFormat='Step {0}'}" FontWeight="SemiBold" FontSize="16"/>

    <!-- Help text -->
    <TextBlock Text="{Binding WizardStepHelp}" TextWrapping="Wrap" Opacity="0.75"/>

    <!-- OAuth waiting state -->
    <StackPanel IsVisible="{Binding ShowWizardOAuthSpinner}" Spacing="8">
        <ProgressBar IsIndeterminate="True"/>
        <TextBlock Text="Waiting for authorization in your browser..."/>
        <TextBlock Text="{Binding WizardOAuthUrl}" FontSize="11" Opacity="0.6" TextWrapping="Wrap"/>
    </StackPanel>

    <!-- Input for current step (when not OAuth) -->
    <StackPanel IsVisible="{Binding !ShowWizardOAuthSpinner}" Spacing="8">

        <!-- ComboBox for exclusive options with examples -->
        <ComboBox IsVisible="{Binding WizardHasExamples}"
                  ItemsSource="{Binding CurrentWizardStep.Examples}"
                  ... />

        <!-- TextBox for free-form input -->
        <TextBox IsVisible="{Binding !WizardHasExamples}"
                 Text="{Binding WizardAnswer}"
                 Watermark="{Binding CurrentWizardStep.DefaultValue}" />

        <!-- Navigation -->
        <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Content="Next" Command="{Binding SubmitWizardAnswerCommand}"/>
            <Button Content="Cancel" Command="{Binding CancelWizardCommand}"/>
        </StackPanel>
    </StackPanel>
</StackPanel>
```

**Step 1: Commit**

```
git commit -m "feat: add wizard UI panel to Remote Assistant"
```

---

## Task 8: "Setup Wizard" Button

**Files:**
- Modify: `RcloneMountManager.GUI/Views/MainWindow.axaml`

Add a "Setup Wizard" button next to (or near) the existing "Create Remote" button. For OAuth backends, show an info hint: "This backend requires browser authorization — use the Setup Wizard."

```xml
<Button Content="Setup Wizard"
        Command="{Binding StartWizardCommand}"
        IsVisible="{Binding ShowRemoteEditorContent}"/>

<!-- OAuth hint -->
<TextBlock IsVisible="{Binding SelectedBackend.RequiresOAuth}"
           Text="This backend requires browser authorization. Use the Setup Wizard to configure it."
           FontSize="11" Opacity="0.75" Foreground="#E0A000"/>
```

**Step 1: Commit**

```
git commit -m "feat: add Setup Wizard button with OAuth hint to Remote Assistant"
```

---

## Task 9: Integration Tests

**Files:**
- Create: `RcloneMountManager.Tests/Services/RcloneConfigWizardServiceTests.cs`
- Modify: `RcloneMountManager.Tests/ViewModels/` (wizard state tests)

Test scenarios:
1. Non-OAuth backend (sftp): `StartAsync` returns `IsComplete == true` immediately
2. OAuth backend (onedrive): `StartAsync` returns `IsOAuthBrowserPrompt` step
3. `ContinueAsync` with valid state/result returns next step
4. `ReadRemoteConfigAsync` correctly parses `rclone config dump` output
5. ViewModel: `StartWizardAsync` sets `IsWizardActive`, `CancelWizard` resets state
6. ViewModel: `HandleWizardStep` auto-skips `IsAdvancedPrompt`

**Step 1: Commit**

```
git commit -m "test: add wizard service and viewmodel tests"
```

---

## Task 10: Cleanup & Polish

1. Hide the manual form fields while wizard is active (or dim them)
2. Show success message after wizard completion
3. Error handling: if rclone exits non-zero during wizard, show error and allow retry
4. Cancel wizard should run `rclone config delete <name>` to clean up partial config
5. Ensure existing "Create Remote" flow still works unchanged for non-wizard usage

**Step 1: Commit**

```
git commit -m "feat: wizard polish - error handling, cleanup, success message"
```

---

## Summary of OAuth Backends (18 detected)

box, drive, dropbox, filefabric, gcs, gphotos, hidrive, jottacloud, linkbox, mailru, onedrive, pcloud, pikpak, premiumizeme, putio, shade, sharefile, yandex, zoho
