using DesktopGuard.App.Resources;
using DesktopGuard.Core.Shortcuts;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DesktopGuard.App.Notifications;

internal sealed class WindowsNotificationService : IDisposable
{
    private const int MaximumNotificationNameLength = 100;

    private AppNotificationManager? _manager;
    private bool _registered;

    public event Action<Guid?>? NotificationActivated;

    public bool TryRegister(out Exception? failure)
    {
        if (_registered)
        {
            failure = null;
            return true;
        }

        AppNotificationManager? manager = null;
        try
        {
            manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
            _manager = manager;
            _registered = true;
            failure = null;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException
            or System.ComponentModel.Win32Exception
            or TypeInitializationException
            or TypeLoadException
            or FileNotFoundException
            or FileLoadException
            or BadImageFormatException
            or MissingMethodException)
        {
            if (manager is not null)
            {
                manager.NotificationInvoked -= OnNotificationInvoked;
            }

            failure = exception;
            return false;
        }
    }

    public void ShowNewShortcut(TrackedShortcut? shortcut, string name, bool isPublic)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            string safeName = SanitizeName(name);
            AppNotificationBuilder builder = new AppNotificationBuilder()
                .AddText(isPublic ? AppStrings.PublicShortcutNotificationTitle : AppStrings.ShortcutNotificationTitle)
                .AddText(string.Format(AppStrings.ShortcutNotificationBodyFormat, safeName));

            if (shortcut is not null && shortcut.Id != Guid.Empty)
            {
                builder.AddArgument("shortcutId", shortcut.Id.ToString("N"));
            }

            _manager?.Show(builder.BuildNotification());
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException
            or System.ComponentModel.Win32Exception
            or ArgumentException
            or TypeInitializationException
            or TypeLoadException
            or FileNotFoundException
            or FileLoadException
            or BadImageFormatException
            or MissingMethodException)
        {
            // Notification availability is controlled by Windows. The review list remains authoritative.
        }
    }

    public void Dispose()
    {
        AppNotificationManager? manager = _manager;
        if (manager is null)
        {
            return;
        }

        manager.NotificationInvoked -= OnNotificationInvoked;
        if (_registered)
        {
            try
            {
                manager.Unregister();
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.Runtime.InteropServices.COMException)
            {
                // The process is exiting; Windows may already have removed the registration.
            }

            _registered = false;
        }

        _manager = null;
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        NotificationActivated?.Invoke(NotificationActivation.ParseShortcutId(args.Arguments));
    }

    private static string SanitizeName(string name)
    {
        char[] safeCharacters = name
            .Where(character => !char.IsControl(character))
            .Take(MaximumNotificationNameLength)
            .ToArray();
        string safeName = new(safeCharacters);
        return string.IsNullOrWhiteSpace(safeName) ? AppStrings.UnknownShortcutName : safeName;
    }
}
