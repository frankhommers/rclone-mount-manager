using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;

namespace RcloneMountManager.Tests.Services;

public sealed class LaunchAgentServiceTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"launch-agent-tests-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Fact]
  public async Task EnableAsync_WiresPlutilAndBootstrapCommandsInGuiDomain()
  {
    List<(string Command, string[] Args)> calls = new();
    LaunchAgentService service = CreateService(calls);
    MountProfile profile = CreateProfile();

    await service.EnableAsync(profile, "#!/bin/bash\nexit 0\n", CancellationToken.None);

    string plistPath = service.GetLaunchAgentPlistPath(profile);
    Assert.Equal(3, calls.Count);
    Assert.Equal("plutil", calls[0].Command);
    Assert.Equal(["-lint", plistPath], calls[0].Args);
    Assert.Equal("launchctl", calls[1].Command);
    Assert.Equal(["bootout", "gui/501/com.rclonemountmanager.profile.profile-123"], calls[1].Args);
    Assert.Equal("launchctl", calls[2].Command);
    Assert.Equal(["bootstrap", "gui/501", plistPath], calls[2].Args);
  }

  [Fact]
  public async Task DisableAsync_WiresBootoutCommandWithStableServiceTarget()
  {
    List<(string Command, string[] Args)> calls = new();
    LaunchAgentService service = CreateService(calls);
    MountProfile profile = CreateProfile();
    string plistPath = service.GetLaunchAgentPlistPath(profile);

    Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
    await File.WriteAllTextAsync(plistPath, "plist");

    await service.DisableAsync(profile, CancellationToken.None);

    Assert.Single(calls);
    Assert.Equal("launchctl", calls[0].Command);
    Assert.Equal(["bootout", "gui/501/com.rclonemountmanager.profile.profile-123"], calls[0].Args);
    Assert.False(File.Exists(plistPath));
  }

  [Fact]
  public async Task EnableAsync_ThrowsExplicitContextWhenPlutilFails()
  {
    LaunchAgentService service = CreateServiceForFailure("plutil", 1, "plist is malformed", "lint error");
    MountProfile profile = CreateProfile();

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        service.EnableAsync(profile, "#!/bin/bash\nexit 0\n", CancellationToken.None));

    Assert.Contains("plutil -lint", ex.Message);
    Assert.Contains("exit code 1", ex.Message);
    Assert.Contains("stdout: plist is malformed", ex.Message);
    Assert.Contains("stderr: lint error", ex.Message);
  }

  [Fact]
  public async Task DisableAsync_ToleratesBootoutFailure_StillDeletesPlist()
  {
    LaunchAgentService service = CreateServiceForFailure("launchctl", 3, "", "No such process");
    MountProfile profile = CreateProfile();
    string plistPath = service.GetLaunchAgentPlistPath(profile);
    Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
    await File.WriteAllTextAsync(plistPath, "plist");

    await service.DisableAsync(profile, CancellationToken.None);

    Assert.False(File.Exists(plistPath));
  }

  [Fact]
  public async Task EnableAsync_GeneratesPlistWithConsistentLabelAndPath()
  {
    List<(string Command, string[] Args)> calls = new();
    LaunchAgentService service = CreateService(calls);
    MountProfile profile = CreateProfile();
    string expectedLabel = "com.rclonemountmanager.profile.profile-123";

    await service.EnableAsync(profile, "#!/bin/bash\nexit 0\n", CancellationToken.None);

    string plistPath = service.GetLaunchAgentPlistPath(profile);
    string plistContent = await File.ReadAllTextAsync(plistPath);

    Assert.EndsWith($"{expectedLabel}.plist", plistPath, StringComparison.Ordinal);
    Assert.Contains($"<string>{expectedLabel}</string>", plistContent, StringComparison.Ordinal);
  }

  [Fact]
  public async Task EnableAsync_LoadsAgentFromLoginShellAndPreservesQuotedScriptPath()
  {
    if (!OperatingSystem.IsMacOS())
    {
      return;
    }

    Directory.CreateDirectory(_tempRoot);
    string shellPath = Path.Combine(_tempRoot, "test-login-shell");
    await File.WriteAllTextAsync(shellPath, """
      #!/bin/sh
      [ "$1" = "-lic" ] || exit 64
      export SSH_AUTH_SOCK=/tmp/test-ssh-agent.sock
      exec /bin/sh -c "$2"
      """);
    File.SetUnixFileMode(shellPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    LaunchAgentService service = new(
      NullLogger<LaunchAgentService>.Instance,
      appDataDirectory: Path.Combine(_tempRoot, "User's App Data & Mounts"),
      userProfileDirectory: Path.Combine(_tempRoot, "User"),
      commandRunner: (_, _, _) => Task.FromResult(new LaunchAgentService.CommandExecutionResult(0, "", "")),
      uidProvider: () => 501,
      loginShellPath: shellPath);
    MountProfile profile = CreateProfile();

    await service.EnableAsync(profile, "#!/bin/bash\nprintf '%s' \"$SSH_AUTH_SOCK\"\n", CancellationToken.None);

    XDocument plist = XDocument.Load(service.GetLaunchAgentPlistPath(profile));
    XElement argumentsElement = (XElement)plist.Root!.Element("dict")!.Elements("key")
      .Single(key => key.Value == "ProgramArguments").NextNode!;
    string[] arguments = argumentsElement.Elements("string").Select(value => value.Value).ToArray();
    ProcessStartInfo startInfo = new(arguments[0])
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    startInfo.Environment["SSH_AUTH_SOCK"] = "/tmp/wrong-inherited-agent.sock";
    foreach (string argument in arguments.Skip(1))
    {
      startInfo.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(startInfo)!;
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
    string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
    string error = await process.StandardError.ReadToEndAsync(timeout.Token);
    await process.WaitForExitAsync(timeout.Token);

    Assert.True(process.ExitCode == 0, error);
    Assert.Equal("/tmp/test-ssh-agent.sock", output);
  }

  private LaunchAgentService CreateService(List<(string Command, string[] Args)> calls)
  {
    string appData = Path.Combine(_tempRoot, "AppData");
    string userProfile = Path.Combine(_tempRoot, "User");

    return new LaunchAgentService(
      NullLogger<LaunchAgentService>.Instance,
      appData,
      userProfile,
      (command, args, _) =>
      {
        calls.Add((command, args));
        return Task.FromResult(new LaunchAgentService.CommandExecutionResult(0, string.Empty, string.Empty));
      },
      () => 501);
  }

  private LaunchAgentService CreateServiceForFailure(string failingCommand, int exitCode, string stdout, string stderr)
  {
    string appData = Path.Combine(_tempRoot, "AppData");
    string userProfile = Path.Combine(_tempRoot, "User");

    return new LaunchAgentService(
      NullLogger<LaunchAgentService>.Instance,
      appData,
      userProfile,
      (command, _, _) =>
      {
        if (string.Equals(command, failingCommand, StringComparison.Ordinal))
        {
          return Task.FromResult(new LaunchAgentService.CommandExecutionResult(exitCode, stdout, stderr));
        }

        return Task.FromResult(new LaunchAgentService.CommandExecutionResult(0, string.Empty, string.Empty));
      },
      () => 501);
  }

  private static MountProfile CreateProfile()
  {
    return new MountProfile
    {
      Id = "profile-123",
      Name = "Sample Profile",
    };
  }
}
