# Desktop Guard

Desktop Guard (DG) is a Windows 11 system-tray app that helps you review `.lnk` and `.url` shortcuts on your user Desktop and Public Desktop.

## Install and start

DG is distributed as a ZIP, not an installer. Extract it to a stable folder under your user profile with normal per-user permissions. A suitable default is `%LOCALAPPDATA%\Programs\DesktopGuard`; avoid shared or Public folders.

Run `DesktopGuard.App.exe` from that final folder. DG starts in the notification area by default. Open its main window from the tray icon. **Start at sign-in** and **Start minimized** are enabled by default and can be changed under **More > Settings**. When start-at-sign-in is enabled, DG registers the path of the executable you launched for your account. No administrator rights are needed to extract or start DG.

Your settings, shortcut decisions, event log, and Recovery files live separately under `%LOCALAPPDATA%\DesktopGuard`, so updating or moving the program folder does not move that data. The first start may take longer while the bundled runtime is unpacked into your user temp folder.

## Review shortcuts

1. After each Desktop's first complete scan, existing shortcuts form its approved baseline. New `.lnk` and `.url` files remain where they are as **Pending review**. New Public Desktop links are left untouched until you act.
2. Select a pending shortcut and choose **Approve** to leave it in place as reviewed, or **Park** to move it into Recovery. Choose **Restore** to return a parked shortcut to its original Desktop path. DG does not automatically park or delete shortcuts.
3. Hidden shortcuts stay in the list and are labeled **(Hidden)**. Use **More shortcut actions > Make visible** to clear the Hidden attribute. DG preserves Hidden while a shortcut is in Recovery and clears it when publishing one to a Desktop.
4. Use **More shortcut actions** to rename a shortcut while keeping `.lnk` or `.url`, open Windows Properties, or delete it. Deleting from a Desktop sends the shortcut to the Windows Recycle Bin. Deleting from Recovery is permanent and requires confirmation.
5. Use **Move to user Desktop** or **Move to Public Desktop** to move a selected shortcut between locations. DG asks before moving a shortcut to Public Desktop. A launcher created by DG is for your account and cannot be moved to Public Desktop from DG, even after it is renamed.
6. Click a Windows notification to open DG and select the relevant shortcut. The review list remains available if Windows suppresses notifications. Use **Scan now** to request a full scan, or **More > Open recovery folder** to inspect Recovery.
7. Choose **Add Desktop Guard shortcut** to create a launcher on your user Desktop. DG does not replace an existing copy. For a launcher created by a version before v6, DG cannot safely identify it by name alone; delete the old launcher explicitly, then create a new one.

DG never executes or resolves shortcut targets. If an explicit file action is denied, Windows may ask for permission for that action. File locks do not trigger automatic retries; close the app or installer using the shortcut and choose **Retry operation**.

## Update or move DG

- **Update in place:** Exit DG from its tray menu. Extract the new ZIP into the same program folder and replace the existing files, then start DG again. Your settings and Recovery data remain under `%LOCALAPPDATA%\DesktopGuard`.
- **Move to another folder:** Exit DG first, copy the program folder, then launch `DesktopGuard.App.exe` from the new folder while the old folder is still available. When **Start at sign-in** is enabled, DG updates its entry to the path it is now running from. If you use the Desktop launcher, delete the old launcher through DG and choose **Add Desktop Guard shortcut** to create one that points to the new folder. After checking the new copy and launcher, exit DG and remove the old program folder.

## Remove DG

Turn off **Start at sign-in**, exit DG from the tray menu, then delete the program folder. DG leaves `%LOCALAPPDATA%\DesktopGuard` intact so Recovery data is not lost. Review or restore anything there before manually deleting that data folder.

The ZIP contains the self-contained executable, this guide as `README.md`, and the MIT-0 license. DG does not install a service or updater. The executable is unsigned; Windows may show a downloaded-file security prompt depending on your system policy. See [LICENSE](LICENSE) for license terms.
