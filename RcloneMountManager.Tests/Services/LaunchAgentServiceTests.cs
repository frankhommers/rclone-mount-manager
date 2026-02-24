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
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EnableAsync_WiresPlutilAndBootstrapCommandsInGuiDomain()
    {
        var calls = new List<(string Command, string[] Args)>();
        var service = CreateService(calls);
        var profile = CreateProfile();

        await service.EnableAsync(profile, "#!/bin/bash\nexit 0\n", _ => { }, CancellationToken.None);

        var plistPath = service.GetLaunchAgentPlistPath(profile);
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
        var calls = new List<(string Command, string[] Args)>();
        var service = CreateService(calls);
        var profile = CreateProfile();
        var plistPath = service.GetLaunchAgentPlistPath(profile);

        Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
        await File.WriteAllTextAsync(plistPath, "plist");

        await service.DisableAsync(profile, _ => { }, CancellationToken.None);

        Assert.Single(calls);
        Assert.Equal("launchctl", calls[0].Command);
        Assert.Equal(["bootout", "gui/501/com.rclonemountmanager.profile.profile-123"], calls[0].Args);
        Assert.False(File.Exists(plistPath));
    }

    [Fact]
    public async Task EnableAsync_ThrowsExplicitContextWhenPlutilFails()
    {
        var service = CreateServiceForFailure("plutil", 1, "plist is malformed", "lint error");
        var profile = CreateProfile();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnableAsync(profile, "#!/bin/bash\nexit 0\n", _ => { }, CancellationToken.None));

        Assert.Contains("plutil -lint", ex.Message);
        Assert.Contains("exit code 1", ex.Message);
        Assert.Contains("stdout: plist is malformed", ex.Message);
        Assert.Contains("stderr: lint error", ex.Message);
    }

    [Fact]
    public async Task DisableAsync_ToleratesBootoutFailure_StillDeletesPlist()
    {
        var logMessages = new List<string>();
        var service = CreateServiceForFailure("launchctl", 3, "", "No such process");
        var profile = CreateProfile();
        var plistPath = service.GetLaunchAgentPlistPath(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
        await File.WriteAllTextAsync(plistPath, "plist");

        await service.DisableAsync(profile, logMessages.Add, CancellationToken.None);

        Assert.False(File.Exists(plistPath));
        Assert.Contains(logMessages, m => m.Contains("exited with code 3"));
    }

    [Fact]
    public async Task EnableAsync_GeneratesPlistWithConsistentLabelAndPath()
    {
        var calls = new List<(string Command, string[] Args)>();
        var service = CreateService(calls);
        var profile = CreateProfile();
        var expectedLabel = "com.rclonemountmanager.profile.profile-123";

        await service.EnableAsync(profile, "#!/bin/bash\nexit 0\n", _ => { }, CancellationToken.None);

        var plistPath = service.GetLaunchAgentPlistPath(profile);
        var plistContent = await File.ReadAllTextAsync(plistPath);

        Assert.EndsWith($"{expectedLabel}.plist", plistPath, StringComparison.Ordinal);
        Assert.Contains($"<string>{expectedLabel}</string>", plistContent, StringComparison.Ordinal);
    }

    private LaunchAgentService CreateService(List<(string Command, string[] Args)> calls)
    {
        var appData = Path.Combine(_tempRoot, "AppData");
        var userProfile = Path.Combine(_tempRoot, "User");

        return new LaunchAgentService(
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
        var appData = Path.Combine(_tempRoot, "AppData");
        var userProfile = Path.Combine(_tempRoot, "User");

        return new LaunchAgentService(
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
