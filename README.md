# Rclone Mount Manager

A cross-platform desktop application for managing filesystem mounts via [rclone](https://rclone.org/) and NFS.

## Features

- **Multiple mount profiles** - Create and manage different mount configurations
- **Typed mount parameters** - All rclone mount, VFS, NFS, filter, and general options with appropriate UI controls (toggles, dropdowns, text fields) auto-discovered from rclone
- **Quick Connect** - Mount WebDAV, SFTP, FTP, and FTPS endpoints without pre-configuring rclone remotes
- **Backend builder** - Discover and configure any of rclone's 60+ backend types directly from the app
- **Script generation** - Generate and save bash mount scripts for any profile
- **Start at login** - Enable automatic mounting via macOS LaunchAgents
- **Dark/Light theme** - Follow system theme or choose manually

## Tech Stack

- **UI:** [Avalonia UI](https://avaloniaui.net/) 11.3 with Fluent theme
- **Language:** C# / .NET 10
- **MVVM:** CommunityToolkit.Mvvm
- **Process execution:** CliWrap
- **Logging:** Serilog

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [rclone](https://rclone.org/install/) (for runtime functionality)

### Build

```bash
git clone https://github.com/frankhommers/rclone-mount.git
cd rclone-mount
dotnet restore RcloneMountManager.slnx
dotnet build RcloneMountManager.slnx
```

### Run

```bash
dotnet run --project RcloneMountManager.GUI/RcloneMountManager.GUI.csproj
```

### Test

```bash
dotnet test RcloneMountManager.slnx
```

### macOS App Bundle

```bash
bash scripts/build-macos-app.sh --output dist
open "dist/Rclone Mount Manager.app"
```

## Project Structure

```
RcloneMountManager.Core/     # Shared models and services
RcloneMountManager.GUI/      # Avalonia UI application
RcloneMountManager.Tests/    # Unit tests
scripts/                     # Build and distribution scripts
docs/                        # Design documents and plans
```

## How It Works

Mount parameters are discovered at runtime via `rclone rc --loopback options/info`, which returns structured JSON with all available options including types, defaults, and descriptions. The app renders appropriate UI controls for each option type (toggles for booleans, dropdowns for enums, text fields for strings/durations/sizes).

Only parameters that differ from the default are stored and included in mount commands, keeping profiles clean and portable.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## License

[MIT](LICENSE)
