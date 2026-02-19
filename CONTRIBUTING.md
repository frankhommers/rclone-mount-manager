# Contributing

Thanks for your interest in contributing to Rclone Mount Manager.

## Development Setup

1. Install prerequisites:
   - .NET 10 SDK
   - rclone
   - Git
2. Clone and build:

```bash
git clone <your-fork-url>
cd rclone-mount
dotnet restore RcloneMountManager.slnx
dotnet build RcloneMountManager.slnx
```

3. Run tests:

```bash
dotnet test RcloneMountManager.slnx
```

## Branches and Pull Requests

- Create a feature branch from `main`.
- Keep changes focused and atomic.
- Include a clear PR description.

## Coding Guidelines

- Follow existing code style and naming conventions.
- Prefer small, readable methods over deeply nested logic.
- Add tests for behavioral changes when practical.
- Use CommunityToolkit.Mvvm attributes (`[ObservableProperty]`, `[RelayCommand]`).

## Commit Messages

Use concise, imperative messages. Examples:

- `fix: resolve mount path resolution on Linux`
- `feat: add typed Duration controls for VFS options`
- `refactor: split MainWindowViewModel into focused ViewModels`

## Reporting Bugs

When filing an issue, include:

- OS and version
- rclone version (`rclone version`)
- Steps to reproduce
- Expected vs actual behavior
- Any error messages or logs
