# Test Connection Dialog + Diagnostics Sidebar

## Problem

1. When a test connection fails, the error is buried in the diagnostics timeline with only a generic "Operation failed." in the status bar. Users cannot easily see what went wrong.
2. The "Diagnostics timeline" section with "Profile scope" and "Timeline scope" labels is confusing -- the terminology is opaque.
3. The diagnostics section is inline in the editor, making it hard to find and mixed with profile configuration.

## Design

### Change 1: Live Test Connection Dialog

A modal dialog that opens immediately when the user clicks "Test connection" and shows progress in real time.

**Behavior:**
- Dialog opens as soon as the test starts
- Title: "Testing connection..."
- Log lines stream in as they arrive, displayed in a scrollable monospace text area
- When complete:
  - **Success**: Title changes to "Connection test passed" with a green checkmark indicator
  - **Failure**: Title changes to "Connection test failed" with a red X indicator
- User dismisses with an "OK" button (always visible)
- The dialog captures all log lines from the test operation (`AppendLog` calls route to the dialog while it's open)

**ViewModel additions:**
- `IsTestDialogVisible` (bool) -- controls dialog overlay visibility
- `TestDialogTitle` (string) -- "Testing connection..." / "Connection test passed" / "Connection test failed"
- `TestDialogSuccess` (bool?) -- null while running, true on success, false on failure
- `TestDialogLines` (ObservableCollection<string>) -- live log output
- `DismissTestDialogCommand` -- closes the dialog

**Flow:**
1. `TestConnectionAsync()` sets `IsTestDialogVisible = true`, clears `TestDialogLines`, sets title to "Testing connection...", `TestDialogSuccess = null`
2. The log callback appends each line to `TestDialogLines`
3. On success: set `TestDialogSuccess = true`, title to "Connection test passed"
4. On failure: set `TestDialogSuccess = false`, title to "Connection test failed", append error message
5. User clicks OK -> `IsTestDialogVisible = false`

### Change 2: Diagnostics Sidebar Entry

Move diagnostics from inline in the editor to a dedicated sidebar section.

**Sidebar layout:**
```
REMOTES
  [remote entries...]
MOUNTS
  [mount entries...]
DIAGNOSTICS
  Logs
```

**Behavior:**
- Clicking "Logs" sets the main content area to show the diagnostics log view
- New ViewModel property: `ShowDiagnosticsView` (bool) -- when true, the main content shows the log view instead of the remote/mount editor
- Selecting a remote or mount in the sidebar switches back to the editor view

**Log view content (moved from inline):**
- "Filter by profile" dropdown (was "Profile scope")
- "Startup events only" checkbox (was "Timeline scope" / "Startup path only")
- The diagnostics timeline ListBox (same columns: timestamp, severity, stage, message)
- Helper text: "Use filters to narrow down log entries."

### Change 3: Remove Inline Diagnostics

Remove the "Diagnostics timeline" section from the editor scroll area (lines 426-492 in MainWindow.axaml). The diagnostics are now only accessible via the sidebar.

## Files to Modify

- `MainWindow.axaml` -- add test dialog overlay, add DIAGNOSTICS sidebar section, add diagnostics main content view, remove inline diagnostics
- `MainWindowViewModel.cs` -- add test dialog properties/commands, add `ShowDiagnosticsView` property, modify `TestConnectionAsync` to use dialog, add sidebar diagnostics selection logic
- Tests for new dialog behavior and sidebar navigation
