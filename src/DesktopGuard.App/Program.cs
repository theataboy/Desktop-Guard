using System.Security.Principal;
using DesktopGuard.App.Application;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Notifications;
using DesktopGuard.App.Persistence;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.App.Startup;
using DesktopGuard.Core.State;

namespace DesktopGuard.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--elevated-file-operation", StringComparison.Ordinal))
        {
            return args.Length == 2
                ? ElevatedShortcutOperationRunner.Run(args[1])
                : 10;
        }

        if (args.Any(argument => string.Equals(argument, "--elevated-file-operation", StringComparison.Ordinal)))
        {
            return 10;
        }

        ApplicationConfiguration.Initialize();

        string userId = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The signed-in Windows user could not be identified.");
        using Mutex instanceMutex = new(initiallyOwned: true, $"Local\\DesktopGuard-{userId}", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        IDesktopRootProvider rootProvider = new WindowsDesktopRootProvider();
        DesktopInventoryScanner scanner = new(rootProvider);
        DesktopGuardStateStore stateStore = new();
        WindowsNotificationService notifications = new();
        ShortcutFileManager fileManager = new(rootProvider, stateStore);
        DesktopGuardController controller = new(
            scanner,
            new InventoryReconciler(),
            stateStore,
            fileManager,
            new StartAtSignInManager(),
            notifications);

        DesktopGuardApplicationContext context = new(
            controller,
            rootProvider,
            notifications,
            args.Any(argument => string.Equals(argument, "--background", StringComparison.Ordinal)));
        System.Windows.Forms.Application.Run(context);
        return 0;
    }
}
