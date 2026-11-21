# Desktop Guard

Desktop Guard is a Windows 11 system-tray app that helps you review `.lnk` and `.url` shortcuts on your user Desktop and Public Desktop.

## Install and start

Desktop Guard is distributed as a ZIP, not an installer. Extract it to a stable folder under your user profile with normal per-user permissions. A suitable default is `%LOCALAPPDATA%\Programs\DesktopGuard`; avoid shared or Public folders.

Run `DesktopGuard.App.exe` from that final folder. Desktop Guard starts in the notification area by default. Open its main window from the tray icon. **Start at sign-in** and **Start minimized** are enabled by default and can be changed under **More > Settings**. In Settings, **Apply** saves without closing, **Save** saves and closes, and **Cancel** discards changes. Closing with unsaved changes asks whether to save, discard, or return. Press **Enter** or **Ctrl+S** to save and **Escape** to cancel. When start-at-sign-in is enabled, Desktop Guard registers the path of the executable you launched for your account. No administrator rights are needed to extract or start Desktop Guard.

Your settings, shortcut decisions, event log, and Recovery files live separately under `%LOCALAPPDATA%\DesktopGuard`, so updating or moving the program folder does not move that data. The first start may take longer while the bundled runtime is unpacked into your user temp folder.

## Review shortcuts

1. After each Desktop's first complete scan, existing shortcuts form its approved baseline. New `.lnk` and `.url` files remain where they are as **Pending review**. New Public Desktop links are left untouched until you act.
2. Select a pending shortcut and choose **Approve** to leave it in place as reviewed, or **Park** to move it into Recovery. Choose **Restore** to return a parked shortcut to its original Desktop folder. If its name is already used there, Desktop Guard preserves the existing item and restores with the first free numbered name, such as `Program(2).url`. The Desktop Guard launcher keeps its separate duplicate checks. If an app recreates a shortcut with the same launch settings as one still parked on that Desktop, Desktop Guard shows it as scheduled and parks it again after the delay in **More > Settings** (2–30 minutes, default 5). Existing pending shortcuts are also compared once after upgrading; approved shortcuts keep their decision. A changed or unreadable shortcut remains in review. Desktop Guard never automatically deletes a Desktop shortcut.
3. Hidden shortcuts stay in the list and are labeled **(Hidden)**. Use **More shortcut actions > Make visible** to clear the Hidden attribute. Desktop Guard preserves Hidden while a shortcut is in Recovery and clears it when publishing one to a Desktop.
4. Use **More shortcut actions** to rename a shortcut while keeping `.lnk` or `.url`, open Windows Properties, or delete it. Deleting from a Desktop sends the shortcut to the Windows Recycle Bin. Deleting from Recovery is permanent and requires confirmation.
   Desktop Guard also automatically removes duplicate copies in Recovery with the same launch settings on the same Desktop. It keeps the newest last-write date, or the first stored copy if dates tie, and shows a summary notification. Desktop Guard preserves last-write dates while moving shortcuts, so parking an older copy does not make it appear newer. Copies parked by older Desktop Guard versions may have transfer dates instead of their original dates. Icons and descriptions may differ between these copies. If either file cannot be safely checked, Desktop Guard preserves the files and shows a warning.
5. Use **Move to user Desktop** or **Move to Public Desktop** to move a selected shortcut between locations. If the name is already used in the destination folder, Desktop Guard preserves the existing item and chooses the first free numbered name. The same name in the other Desktop folder is allowed. Desktop Guard asks before moving a shortcut to Public Desktop. A launcher created by Desktop Guard is for your account and cannot be moved to Public Desktop from Desktop Guard, even after it is renamed.
6. Click a Windows notification to open Desktop Guard and select the relevant shortcut. The review list remains available if Windows suppresses notifications. Use **Scan now** to request a full scan, or **More > Open recovery folder** to inspect Recovery.
7. Choose **Add Desktop Guard shortcut** to create a launcher on your user Desktop. Desktop Guard does not replace an existing copy. For a launcher created by a version before v6, Desktop Guard cannot safely identify it by name alone; delete the old launcher explicitly, then create a new one.

Press **Ctrl+F** to focus search. With the shortcut list focused, **F2** renames the selected shortcut and **Delete** deletes it when the action is available.

Desktop Guard never executes or resolves shortcut targets. If an explicit file action is denied, Windows may ask for permission for that action. File locks do not trigger automatic retries; close the app or installer using the shortcut and choose **Retry operation** when it is available. If a destination name becomes occupied during restore or a Desktop move, Desktop Guard reports the conflict and preserves the files. Select the shortcut and choose **Restore** or the move action again to select a free name.

## Update or move Desktop Guard

- **Update in place:** Exit Desktop Guard from its tray menu. Extract the new ZIP into the same program folder and replace the existing files, then start Desktop Guard again. Your settings and Recovery data remain under `%LOCALAPPDATA%\DesktopGuard`.
- **Move to another folder:** Exit Desktop Guard first, copy the program folder, then launch `DesktopGuard.App.exe` from the new folder while the old folder is still available. When **Start at sign-in** is enabled, Desktop Guard updates its entry to the path it is now running from. If you use the Desktop launcher, delete the old launcher through Desktop Guard and choose **Add Desktop Guard shortcut** to create one that points to the new folder. After checking the new copy and launcher, exit Desktop Guard and remove the old program folder.

## Remove Desktop Guard

Turn off **Start at sign-in**, exit Desktop Guard from the tray menu, then delete the program folder. Desktop Guard leaves `%LOCALAPPDATA%\DesktopGuard` intact so Recovery data is not lost. Review or restore anything there before manually deleting that data folder.

The ZIP contains the self-contained executable, this guide as `README.html` and `README.md`, the Desktop Guard icon used by the HTML guide, and the MIT License. Desktop Guard does not install a service or updater. The executable is unsigned; Windows may show a downloaded-file security prompt depending on your system policy. See [LICENSE](LICENSE) for license terms.
