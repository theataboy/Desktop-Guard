using DesktopGuard.App.Application;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Notifications;
using DesktopGuard.App.Persistence;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.App.Startup;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.State;

namespace DesktopGuard.App.Tests.Application;

[TestClass]
[DoNotParallelize]
public sealed class DesktopGuardApplicationContextTests
{
    [TestMethod]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    public void ShouldShowMainForm_UsesBackgroundArgumentAndMinimizedPreference(
        bool startInBackground,
        bool startMinimized,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            DesktopGuardApplicationContext.ShouldShowMainForm(startInBackground, startMinimized));
    }

    [TestMethod]
    public void Run_DoesNotShowMainFormWhenStartMinimizedIsEnabled()
    {
        Assert.IsFalse(RunOnSta(startInBackground: false, startMinimized: true));
    }

    [TestMethod]
    public void Run_DoesNotShowMainFormWhenBackgroundArgumentOverridesPreference()
    {
        Assert.IsFalse(RunOnSta(startInBackground: true, startMinimized: false));
    }

    private static bool RunOnSta(bool startInBackground, bool startMinimized)
    {
        bool? mainFormVisible = null;
        Exception? failure = null;
        Thread uiThread = new(() =>
        {
            try
            {
                mainFormVisible = RunScenario(startInBackground, startMinimized);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        Assert.IsTrue(uiThread.Join(TimeSpan.FromSeconds(15)), "The WinForms startup loop did not exit.");
        if (failure is not null)
        {
            throw new AssertFailedException($"The WinForms startup check failed: {failure}");
        }

        return mainFormVisible
            ?? throw new AssertFailedException("The main form visibility was not observed after startup.");
    }

    private static bool RunScenario(bool startInBackground, bool startMinimized)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        DesktopGuardStateStore stateStore = new(Path.Combine(temporaryDirectory.Path, "AppData"));
        stateStore.Save(new DesktopGuardState
        {
            StartAtSignIn = false,
            StartMinimized = startMinimized,
        });

        FixedDesktopRootProvider rootProvider = new(userDesktop, publicDesktop);
        NoOpStartAtSignInManager startAtSignInManager = new();
        WindowsNotificationService notifications = new();
        DesktopGuardController controller = new(
            new DesktopInventoryScanner(rootProvider),
            new InventoryReconciler(),
            stateStore,
            new ShortcutFileManager(rootProvider, stateStore),
            startAtSignInManager,
            notifications);
        int stateChangeCount = 0;
        controller.StateChanged += () => Interlocked.Increment(ref stateChangeCount);
        DesktopGuardApplicationContext context = new(
            controller,
            rootProvider,
            notifications,
            startInBackground,
            enableShellIntegration: false);

        bool? mainFormVisible = null;
        Exception? failure = null;
        int stableTicks = 0;
        int elapsedTicks = 0;
        using System.Windows.Forms.Timer timer = new() { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            elapsedTicks++;
            if (context.MainForm is not null
                && controller.State.Roots.Count == 2
                && Volatile.Read(ref stateChangeCount) >= 3
                && startAtSignInManager.WasCalled)
            {
                stableTicks++;
                if (stableTicks >= 4)
                {
                    mainFormVisible = context.MainForm.Visible;
                    timer.Stop();
                    context.ExitThread();
                }
            }
            else
            {
                stableTicks = 0;
            }

            if (elapsedTicks >= 200)
            {
                failure = new TimeoutException(
                    $"The application context did not finish startup within ten seconds. "
                    + $"MainForm assigned: {context.MainForm is not null}; "
                    + $"desktop roots loaded: {controller.State.Roots.Count}; "
                    + $"startup preference manager called: {startAtSignInManager.WasCalled}.");
                timer.Stop();
                context.ExitThread();
            }
        };
        timer.Start();

        System.Windows.Forms.Application.Run(context);

        if (failure is not null)
        {
            throw failure;
        }

        return mainFormVisible
            ?? throw new AssertFailedException("The main form visibility was not observed after startup.");
    }

    private sealed class FixedDesktopRootProvider(string userDesktop, string publicDesktop) : IDesktopRootProvider
    {
        public IReadOnlyList<DesktopRoot> GetRoots()
        {
            return
            [
                new DesktopRoot(DesktopRootKind.User, userDesktop),
                new DesktopRoot(DesktopRootKind.Public, publicDesktop),
            ];
        }
    }

    private sealed class NoOpStartAtSignInManager : IStartAtSignInManager
    {
        private int _wasCalled;

        public bool WasCalled => Volatile.Read(ref _wasCalled) != 0;

        public void SetEnabled(bool enabled, bool startMinimized)
        {
            Volatile.Write(ref _wasCalled, 1);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DesktopGuard.App.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
