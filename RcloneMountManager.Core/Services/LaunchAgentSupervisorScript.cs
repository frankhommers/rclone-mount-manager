using RcloneMountManager.Core.Models;
using System.Text;

namespace RcloneMountManager.Core.Services;

public static class LaunchAgentSupervisorScript
{
  public static string Build(string mountScriptPath, string stateFilePath, LaunchAgentRestartPolicy policy)
  {
    ArgumentNullException.ThrowIfNull(policy);

    StringBuilder builder = new();
    builder.AppendLine("#!/bin/bash");
    builder.AppendLine("set -u");
    builder.AppendLine();
    builder.AppendLine($"MOUNT_SCRIPT={QuoteForBash(mountScriptPath)}");
    builder.AppendLine($"STATE_FILE={QuoteForBash(stateFilePath)}");
    builder.AppendLine($"INITIAL_BACKOFF={policy.InitialBackoffSeconds}");
    builder.AppendLine($"MAX_BACKOFF={policy.MaxBackoffSeconds}");
    builder.AppendLine($"HEALTHY_RUN={policy.HealthyRunSeconds}");
    builder.AppendLine();
    builder.AppendLine("child=0");
    builder.AppendLine(
      "trap 'if [ \"$child\" -gt 0 ]; then kill -TERM \"$child\" 2>/dev/null; wait \"$child\"; fi; exit 0' TERM INT");
    builder.AppendLine();
    builder.AppendLine("started=$SECONDS");
    builder.AppendLine("/bin/bash \"$MOUNT_SCRIPT\" &");
    builder.AppendLine("child=$!");
    builder.AppendLine("wait \"$child\"");
    builder.AppendLine("status=$?");
    builder.AppendLine("child=0");
    builder.AppendLine("runtime=$((SECONDS - started))");
    builder.AppendLine();
    builder.AppendLine("if [ \"$status\" -eq 0 ] || [ \"$runtime\" -ge \"$HEALTHY_RUN\" ]; then");
    builder.AppendLine("  rm -f \"$STATE_FILE\"");
    builder.AppendLine("  exit \"$status\"");
    builder.AppendLine("fi");
    builder.AppendLine();
    builder.AppendLine("backoff=$INITIAL_BACKOFF");
    builder.AppendLine("previous=$(cat \"$STATE_FILE\" 2>/dev/null || true)");
    builder.AppendLine("case \"$previous\" in");
    builder.AppendLine("  ''|*[!0-9]*) ;;");
    builder.AppendLine("  *) backoff=$((previous * 2)) ;;");
    builder.AppendLine("esac");
    builder.AppendLine("if [ \"$backoff\" -gt \"$MAX_BACKOFF\" ]; then");
    builder.AppendLine("  backoff=$MAX_BACKOFF");
    builder.AppendLine("fi");
    builder.AppendLine("mkdir -p \"$(dirname \"$STATE_FILE\")\"");
    builder.AppendLine("printf '%s\\n' \"$backoff\" > \"$STATE_FILE\"");
    builder.AppendLine(
      "echo \"$(date '+%Y-%m-%dT%H:%M:%S%z') mount exited with status $status after ${runtime}s; retrying in ${backoff}s\" >&2");
    builder.AppendLine();
    builder.AppendLine("sleep \"$backoff\" &");
    builder.AppendLine("child=$!");
    builder.AppendLine("wait \"$child\"");
    builder.AppendLine("child=0");
    builder.AppendLine("exit \"$status\"");
    return builder.ToString();
  }

  private static string QuoteForBash(string value) => $"'{value.Replace("'", "'\\''")}'";
}
