# Technology Stack

**Analysis Date:** 2026-02-21

## Languages

**Primary:**
- C# / .NET 10 - application code in `RcloneMountManager.Core/`, `RcloneMountManager.GUI/`, and `RcloneMountManager.Tests/` (`*.csproj` targets `net10.0`).

**Secondary:**
- Bash - packaging and release scripts in `scripts/build-macos-app.sh` and `scripts/create-dmg.sh`.
- YAML - CI/CD workflow in `.github/workflows/release-dmg.yml`.

## Runtime

**Environment:**
- .NET runtime for desktop app execution (`RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`).
- Native OS tooling required at runtime for mount management: `rclone`, `mount`, `umount`/`fusermount`, and macOS `launchctl` (`RcloneMountManager.Core/Services/MountManagerService.cs`, `RcloneMountManager.Core/Services/LaunchAgentService.cs`).

**Package Manager:**
- NuGet via .NET CLI (`dotnet restore/build/test` in `README.md`).
- Lockfile: missing (`**/packages.lock.json` not detected).

## Frameworks

**Core:**
- .NET SDK-style projects (`RcloneMountManager.Core/RcloneMountManager.Core.csproj`, `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`).
- Avalonia UI 11.3.12 for desktop UI (`RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`).
- CommunityToolkit.Mvvm 8.4.0 for MVVM patterns (`RcloneMountManager.Core/RcloneMountManager.Core.csproj`, `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`).

**Testing:**
- xUnit 2.9.3 + `Microsoft.NET.Test.Sdk` 17.14.1 + Coverlet collector 6.0.4 (`RcloneMountManager.Tests/RcloneMountManager.Tests.csproj`).

**Build/Dev:**
- .NET CLI for build/test (`README.md`, `.github/workflows/release-dmg.yml`).
- GitHub Actions for release packaging (`.github/workflows/release-dmg.yml`).

## Key Dependencies

**Critical:**
- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` 11.3.12 - cross-platform desktop UI layer (`RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`).
- `CliWrap` 3.10.0 - all external process execution for rclone/mount/launchctl (`RcloneMountManager.Core/Services/*.cs`).
- `CommunityToolkit.Mvvm` 8.4.0 - observable properties and commands in ViewModels (`RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`).

**Infrastructure:**
- `Serilog` 4.2.0 + `Serilog.Sinks.Console` 6.0.0 + `Serilog.Sinks.File` 6.0.0 - app logging (`RcloneMountManager.GUI/Program.cs`, `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`).

## Configuration

**Environment:**
- No `.env` or appsettings-based configuration detected.
- Versioning configured centrally in `Directory.Build.props`.
- User runtime configuration persisted to JSON at app data path (`RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`).

**Build:**
- Solution entry: `RcloneMountManager.slnx`.
- Project config: `RcloneMountManager.Core/RcloneMountManager.Core.csproj`, `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`, `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj`.
- Release workflow config: `.github/workflows/release-dmg.yml`.

## Platform Requirements

**Development:**
- .NET 10 SDK (`README.md`, `.github/workflows/release-dmg.yml`).
- `rclone` installed for runtime-dependent features and local functional use (`README.md`, `RcloneMountManager.Core/Services/RcloneOptionsService.cs`).

**Production:**
- Desktop app runtime target is macOS app bundle + DMG artifacts (`scripts/build-macos-app.sh`, `scripts/create-dmg.sh`, `.github/workflows/release-dmg.yml`).
- Core mount functionality assumes host OS mount tooling and rclone binary (`RcloneMountManager.Core/Services/MountManagerService.cs`).

---

*Stack analysis: 2026-02-21*
