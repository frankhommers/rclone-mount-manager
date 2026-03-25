using System.Diagnostics;

namespace RcloneMountManager.Core.Services;

public static class ShellEnvironmentService
{
  private static readonly string[] EnvironmentVariables = ["PATH", "SSH_AUTH_SOCK"];

  public static async Task ResolveAsync()
  {
    if (OperatingSystem.IsWindows())
    {
      return;
    }

    string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";
    string printScript = string.Join(
      "; ",
      EnvironmentVariables.Select(v => "echo \"" + v + "=$" + v + "\""));

    try
    {
      ProcessStartInfo psi = new()
      {
        FileName = shell,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      psi.ArgumentList.Add("-li");
      psi.ArgumentList.Add("-c");
      psi.ArgumentList.Add(printScript);

      using Process? process = Process.Start(psi);
      if (process is null)
      {
        return;
      }

      string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
      await process.WaitForExitAsync().ConfigureAwait(false);

      if (process.ExitCode != 0)
      {
        return;
      }

      foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      {
        int separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
          continue;
        }

        string name = line[..separatorIndex];
        string value = line[(separatorIndex + 1)..];

        if (!string.IsNullOrEmpty(value) && EnvironmentVariables.Contains(name))
        {
          Environment.SetEnvironmentVariable(name, value);
        }
      }
    }
    catch
    {
      // Fall back to inherited environment silently
    }
  }
}
