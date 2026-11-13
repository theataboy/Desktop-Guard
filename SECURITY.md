# Desktop Guard security baseline

Security is a requirement at design, implementation, review, and release. Revisit this document whenever a feature adds an input, dependency, permission, or file operation.

## Security goal and limits

DG is a least-privilege tool for detecting and reversibly managing desktop shortcuts. It is not an antivirus product or a security boundary. It cannot protect a user's files or DG's per-user state from a malicious process already running as that user, or from an administrator.

## Assets and trust boundaries

- Desktop and Public Desktop entries, including `.lnk` and `.url` contents.
- DG's local settings, inventory, event log, and recovery files.
- Filesystem watcher events and Windows notification activation data.
- The current user's startup `Run` registry value and the path to the running DG executable.
- Build dependencies and the published application folder.

Treat all of these inputs as untrusted, including data read back from DG's own files.

## Required controls

- Keep the main process at `asInvoker`; do not add a privileged service, network access, telemetry, or secret storage. Discovery and state-only approval never elevate. Only an explicit file operation that receives Win32 `ERROR_ACCESS_DENIED` may launch the same executable once with the `runas` verb. Sharing and lock violations never trigger UAC. Treat cancellation and every nonzero helper result as a failed operation.
- Keep elevated work in a command-only helper path that returns before WinForms, notifications, startup registration, or state-store initialization. Its bounded request contains a fixed operation enum, root kinds, opaque shortcut and operation IDs, recovery ID when required, and a validated basename. It accepts no caller-supplied path or command. Derive User Desktop and Local AppData from the interactive shell user's token, not from the elevated account; derive Public Desktop through the Windows known-folder provider. Validate that every resulting source and destination is a direct child of an allowed root before acting.
- Resolve Desktop roots through Windows known-folder APIs. Scan only direct children. Accept the OS-selected root path, including a redirected Desktop, but reject candidate entries that escape that root or are reparse points.
- During discovery, read only top-level entry names and filesystem attributes. Do not read file contents. Exclude directories and reject candidate entries that escape the selected root or are reparse points.
- Classify shortcuts by the `.lnk` and `.url` extensions only. Never execute a shortcut or launch, resolve, or parse its target/arguments during discovery or management.
- Validate leaf names using Windows filename rules before tracking or operating on them. Reject alternate data stream syntax, reserved device names, invalid characters, and trailing spaces or dots.
- Treat watcher events as hints. Reconcile with directory scans, validate each candidate again immediately before acting, and bound/deduplicate event queues.
- Use a capacity-one coalesced watcher signal queue. Run full scans at startup, after watcher errors, and periodically; do not use event-provided paths as candidates.
- Park only after an explicit user action, two stable checks at least one second apart, and a final source recheck immediately before moving. The stability check is a race guard, not evidence that an installer finished.
- Create the DG Desktop shortcut only after an explicit user action and complete scans of the current User and Public Desktops. Refuse creation when a `Desktop Guard.lnk` is tracked on either desktop or parked in recovery, and recheck both destinations immediately before publication. Resolve the User destination from the Windows known-folder provider, validate the exact fixed leaf name and direct-child path, and recheck the User root and destination immediately before publication. Write the link under an operation-specific hidden staging directory inside that Desktop, persist its expected content hash before publishing, and use a no-overwrite move. This creation path never invokes the elevation helper.
- Before restoring a parked `Desktop Guard.lnk`, check the other current Desktop and fail closed if it already contains that link. Never create or automatically remove a second copy to resolve a duplicate; keep both files and recovery data for an explicit user decision.
- Move only between the current user's Desktop, Public Desktop, and a per-shortcut folder under DG's local Recovery directory. Persist operation intent first. Create destination files under a hidden operation-specific staging directory inside the destination folder so the final shortcut inherits that folder's ACL; verify the opaque bytes before removing the source. Reconcile interrupted moves automatically only when exactly one safe endpoint exists. A manual retry may clean only the exact operation-owned staging directory and resume the same persisted operation. Never overwrite a desktop shortcut. On ambiguous paths, source replacement, corrupt state, access errors, locks, or destination conflicts, preserve all files and report the failure.
- Reject reparse points on candidate files, staging directories, per-shortcut recovery folders, the Recovery root, and DG's data directory. Revalidate both Desktop roots before management; if either path changed, keep prior decisions and block operations on the changed location.
- Enable shortcut management only after a complete scan of the current user Desktop. An incomplete scan keeps existing decisions visible but disables file operations until a complete scan succeeds.
- Keep the state file bounded to 4 MiB and write it to a temporary file before replacing the prior state. Reject oversized or malformed data without replacing the existing state.
- Keep notification activation data to a validated opaque shortcut ID resolved against current local state. Never put raw paths into activation arguments. Sanitize and bound names shown in notification text, and show at most five notifications per scan.
- Register only the DG-owned value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The command targets the current DG executable and a fixed `--background` argument; DG does not accept arbitrary commands from state or notification data.
- Keep event records local and limited to file name, path, and timestamp for at most 90 days. Bound notification text and preserve recovery data separately from the event log.
- Migrate schema v1 by preserving its Public checkpoint: paths in that checkpoint are the existing approved baseline; Public shortcut paths absent from it become pending when first scanned after migration. Migrate schema v2 without changing existing decisions or checkpoints. The schema v3 creation-operation hash is accepted only for a validated in-progress User Desktop shortcut creation. If state is corrupt, preserve it and require a safe recovery path; never silently reset approvals or reinterpret either Desktop as a new baseline. Do not trim recovery records to meet a storage cap; fail closed and preserve the prior state.

## Dependencies and release checks

- Keep exact dependency versions and commit NuGet lock files. NuGet vulnerability auditing covers direct and transitive dependencies.
- The Windows App SDK is required for unpackaged Windows notifications. Ship its self-contained x64 runtime with the application; do not introduce network access or runtime downloads.
- Publish the app as a self-contained compressed single-file executable. The configured self-extraction places bundled files under the current user's `%TEMP%\.net` directory before startup. Keep the default per-user extraction location; never redirect it to a shared writable directory. Keep the main process unelevated and validate extraction, same-user UAC, and over-the-shoulder UAC behavior on Windows 11 before release.
- Review security-related .NET analyzer diagnostics. Do not suppress one without recording why and what compensating control is used.
- Before a release, restore with `--locked-mode`, run `dotnet package list --vulnerable --include-transitive`, and review every finding. Unresolved high or critical advisories block release unless a documented mitigation makes the affected code unreachable.
- Automated security regression coverage is not complete until tests validate invalid or unknown notification activation IDs and watcher event floods or errors that require a full rescan. Keep these gaps explicit until those tests exist.
- Automated file-operation tests must use temporary directories. Manually validate tray lifecycle, sign-in registration, Windows notification display and activation, watcher-error recovery, same-user and over-the-shoulder UAC, UAC cancellation/policy denial, ACL inheritance, recovery, lock handling, and failure handling on Windows 11 before release.

## References

- [Microsoft Security Development Lifecycle](https://learn.microsoft.com/en-us/compliance/assurance/assurance-microsoft-security-development-lifecycle)
- [NuGet package vulnerability auditing](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)
- [.NET code analysis](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)
