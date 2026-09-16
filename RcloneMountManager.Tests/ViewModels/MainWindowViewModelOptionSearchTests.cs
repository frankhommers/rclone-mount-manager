using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelOptionSearchTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"option-search-tests-{Guid.NewGuid():N}");
  private readonly MainWindowViewModel _viewModel;

  public MainWindowViewModelOptionSearchTests()
  {
    _viewModel = new MainWindowViewModel(
      profilesFilePath: Path.Combine(_tempRoot, "profiles.json"),
      startupEnabledProbe: _ => false,
      sourceRemoteExistsInRcloneConfigRunner: (_, _, _) => Task.FromResult(true),
      loadStartupData: false);
    _viewModel.SelectedBackend = new RcloneBackendInfo
    {
      Name = "sftp",
      Options =
      [
        new RcloneBackendOption { Name = "host", Required = true, Help = "Server address" },
        new RcloneBackendOption { Name = "key_file", Help = "Path to SSH private key" },
        new RcloneBackendOption { Name = "key_use_agent", Advanced = true, Help = "Force the SSH agent" },
        new RcloneBackendOption { Name = "pass", IsPassword = true, Help = "Password" },
      ],
    };
  }

  public void Dispose()
  {
    _viewModel.Dispose();
    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Theory]
  [InlineData("key_file")]
  [InlineData("KEY-FILE")]
  [InlineData("  key file  ")]
  [InlineData("--key-file")]
  [InlineData("private key")]
  public void Search_FindsOptionByNameOrDescription(string search)
  {
    _viewModel.BackendOptionSearchText = search;

    Assert.Equal("key_file", Assert.Single(_viewModel.VisibleBackendOptionInputs).Name);
    Assert.Empty(_viewModel.VisibleAdvancedBackendOptionInputs);
    Assert.False(_viewModel.HasNoBackendOptionMatches);
  }

  [Fact]
  public void Search_RevealsAdvancedOptionsAndRestoresCollapsedState()
  {
    Assert.False(_viewModel.AreAdvancedBackendOptionsExpanded);

    _viewModel.BackendOptionSearchText = "agent";

    Assert.Empty(_viewModel.VisibleBackendOptionInputs);
    Assert.Equal("key_use_agent", Assert.Single(_viewModel.VisibleAdvancedBackendOptionInputs).Name);
    Assert.True(_viewModel.AreAdvancedBackendOptionsExpanded);
    Assert.False(_viewModel.ShowAdvancedBackendOptions);
    Assert.False(_viewModel.HasNoBackendOptionMatches);

    _viewModel.ClearBackendOptionSearchCommand.Execute(null);

    Assert.False(_viewModel.AreAdvancedBackendOptionsExpanded);
    Assert.False(_viewModel.HasBackendOptionSearch);
    Assert.Equal(3, _viewModel.VisibleBackendOptionInputs.Count());
  }

  [Fact]
  public void SearchAndAdvancedToggle_PreserveUnsavedValuesAndInputState()
  {
    RcloneBackendOptionInput key = _viewModel.BackendOptionInputs.Single(option => option.Name == "key_file");
    RcloneBackendOptionInput agent = Assert.Single(_viewModel.AdvancedBackendOptionInputs);
    RcloneBackendOptionInput password = _viewModel.BackendOptionInputs.Single(option => option.Name == "pass");
    key.Value = "/tmp/test-key";
    key.IsPinned = true;
    agent.Value = "true";
    password.Value = "test-password";
    password.ConfirmValue = "test-password";

    _viewModel.BackendOptionSearchText = "agent";
    _viewModel.ShowAdvancedBackendOptions = true;
    _viewModel.ShowAdvancedBackendOptions = false;
    _viewModel.ClearBackendOptionSearchCommand.Execute(null);

    Assert.Same(key, _viewModel.VisibleBackendOptionInputs.Single(option => option.Name == "key_file"));
    Assert.Equal("/tmp/test-key", key.Value);
    Assert.True(key.IsPinned);
    Assert.Same(agent, Assert.Single(_viewModel.AdvancedBackendOptionInputs));
    Assert.Equal("true", agent.Value);
    Assert.Equal("test-password", password.ConfirmValue);
    Assert.False(password.HasSecretMismatch);
  }

  [Fact]
  public void Search_DoesNotMatchSecretValues()
  {
    _viewModel.BackendOptionInputs.Single(option => option.Name == "pass").Value = "secret-needle";

    _viewModel.BackendOptionSearchText = "secret-needle";

    Assert.True(_viewModel.HasNoBackendOptionMatches);
    Assert.Empty(_viewModel.VisibleBackendOptionInputs);
    Assert.Empty(_viewModel.VisibleAdvancedBackendOptionInputs);
  }

  [Fact]
  public void Search_WhitespaceRestoresAllOptions()
  {
    _viewModel.BackendOptionSearchText = "missing-option";
    Assert.True(_viewModel.HasNoBackendOptionMatches);

    _viewModel.BackendOptionSearchText = " \t ";

    Assert.False(_viewModel.HasBackendOptionSearch);
    Assert.False(_viewModel.HasNoBackendOptionMatches);
    Assert.Equal(3, _viewModel.VisibleBackendOptionInputs.Count());
    Assert.Single(_viewModel.VisibleAdvancedBackendOptionInputs);
  }

  [Fact]
  public void SelectProfile_RestoresAdvancedValuesWhileCollapsed()
  {
    _viewModel.AvailableBackends.Add(_viewModel.SelectedBackend!);
    MountProfile profile = new()
    {
      Name = "test-sftp",
      IsRemoteDefinition = true,
      BackendName = "sftp",
      BackendOptions = new Dictionary<string, string> { ["key_use_agent"] = "true" },
    };
    _viewModel.Profiles.Add(profile);

    _viewModel.SelectedProfile = profile;

    Assert.False(_viewModel.ShowAdvancedBackendOptions);
    Assert.Equal("true", Assert.Single(_viewModel.AdvancedBackendOptionInputs).Value);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SaveRemote_IncludesOptionsHiddenBySearch(bool remoteExists)
  {
    if (OperatingSystem.IsWindows())
    {
      return;
    }

    Directory.CreateDirectory(_tempRoot);
    string binaryPath = Path.Combine(_tempRoot, "rclone-test");
    string configJson = remoteExists ? "{\"test-sftp\":{\"type\":\"sftp\"}}" : "{}";
    File.WriteAllText(binaryPath, $$"""
      #!/bin/sh
      if [ "$2" = "dump" ]; then
        echo '{{configJson}}'
      else
        printf '%s\n' "$@" > "$0.args"
      fi
      """);
    File.SetUnixFileMode(binaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    _viewModel.AvailableBackends.Add(_viewModel.SelectedBackend!);
    MountProfile profile = new()
    {
      Name = "test-sftp",
      IsRemoteDefinition = true,
      BackendName = "sftp",
      RcloneBinaryPath = binaryPath,
    };
    _viewModel.Profiles.Add(profile);
    _viewModel.SelectedProfile = profile;
    _viewModel.BackendOptionInputs.Single(option => option.Name == "host").Value = "example.invalid";
    Assert.Single(_viewModel.AdvancedBackendOptionInputs).Value = "true";
    _viewModel.BackendOptionSearchText = "key_file";

    await _viewModel.CreateRemoteCommand.ExecuteAsync(null);

    string[] arguments = File.ReadAllLines(binaryPath + ".args");
    Assert.Equal(remoteExists ? "update" : "create", arguments[1]);
    Assert.Equal("example.invalid", arguments[Array.IndexOf(arguments, "host") + 1]);
    Assert.Contains("key_use_agent", arguments);
    Assert.Equal("true", arguments[Array.IndexOf(arguments, "key_use_agent") + 1]);
    Assert.Equal("true", profile.BackendOptions["key_use_agent"]);
    Assert.Equal("Remote 'test-sftp' saved.", _viewModel.StatusText);
  }
}
