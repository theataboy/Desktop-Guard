# Desktop Guard

Desktop Guard (DG) is a Windows 11 system-tray app that watches desktop shortcuts and lets the signed-in user review them without blocking installers.

## v1 behavior

- The first complete scan of each desktop establishes its baseline. Existing shortcuts on both desktops are marked **Approved and visible**; a migrated Public checkpoint remains the baseline after upgrade.
- New `.lnk` and `.url` files on the user Desktop remain in place as **Pending review**. The user can approve or park them.
- v1 has no automatic blacklist or whitelist rules; each new shortcut stays pending until the user reviews it.
- A checked shortcut is approved and visible. Unchecking it explicitly parks it in DG's recovery folder; checking a parked item restores it to its original path.
- Parking waits at least one second and checks the file again immediately before moving. This is a race guard; it does not prove an installer has finished.
- New shortcuts on either desktop appear as **Pending review**. Public Desktop additions remain untouched until the user acts.
- Shortcuts can be moved between the user and Public desktops. Moving to Public requires confirmation because the link becomes visible to all users; moving from Public to the user Desktop leaves it pending review.
- Other new files are recorded locally without being opened or changed.
- File-system notifications trigger coalesced full scans, with another full scan every 30 seconds. Windows notifications are a convenience; the list is the source of truth.
- The main window and tray menu can open the recovery folder. Closing the window hides DG to the tray; the tray menu controls start-at-sign-in and exits DG.
- Start at sign-in is enabled by default and can be turned off in the window or tray menu.
- DG never blocks shortcut creation, executes or resolves a shortcut, automatically parks a file, or overwrites a destination. It requests UAC only after an explicit file action receives Access Denied; file locks do not trigger UAC or automatic retries.

All v1 application text is English. User state, the bounded event log, and recovery files live under `%LOCALAPPDATA%\DesktopGuard`. The event log stores only file names, paths, and timestamps; entries older than 90 days are removed.

## Using Desktop Guard

1. Run `DesktopGuard.App.exe`. DG appears in the Windows notification area. Start at sign-in is enabled by default; change it from the main window or the tray menu if needed.
2. Open DG from its tray icon. After a complete scan, existing shortcuts are listed as **Approved and visible**. New `.lnk` and `.url` files on either desktop are listed as **Pending review** and stay where they are. Clicking a Windows notification opens DG and selects that shortcut; the list remains available if Windows suppresses notifications.
3. Select a pending shortcut and choose **Approve selected** to leave it visible, or **Park selected** to move it into DG's recovery folder. For an already approved shortcut, uncheck its row or choose **Park selected**. Restore a parked shortcut with **Restore selected** or by checking its row; DG returns it to its original desktop path.
4. Use **Move to user Desktop** or **Move to Public Desktop** to move a selected link between desktops. DG asks for confirmation before moving a link to Public because it becomes visible to everyone using the PC. Scanning and approval do not request elevation. If an explicit file operation receives Access Denied, Windows may show a permission prompt for that operation.
5. If an operation reports that a shortcut is in use, close the installer or app using it and choose **Retry operation**. DG does not retry automatically. If a destination already exists or a safety check fails, DG leaves the files unchanged and reports the issue.
6. Open the recovery folder from the main-window button or the tray menu to inspect parked shortcuts. Use **Scan now** to request another full scan.

## Project structure

- `src/DesktopGuard.Core` contains platform-independent inventory policy, state reconciliation, and shortcut status transitions.
- `src/DesktopGuard.App` contains WinForms presentation, tray and notification integration, filesystem monitoring, startup registration, file operations, and local persistence.
- `tests/DesktopGuard.Tests` contains Core policy and state tests.
- `tests/DesktopGuard.App.Tests` contains scanner, persistence, and temporary-directory file-operation tests.
- `assets/branding` contains the original icon sheet and selected transparent image. The executable icon is `src/DesktopGuard.App/Resources/DesktopGuard.ico`.
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

Publish the self-contained, compressed x64 executable with the .NET and Windows App SDK runtimes included:

```powershell
dotnet publish src/DesktopGuard.App/DesktopGuard.App.csproj -c Release -p:PublishProfile=SelfContainedSingleFile -p:DebugSymbols=false -p:DebugType=None --no-restore
```

The only output file is `DesktopGuard.App.exe` in `src/DesktopGuard.App/bin/Release/net10.0-windows10.0.22621.0/win-x64/publish-singlefile`. It is architecture-specific and contains the runtimes, so it can run without separate .NET or Windows App SDK installation. The bundled files are extracted to the user's `%TEMP%\.net` directory at startup; this increases first-start time. Before distributing a build, run the NuGet vulnerability audit and manually verify notifications, tray behavior, startup settings, recovery, and permission/conflict handling on Windows 11. Automated file-operation tests use temporary directories only.

## Security

DG's main process runs as the signed-in user without elevation or a service. A restricted one-operation helper can be launched through UAC only after Access Denied on an explicit file action. DG treats names, paths, saved state, filesystem events, and notification activation as untrusted. It inspects direct children only, never resolves or executes shortcut targets, and fails closed on ambiguous paths, incomplete scans, unsafe state, and move conflicts. See [SECURITY.md](SECURITY.md).
