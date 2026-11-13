# Repository guidance

## Product requirements

- Desktop Guard v1 targets Windows 11 and uses .NET 10 with Windows Forms.
- All user-facing application text is English in v1. Keep UI strings in `src/DesktopGuard.App/Resources/Strings.resx` and access them through the app's resource wrapper. English is the neutral fallback; add culture-specific resource files only when another language is requested. Do not add a language picker before then.
- Keep `SatelliteResourceLanguages` limited to `en-US` for v1; expand it when another UI language is added.
- Use `NotifyIcon` for the tray icon/menu and Windows App SDK `AppNotificationManager` for Windows notifications. Keep this integration in `DesktopGuard.App`; bundle the Windows App SDK runtime for the unpackaged local-folder release.
- Preserve the v1 behavior documented in `README.md`: monitor and manage `.lnk` and `.url` files on both the user and Public desktops. Existing Public links form the approved baseline; new Public links are pending review and remain untouched until the user acts. Discovery and approval never elevate. Observe other file types without modifying them.
- Treat the first complete scan of each desktop as its baseline. Do not classify existing items as new. If the user Desktop path changes, preserve its prior decisions and require review instead of silently establishing a new baseline.
- Never delete desktop files automatically. Parking and restore operations must be reversible, preserve the original path, and avoid overwriting an existing file.
- Park only after the user explicitly chooses it and the shortcut is accessible with the same size and last-write time across checks at least one second apart. Recheck immediately before moving. A stable-file check is a race guard, not proof that an installer has finished.
- Store user state and recovery data under `%LOCALAPPDATA%\DesktopGuard`, never in the repository or beside the executable.
- Keep the event log local and limited to the file name, path, and timestamp; prune entries older than 90 days. Provide recovery-folder commands in both the tray menu and main window.
- Start at sign-in is enabled by default and can be disabled. Register only DG's own value under the current user's `Run` key; never use a service.
- Treat file-system watcher events as coalesced hints. Keep the queue bounded and perform a full scan at startup, after watcher errors, and periodically.
- Show at most five Windows notifications per scan. Keep the review list authoritative if Windows suppresses notifications; notification arguments may contain only validated opaque shortcut IDs.

## Security requirements

- Treat security as a requirement for every design, implementation, review, and release change. Read `SECURITY.md` before feature work and update its threat model when a change adds an input, dependency, permission, or file operation.
- Treat filenames, paths, shortcut contents, filesystem events, saved state, and notification activation data as untrusted. Never execute a shortcut or launch/resolve its target during discovery or management.
- Validate leaf names using Windows filename rules. Reject alternate data stream syntax, reserved device names, invalid characters, and trailing spaces or dots before tracking or operating on a shortcut.
- Resolve Desktop roots through Windows known-folder APIs. Inspect only direct children; validate canonical candidate paths remain under the selected root and reject candidate reparse points. Recheck the source immediately before a file operation.
- Persist operation intent before parking, restoring, or moving between desktops. Create destination files under a hidden operation-specific staging directory within the destination folder so the file inherits destination ACLs. Verify copied bytes before removing the source. Reconcile interrupted actions automatically only when exactly one safe endpoint exists; allow a user-requested retry to clean only that operation's validated staging directory.
- A Public path change preserves previous decisions and blocks operations on the changed root. For schema v1 migration, preserve the existing Public checkpoint as the approved baseline; entries absent from that checkpoint become pending after upgrade.
- Keep the main app asInvoker and do not use a Windows service. Discovery and state-only approval never elevate. After an explicit file operation fails with Win32 Access Denied, a one-operation helper may relaunch the same executable with `runas`; sharing and lock errors never trigger UAC. The helper accepts only a fixed operation, opaque IDs, root kinds, and a validated shortcut name; it derives the original interactive user's known folders and never accepts arbitrary paths or commands. Do not add network access, telemetry, or secret storage to the DG v1 runtime.
- Fail closed on ambiguous paths, malformed state, access errors, and collisions: preserve the original and recovery data, show a clear error, and never overwrite or automatically delete a desktop file.
- Bound filesystem-event queues and notification text. Notification activation must use validated opaque identifiers, never raw paths.
- Do not suppress a security analyzer or dependency-audit finding without recording its reason and mitigation in `SECURITY.md`.

## Architecture and code

- Keep policy and state transitions in `DesktopGuard.Core`; keep WinForms, shell, startup, notifications, and filesystem integration in `DesktopGuard.App`.
- Keep `DesktopGuard.Core` platform-independent: it must not reference `DesktopGuard.App`, WinForms, Windows APIs, or perform filesystem, registry, notification, or process operations directly. Put required capabilities behind narrow interfaces and implement them in `DesktopGuard.App`.
- Keep project references one-way: `DesktopGuard.App` may reference `DesktopGuard.Core`; `DesktopGuard.Tests` may reference the projects it needs for focused tests; production projects must never reference test projects. Do not add projects or architectural layers without a concrete requirement.
- Keep WinForms event handlers and presentation code thin. They translate user actions into application operations and display results; policy decisions and state transitions belong in `DesktopGuard.Core`.
- Organize code by responsibility and, when a feature grows, by feature within its owning project. Use `DesktopGuard.Core` folders for domain policy/state and `DesktopGuard.App` folders for presentation, Windows integration, and persistence. Keep `Program.cs` at the app entry point and UI resources under `Resources`.
- Create a folder only when it has files to own. Avoid generic dumping grounds such as `Helpers`, `Utils`, or an all-purpose `Services` folder; name folders and types after the product responsibility they implement.
- Before adding a type, check whether an existing type already owns that responsibility and place the new type in the correct project and feature folder. Prefer focused types with one clear reason to change; avoid both oversized classes and one-line wrapper types that add no useful boundary.
- Put each meaningful top-level type in a correspondingly named source file. Keep namespaces aligned with the project and folder structure. Partial types are reserved for framework-generated patterns such as WinForms Designer files; do not split a type across files otherwise.
- Add an interface when it defines a real boundary, such as operating-system or storage access, or enables a meaningful test seam. Do not add interfaces, dependency-injection frameworks, base classes, or design patterns mechanically for every type.
- Keep dependencies flowing inward: presentation and Windows adapters call application/policy code; core policy does not depend on its callers. Avoid duplicate implementations of the same rule; give each rule one clear owner.
- Use nullable reference types, implicit usings, file-scoped namespaces, descriptive names, and the repository `.editorconfig`.
- Keep platform-independent core policy tests in `tests/DesktopGuard.Tests` using MSTest. Put Windows filesystem/UI integration tests in a separate Windows-targeted test project only when that boundary warrants it. Filesystem tests must use temporary directories, never the real user or Public Desktop.
- Mirror the production responsibility/feature names in the test folder structure. Name tests for observable behavior and include regression coverage when fixing a defect.
- Keep dependencies minimal. Add a package only when it provides a clear need that the .NET platform does not already meet.
- Keep operations on desktop files explicit and recoverable. Report access or move failures without silently treating inaccessible locations as empty.

### Initial project map

- `src/DesktopGuard.Core`: desktop inventory policy, shortcut state transitions, reconciliation, and state validation.
- `src/DesktopGuard.App`: `Application` orchestration; `DesktopScanning` scanner and bounded monitor; `Notifications`; `Persistence`; `ShortcutManagement`; `Startup`; WinForms entry point, main window, tray context, and `Resources`.
- `tests/DesktopGuard.Tests`: platform-independent scanning policy, reconciliation, validation, and shortcut transition tests.
- `tests/DesktopGuard.App.Tests`: Windows scanning, persistence, and file-operation tests. File operations use temporary directories, never actual Desktop folders.
- `README.md`: v1 product behavior, usage instructions, and common commands; `SECURITY.md`: threat model and security requirements; `AGENTS.md`: durable coding and repository rules.

This map is an ownership guide, not a requirement to create empty folders in advance. Add feature folders when implementation gives them a clear purpose, and update this map if a deliberate architecture change alters project ownership or dependencies.

## Repository workflow

- Build from the solution root with `dotnet build DesktopGuard.sln`.
- Run core tests with `dotnet test DesktopGuard.sln` when validation is requested.
- Audit direct and transitive NuGet packages with `dotnet package list --vulnerable --include-transitive` before release.
- Use `dotnet restore --locked-mode` for release builds; update and review package lock files when dependencies change.
- Check formatting from the solution root with `dotnet format DesktopGuard.sln --verify-no-changes`.
- Publish the self-contained compressed x64 executable with `dotnet publish src/DesktopGuard.App/DesktopGuard.App.csproj -c Release -p:PublishProfile=SelfContainedSingleFile -p:DebugSymbols=false -p:DebugType=None`.
- Keep generated `bin`, `obj`, test, and publish output out of Git.
- Preserve user changes. Do not stage or commit unless explicitly asked.
