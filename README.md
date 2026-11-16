# Desktop Guard

Desktop Guard (DG) is a Windows 11 system-tray app that watches desktop shortcuts and lets the signed-in user review them without blocking installers.

## Product overview

DG monitors `.lnk` and `.url` shortcuts on the signed-in user's Desktop and Public Desktop. The first complete scan establishes each baseline; new shortcuts remain pending until the user reviews them. Desktop file operations are explicit and recovery-aware.

For end-user installation, usage, update, relocation, and removal instructions, see [README.portable.md](README.portable.md). The portable ZIP includes that guide as `README.md`.

## Project structure

- `src/DesktopGuard.Core` contains platform-independent inventory policy, state reconciliation, and shortcut status transitions.
- `src/DesktopGuard.App` contains WinForms presentation, tray and notification integration, filesystem monitoring, startup registration, file operations, and local persistence.
- `tests/DesktopGuard.Tests` contains Core policy and state tests.
- `tests/DesktopGuard.App.Tests` contains scanner, persistence, notification-activation, monitor, and temporary-directory file-operation tests.
- `assets/branding` contains the original icon sheet and selected transparent image. The executable icon is `src/DesktopGuard.App/Resources/DesktopGuard.ico`.
- `README.portable.md` contains the end-user guide included in the portable ZIP as `README.md`.
- `SECURITY.md` records the threat model and controls; `AGENTS.md` defines repository conventions.

Core has no dependency on the Windows app. Production code does not reference test projects. See `AGENTS.md` for file ownership and security rules.

Desktop Guard is licensed under MIT-0; see [LICENSE](LICENSE). Third-party dependencies and bundled runtime components remain under their respective licenses.

## Requirements

- Windows 11 x64 for running the app.
- .NET 10 SDK for building and testing.

## Build and verify

From the repository root:

```powershell
dotnet restore DesktopGuard.sln --locked-mode
dotnet build DesktopGuard.sln --no-restore
dotnet test DesktopGuard.sln --no-restore
dotnet format DesktopGuard.sln --verify-no-changes
```

Create the portable x64 ZIP with the self-contained executable, portable user guide, and license:

```powershell
.\scripts\package-portable.ps1
```

The script restores in locked mode, publishes the self-contained compressed executable with the .NET and Windows App SDK runtimes, and packages `README.portable.md` as `README.md` in `artifacts/portable/DesktopGuard-win-x64.zip`. The executable is architecture-specific and runs without a separate .NET or Windows App SDK installation. Its bundled files are extracted to the user's `%TEMP%/.net` directory at startup, which increases first-start time. Before distributing a build, run `dotnet package list --vulnerable --include-transitive` and follow the manual Windows 11 release checklist in [SECURITY.md](SECURITY.md). Automated file-operation tests use temporary directories only.

### Prepare a GitHub release

After the workflow is on GitHub, push a version tag such as `v1.0.1`. GitHub Actions builds the portable ZIP on Windows and attaches it to a draft release. Complete the release checks in [SECURITY.md](SECURITY.md), review the draft, then publish it from GitHub. Each version tag must be new; the workflow fails if a release already exists for that tag and never replaces its assets. The temporary Actions artifact expires after one day. For private repositories, the workflow uses the repository's included Actions compute and storage quota; extra usage may be billed.

## Security

DG's main process runs as the signed-in user without elevation or a service. A restricted one-operation helper can be launched through UAC only after Access Denied on an explicit file action. DG treats names, paths, saved state, filesystem events, and notification activation as untrusted. It inspects direct children only, never resolves or executes shortcut targets, and fails closed on ambiguous paths, incomplete scans, unsafe state, and move conflicts. See [SECURITY.md](SECURITY.md).
