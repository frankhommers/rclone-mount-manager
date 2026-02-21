using System;
using System.Collections.Generic;
using System.Linq;

namespace RcloneMountManager.Core.Models;

public sealed class StartupPreflightReport
{
    private readonly List<StartupCheckResult> _checks = [];

    public IReadOnlyList<StartupCheckResult> Checks => _checks;

    public bool CriticalChecksPassed => _checks.All(check => !check.IsCriticalFailure);

    public bool HasWarnings => _checks.Any(check => check.IsWarningFailure);

    public StartupPreflightReport Add(StartupCheckResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _checks.Add(check);
        return this;
    }

    public StartupPreflightReport AddPass(string checkKey, string message) => Add(StartupCheckResult.Pass(checkKey, message));

    public StartupPreflightReport AddWarning(string checkKey, string message) => Add(StartupCheckResult.Warning(checkKey, message));

    public StartupPreflightReport AddCritical(string checkKey, string message) => Add(StartupCheckResult.Critical(checkKey, message));

    public string ToSummaryText()
    {
        if (_checks.Count == 0)
        {
            return "No startup checks were run.";
        }

        var passed = _checks.Count(check => check.IsPass);
        var warnings = _checks.Count(check => check.IsWarningFailure);
        var critical = _checks.Count(check => check.IsCriticalFailure);
        return $"Startup preflight: {passed} pass, {warnings} warning, {critical} critical.";
    }

    public string ToUserFacingMessage()
    {
        if (_checks.Count == 0)
        {
            return "No startup checks were run.";
        }

        return string.Join(Environment.NewLine, _checks.Select(check => $"[{check.CheckKey}] {check.Message}"));
    }
}
