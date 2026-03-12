using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelDiagnosticsTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(
    Path.GetTempPath(),
    $"main-window-diagnostics-tests-{Guid.NewGuid():N}");

  private readonly List<MainWindowViewModel> _viewModels = [];

  public void Dispose()
  {
    foreach (MainWindowViewModel viewModel in _viewModels)
    {
      viewModel.Dispose();
    }

    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Fact]
  public void ShowDiagnosticsView_DefaultsFalse()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    Assert.False(viewModel.ShowDiagnosticsView);
  }

  [Fact]
  public void SelectDiagnostics_ShowsDiagnosticsView()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.SelectDiagnosticsCommand.Execute(null);

    Assert.True(viewModel.ShowDiagnosticsView);
    Assert.Equal("Diagnostics", viewModel.WorkspaceTitle);
    Assert.True(viewModel.ShowDiagnosticsView);
    Assert.False(viewModel.ShowRemoteEditorContent);
    Assert.False(viewModel.ShowMountOperationsContent);
    Assert.False(viewModel.ShowMountConfigContent);
  }

  [Fact]
  public void SelectDashboard_ShowsOverviewWorkspace()
  {
    MainWindowViewModel viewModel = CreateViewModel();

    viewModel.SelectDiagnosticsCommand.Execute(null);
    viewModel.SelectDashboardCommand.Execute(null);

    Assert.True(viewModel.ShowDashboard);
    Assert.Equal("Overview", viewModel.WorkspaceTitle);
    Assert.Equal("Your mounts at a glance", viewModel.WorkspaceSubtitle);
    Assert.True(viewModel.ShowDashboardContent);
    Assert.False(viewModel.ShowEditorScrollViewer);
    Assert.False(viewModel.ShowDiagnosticsView);
  }

  [Fact]
  public void EnterAndExitConfigurationMode_TogglesMountContentState()
  {
    MainWindowViewModel viewModel = CreateViewModel();

    viewModel.SelectDashboardCommand.Execute(null);
    viewModel.AddMountCommand.Execute(null);
    viewModel.EnterConfigurationModeCommand.Execute(null);

    Assert.True(viewModel.IsConfigurationMode);
    Assert.True(viewModel.ShowMountConfigContent);
    Assert.True(viewModel.ShowConfigModeTestConnectionButton);
    Assert.False(viewModel.ShowMountOperationsContent);
    Assert.True(viewModel.ShowBackButton);

    viewModel.ExitConfigurationModeCommand.Execute(null);

    Assert.False(viewModel.IsConfigurationMode);
    Assert.True(viewModel.ShowMountOperationsContent);
    Assert.False(viewModel.ShowMountConfigContent);
    Assert.False(viewModel.ShowConfigModeTestConnectionButton);
  }

  [Fact]
  public void SelectProfileAfterDiagnostics_HidesDiagnosticsView()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.SelectDiagnosticsCommand.Execute(null);
    Assert.True(viewModel.ShowDiagnosticsView);

    viewModel.AddProfileCommand.Execute(null);
    Assert.False(viewModel.ShowDiagnosticsView);
  }

  [Fact]
  public async Task DiagnosticsCategoryFilter_FiltersRemotesAndMounts()
  {
    MainWindowViewModel viewModel = CreateViewModel(
      (_, _) => Task.CompletedTask,
      (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)));

    viewModel.AddRemoteCommand.Execute(null);
    MountProfile remoteProfile = viewModel.SelectedProfile;

    viewModel.AddProfileCommand.Execute(null);
    MountProfile mountProfile = viewModel.SelectedProfile;
    await viewModel.StartMountCommand.ExecuteAsync(null);

    viewModel.SelectDiagnosticsCommand.Execute(null);

    viewModel.SelectedDiagnosticsCategoryFilter = "Mounts";
    Assert.All(
      viewModel.DiagnosticsRows,
      row =>
      {
        bool isMount = viewModel.MountProfiles.Any(p => string.Equals(
                                                     p.Id,
                                                     row.ProfileId,
                                                     StringComparison.OrdinalIgnoreCase));
        Assert.True(isMount);
      });
  }

  [Fact]
  public async Task DiagnosticsSearchText_FiltersMessages()
  {
    MainWindowViewModel viewModel = CreateViewModel(
      (_, _) => Task.CompletedTask,
      (_, _) => Task.FromResult(
        CreateState(
          MountLifecycleState.Mounted,
          MountHealthState.Healthy,
          "unique-search-token")));

    await viewModel.StartMountCommand.ExecuteAsync(null);
    viewModel.SelectDiagnosticsCommand.Execute(null);

    viewModel.DiagnosticsSearchText = "unique-search-token";
    Assert.NotEmpty(viewModel.DiagnosticsRows);
    Assert.All(
      viewModel.DiagnosticsRows,
      row =>
        Assert.Contains("unique-search-token", row.MessageText, StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task DiagnosticsProfileFilter_ShowsOnlyMatchingProfileEvents()
  {
    MainWindowViewModel viewModel = CreateViewModel(
      (_, _) => Task.CompletedTask,
      (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)));

    MountProfile firstProfile = viewModel.SelectedProfile;
    viewModel.AddProfileCommand.Execute(null);
    MountProfile secondProfile = viewModel.SelectedProfile;

    viewModel.SelectedProfile = firstProfile;
    await viewModel.StartMountCommand.ExecuteAsync(null);

    viewModel.SelectedProfile = secondProfile;
    await viewModel.StartMountCommand.ExecuteAsync(null);

    viewModel.SelectedDiagnosticsProfileId = firstProfile.Id;
    Assert.NotEmpty(viewModel.DiagnosticsRows);
    Assert.All(viewModel.DiagnosticsRows, row => Assert.Equal(firstProfile.Id, row.ProfileId));
    Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);

    viewModel.SelectedProfile = secondProfile;
    Assert.All(viewModel.DiagnosticsRows, row => Assert.Equal(firstProfile.Id, row.ProfileId));

    viewModel.SelectedDiagnosticsProfileId = secondProfile.Id;
    Assert.NotEmpty(viewModel.DiagnosticsRows);
    Assert.All(viewModel.DiagnosticsRows, row => Assert.Equal(secondProfile.Id, row.ProfileId));
    Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);
  }

  [Fact]
  public async Task StartupTimelineOnly_IncludesStartupEventsAndExcludesManualAndRefreshEvents()
  {
    MainWindowViewModel viewModel = CreateViewModel(
      runtimeStateVerifier: (_, _) =>
        Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)),
      runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

    MountProfile startupProfile = viewModel.SelectedProfile;
    startupProfile.StartAtLogin = true;
    viewModel.InitializeRuntimeMonitoring();

    await WaitUntilAsync(() => viewModel.Logs.Any(entry => entry.Contains(
                                                    "Startup verification:",
                                                    StringComparison.OrdinalIgnoreCase)));
    await viewModel.StartMountCommand.ExecuteAsync(null);

    viewModel.StartupTimelineOnly = true;

    Assert.NotEmpty(viewModel.Logs);
    Assert.Contains(
      viewModel.DiagnosticsRows,
      row => row.MessageText.Contains("Startup monitor initialization started.", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(
      viewModel.DiagnosticsRows,
      row => row.MessageText.Contains("Startup verification:", StringComparison.OrdinalIgnoreCase));
    Assert.All(
      viewModel.DiagnosticsRows,
      row => Assert.StartsWith("startup/", row.StageText, StringComparison.OrdinalIgnoreCase));
    Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);
    Assert.DoesNotContain(
      viewModel.DiagnosticsRows,
      row => row.StageText.StartsWith("manualstart/", StringComparison.OrdinalIgnoreCase));
    Assert.DoesNotContain(
      viewModel.DiagnosticsRows,
      row => row.StageText.StartsWith("runtimerefresh/", StringComparison.OrdinalIgnoreCase));

    viewModel.StopRuntimeMonitoring();
  }

  [Fact]
  public async Task DiagnosticsTimeline_ProjectsEventsInChronologicalOrder()
  {
    MainWindowViewModel viewModel = CreateViewModel(
      (_, _) => Task.CompletedTask,
      (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)));

    MountProfile profile = viewModel.SelectedProfile;
    viewModel.SelectedDiagnosticsProfileId = profile.Id;
    await viewModel.StartMountCommand.ExecuteAsync(null);

    Assert.NotEmpty(viewModel.DiagnosticsRows);
    Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);
    Assert.All(viewModel.DiagnosticsRows, row => Assert.False(string.IsNullOrWhiteSpace(row.SeverityText)));
    Assert.All(viewModel.DiagnosticsRows, row => Assert.False(string.IsNullOrWhiteSpace(row.StageText)));
    Assert.All(viewModel.DiagnosticsRows, row => Assert.False(string.IsNullOrWhiteSpace(row.MessageText)));

    int initializationIndex = FindLogIndex(viewModel.Logs, "Profiles file");
    int startedIndex = FindLogIndex(viewModel.Logs, "Starting mount");
    int statusIndex = FindLogIndex(viewModel.Logs, "Status:");

    Assert.True(initializationIndex < startedIndex, "Initialization event should appear before execution event.");
    Assert.True(startedIndex < statusIndex, "Execution event should appear before completion event.");
  }

  [Fact]
  public async Task ChangingDiagnosticsFilters_RecomputesTimelineDeterministically()
  {
    MainWindowViewModel viewModel = CreateViewModel(
      (_, _) => Task.CompletedTask,
      (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)),
      (_, _) => Task.FromResult(false));

    MountProfile firstProfile = viewModel.SelectedProfile;
    firstProfile.StartAtLogin = true;

    viewModel.AddProfileCommand.Execute(null);
    MountProfile secondProfile = viewModel.SelectedProfile;
    secondProfile.StartAtLogin = true;

    viewModel.InitializeRuntimeMonitoring();
    await WaitUntilAsync(() =>
                           firstProfile.RuntimeState.Health is MountHealthState.Healthy &&
                           secondProfile.RuntimeState.Health is MountHealthState.Healthy);

    viewModel.SelectedProfile = firstProfile;
    await viewModel.StartMountCommand.ExecuteAsync(null);

    viewModel.SelectedProfile = secondProfile;
    await viewModel.StartMountCommand.ExecuteAsync(null);

    viewModel.SelectedDiagnosticsProfileId = firstProfile.Id;
    viewModel.StartupTimelineOnly = false;
    List<string> firstProfileAllEvents = viewModel.Logs.ToList();

    viewModel.StartupTimelineOnly = true;
    Assert.All(
      viewModel.DiagnosticsRows,
      row => Assert.StartsWith("startup/", row.StageText, StringComparison.OrdinalIgnoreCase));
    Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);

    viewModel.SelectedDiagnosticsProfileId = secondProfile.Id;
    Assert.All(viewModel.DiagnosticsRows, row => Assert.Equal(secondProfile.Id, row.ProfileId));

    viewModel.SelectedDiagnosticsProfileId = firstProfile.Id;
    viewModel.StartupTimelineOnly = false;
    Assert.Equal(firstProfileAllEvents, viewModel.Logs);

    viewModel.StopRuntimeMonitoring();
  }

  private MainWindowViewModel CreateViewModel(
    Func<MountProfile, CancellationToken, Task>? mountStartRunner = null,
    Func<MountProfile, CancellationToken, Task<ProfileRuntimeState>>? runtimeStateVerifier = null,
    Func<TimeSpan, CancellationToken, Task<bool>>? runtimeRefreshWaiter = null)
  {
    async Task<IReadOnlyList<ProfileRuntimeState>> RuntimeStateBatchVerifier(
      IEnumerable<MountProfile> profiles,
      CancellationToken cancellationToken)
    {
      List<ProfileRuntimeState> states = new();
      foreach (MountProfile profile in profiles)
      {
        states.Add(
          await (runtimeStateVerifier?.Invoke(profile, cancellationToken)
                 ?? Task.FromResult(CreateState(MountLifecycleState.Idle, MountHealthState.Unknown))));
      }

      return states;
    }

    MainWindowViewModel viewModel = new(
      CreateProfilesPath(),
      mountStartRunner: mountStartRunner,
      runtimeStateVerifier: runtimeStateVerifier,
      startupEnabledProbe: _ => false,
      runtimeRefreshWaiter: runtimeRefreshWaiter,
      runtimeStateBatchVerifier: RuntimeStateBatchVerifier,
      loadStartupData: false,
      logger: TestLogger.CreateMainWindowViewModelLogger());

    _viewModels.Add(viewModel);
    return viewModel;
  }

  private string CreateProfilesPath()
  {
    Directory.CreateDirectory(_tempRoot);
    return Path.Combine(_tempRoot, "profiles.json");
  }

  private static ProfileRuntimeState CreateState(
    MountLifecycleState lifecycle,
    MountHealthState health,
    string? errorText = null)
  {
    return new ProfileRuntimeState(lifecycle, health, DateTimeOffset.UtcNow, errorText);
  }

  private static int FindLogIndex(IEnumerable<string> logs, string fragment)
  {
    int index = 0;
    foreach (string log in logs)
    {
      if (log.Contains(fragment, StringComparison.OrdinalIgnoreCase))
      {
        return index;
      }

      index++;
    }

    throw new Xunit.Sdk.XunitException($"Expected to find log containing '{fragment}'.");
  }

  private static async Task WaitUntilAsync(Func<bool> condition)
  {
    for (int attempt = 0; attempt < 100; attempt++)
    {
      if (condition())
      {
        return;
      }

      await Task.Delay(10);
    }

    throw new TimeoutException("Timed out waiting for diagnostics expectation.");
  }

  private static void AssertTimestampPresent(MainWindowViewModel.DiagnosticsTimelineRow row)
  {
    Assert.True(
      DateTime.TryParseExact(
        row.TimestampText,
        "yyyy-MM-dd HH:mm:ss",
        null,
        System.Globalization.DateTimeStyles.None,
        out _),
      $"Expected timestamp in yyyy-MM-dd HH:mm:ss format but got '{row.TimestampText}'.");
  }
}