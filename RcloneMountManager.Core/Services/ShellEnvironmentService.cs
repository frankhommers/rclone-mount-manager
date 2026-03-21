using System.Diagnostics;

namespace RcloneMountManager.Core.Services;

public static class ShellEnvironmentService
{
  public static async Task ResolveAsync()
  {
    if (OperatingSystem.IsWindows())
    {
      return;
    }

    string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";

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
      psi.ArgumentList.Add("echo $PATH");

      using Process? process = Process.Start(psi);
      if (process is null)
      {
        return;
      }

      string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
      await process.WaitForExitAsync().ConfigureAwait(false);

      if (process.ExitCode == 0)
      {
        string path = output.Trim();
        if (!string.IsNullOrEmpty(path))
        {
          Environment.SetEnvironmentVariable("PATH", path);
        }
      }
    }
    catch
    {
      // Fall back to inherited PATH silently
    }
  }
}
