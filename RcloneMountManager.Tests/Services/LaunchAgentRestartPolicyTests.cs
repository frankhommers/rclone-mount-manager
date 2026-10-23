using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;

namespace RcloneMountManager.Tests.Services;

public sealed class LaunchAgentRestartPolicyTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"launch-agent-backoff-{Guid.NewGuid():N}");

  public LaunchAgentRestartPolicyTests()
  {
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Fact]
  public void BuildPlist_SetsThrottleIntervalAndLaunchAgentLog()
  {
    LaunchAgentService service = CreateService(new LaunchAgentRestartPolicy(45, 30, 900, 120));
    MountProfile profile = CreateProfile();

    XElement dict = XDocument.Parse(service.BuildPlist(profile, "/tmp/supervisor.sh")).Root!.Element("dict")!;

    Assert.Equal("45", ValueAfterKey(dict, "ThrottleInterval").Value);
    Assert.Equal("integer", ValueAfterKey(dict, "ThrottleInterval").Name.LocalName);
    Assert.Equal(service.GetLaunchAgentLogPath(profile), ValueAfterKey(dict, "StandardErrorPath").Value);
    Assert.Equal("true", ValueAfterKey(dict, "KeepAlive").Name.LocalName);
  }

  [Fact]
  public void BuildPlist_LaunchesSupervisorScript()
  {
    LaunchAgentService service = CreateService(LaunchAgentRestartPolicy.Default);
    MountProfile profile = CreateProfile();
    string supervisorPath = service.GetSupervisorScriptPath(profile);

    XElement dict = XDocument.Parse(service.BuildPlist(profile, supervisorPath)).Root!.Element("dict")!;
    string[] arguments = ValueAfterKey(dict, "ProgramArguments").Elements("string").Select(e => e.Value).ToArray();

    Assert.Equal("-lic", arguments[1]);
    Assert.Contains(supervisorPath, arguments[2], StringComparison.Ordinal);
  }

  [Fact]
  public async Task Supervisor_DoublesBackoffOnRepeatedFastFailures_AndCapsAtMaximum()
  {
    string mountScript = await WriteMountScriptAsync("exit 1");
    string stateFile = Path.Combine(_tempRoot, "state", "profile.backoff");
    LaunchAgentRestartPolicy policy = new(30, 1, 4, 60);

    (int exitCode, _) = await RunSupervisorAsync(mountScript, stateFile, policy);
    Assert.Equal(1, exitCode);
    Assert.Equal("1", (await File.ReadAllTextAsync(stateFile)).Trim());

    await RunSupervisorAsync(mountScript, stateFile, policy);
    Assert.Equal("2", (await File.ReadAllTextAsync(stateFile)).Trim());

    await File.WriteAllTextAsync(stateFile, "4\n");
    (_, string stderr) = await RunSupervisorAsync(mountScript, stateFile, policy);
    Assert.Equal("4", (await File.ReadAllTextAsync(stateFile)).Trim());
    Assert.Contains("retrying in 4s", stderr, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Supervisor_SleepsBackoffBeforeExiting()
  {
    string mountScript = await WriteMountScriptAsync("exit 3");
    string stateFile = Path.Combine(_tempRoot, "state", "profile.backoff");
    Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
    await File.WriteAllTextAsync(stateFile, "1\n");

    Stopwatch stopwatch = Stopwatch.StartNew();
    (int exitCode, _) = await RunSupervisorAsync(mountScript, stateFile, new LaunchAgentRestartPolicy(30, 1, 10, 60));
    stopwatch.Stop();

    Assert.Equal(3, exitCode);
    Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(1.8), $"Expected >= 2s backoff, got {stopwatch.Elapsed}.");
  }

  [Fact]
  public async Task Supervisor_ResetsBackoffAfterCleanExit()
  {
    string mountScript = await WriteMountScriptAsync("exit 0");
    string stateFile = Path.Combine(_tempRoot, "state", "profile.backoff");
    Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
    await File.WriteAllTextAsync(stateFile, "8\n");

    (int exitCode, _) = await RunSupervisorAsync(mountScript, stateFile, new LaunchAgentRestartPolicy(30, 1, 10, 60));

    Assert.Equal(0, exitCode);
    Assert.False(File.Exists(stateFile));
  }

  [Fact]
  public async Task Supervisor_ResetsBackoffAfterHealthyRun()
  {
    string mountScript = await WriteMountScriptAsync("sleep 1; exit 1");
    string stateFile = Path.Combine(_tempRoot, "state", "profile.backoff");
    Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
    await File.WriteAllTextAsync(stateFile, "8\n");

    (int exitCode, _) = await RunSupervisorAsync(mountScript, stateFile, new LaunchAgentRestartPolicy(30, 1, 10, 1));

    Assert.Equal(1, exitCode);
    Assert.False(File.Exists(stateFile));
  }

  [Fact]
  public async Task Supervisor_HandlesPathsWithQuotesAndSpaces()
  {
    string directory = Path.Combine(_tempRoot, "Frank's Mounts & More");
    Directory.CreateDirectory(directory);
    string mountScript = Path.Combine(directory, "mount it.sh");
    await File.WriteAllTextAsync(mountScript, "#!/bin/bash\nexit 0\n");
    string stateFile = Path.Combine(directory, "state dir", "p.backoff");

    (int exitCode, string stderr) = await RunSupervisorAsync(mountScript, stateFile, new LaunchAgentRestartPolicy(30, 1, 10, 60));

    Assert.True(exitCode == 0, stderr);
  }

  [Fact]
  public void ResetBackoff_RemovesStateFile()
  {
    LaunchAgentService service = CreateService(LaunchAgentRestartPolicy.Default);
    MountProfile profile = CreateProfile();
    string stateFile = service.GetBackoffStatePath(profile);
    Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
    File.WriteAllText(stateFile, "900\n");

    service.ResetBackoff(profile);

    Assert.False(File.Exists(stateFile));
  }

  private async Task<string> WriteMountScriptAsync(string body)
  {
    string path = Path.Combine(_tempRoot, $"mount-{Guid.NewGuid():N}.sh");
    await File.WriteAllTextAsync(path, $"#!/bin/bash\n{body}\n");
    return path;
  }

  private async Task<(int ExitCode, string StandardError)> RunSupervisorAsync(
    string mountScript,
    string stateFile,
    LaunchAgentRestartPolicy policy)
  {
    string supervisor = Path.Combine(_tempRoot, $"supervisor-{Guid.NewGuid():N}.sh");
    await File.WriteAllTextAsync(supervisor, LaunchAgentSupervisorScript.Build(mountScript, stateFile, policy));

    ProcessStartInfo startInfo = new("/bin/bash")
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    startInfo.ArgumentList.Add(supervisor);

    using Process process = Process.Start(startInfo)!;
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
    string stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
    await stdoutTask;
    await process.WaitForExitAsync(timeout.Token);
    return (process.ExitCode, stderr);
  }

  private LaunchAgentService CreateService(LaunchAgentRestartPolicy policy)
  {
    return new LaunchAgentService(
      NullLogger<LaunchAgentService>.Instance,
      appDataDirectory: Path.Combine(_tempRoot, "AppData"),
      userProfileDirectory: Path.Combine(_tempRoot, "User"),
      commandRunner: (_, _, _) => Task.FromResult(new LaunchAgentService.CommandExecutionResult(0, "", "")),
      uidProvider: () => 501,
      loginShellPath: "/bin/zsh",
      restartPolicy: policy);
  }

  private static XElement ValueAfterKey(XElement dict, string key)
  {
    XElement keyElement = dict.Elements("key").Single(k => k.Value == key);
    return keyElement.ElementsAfterSelf().First();
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
