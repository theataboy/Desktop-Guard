using DesktopGuard.App.Application;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Notifications;
using DesktopGuard.App.Persistence;
using DesktopGuard.App.Resources;
using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App;

internal sealed class DesktopGuardApplicationContext : ApplicationContext
{
    private readonly DesktopGuardController _controller;
    private readonly WindowsNotificationService _notifications;
    private readonly MainForm _mainForm;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _scanNowMenuItem;
    private readonly ToolStripMenuItem _recoveryMenuItem;
    private readonly ToolStripMenuItem _startAtSignInMenuItem;
    private readonly ToolStripMenuItem _addDesktopShortcutMenuItem;
    private DesktopMonitor? _monitor;
    private bool _isExiting;

    public DesktopGuardApplicationContext(
        DesktopGuardController controller,
        IDesktopRootProvider rootProvider,
        WindowsNotificationService notifications,
        bool startInBackground)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(rootProvider);
        ArgumentNullException.ThrowIfNull(notifications);
        _controller = controller;
        _notifications = notifications;
        _mainForm = new MainForm(controller);
        _mainForm.ThemeChanged += OnThemeChanged;
        _mainForm.CommandAvailabilityChanged += UpdateTraySettings;
        MainForm = _mainForm;
        _mainForm.FormClosing += OnMainFormClosing;
        _mainForm.Resize += OnMainFormResize;
        _notifications.NotificationActivated += OnNotificationActivated;
        _controller.StateChanged += OnControllerStateChanged;

        ToolStripMenuItem openItem = new(AppStrings.OpenDesktopGuard)
        {
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
        };
        openItem.Click += (_, _) => ShowMainForm();
        _scanNowMenuItem = new ToolStripMenuItem(AppStrings.ScanNow)
        {
            Enabled = false,
        };
        _scanNowMenuItem.Click += async (_, _) =>
        {
            await _mainForm.ScanNowAsync();
            UpdateTraySettings();
        };
        _recoveryMenuItem = new ToolStripMenuItem(AppStrings.OpenRecoveryFolder)
        {
            Enabled = false,
        };
        _recoveryMenuItem.Click += (_, _) => _mainForm.OpenRecoveryFolder(showFailureDialog: true);
        _addDesktopShortcutMenuItem = new ToolStripMenuItem(AppStrings.AddDesktopShortcut)
        {
            Visible = false,
        };
        _addDesktopShortcutMenuItem.Click += async (_, _) =>
        {
            await _mainForm.AddDesktopShortcutAsync();
            UpdateTraySettings();
        };
        _startAtSignInMenuItem = new ToolStripMenuItem(AppStrings.StartAtSignIn)
        {
            CheckOnClick = true,
            Enabled = false,
        };
        _startAtSignInMenuItem.Click += async (_, _) => await StartAtSignInMenuItem_ClickAsync();
        ToolStripMenuItem exitItem = new(AppStrings.Exit);
        exitItem.Click += (_, _) => ExitThread();

        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.AddRange([
            openItem,
            new ToolStripSeparator(),
            _scanNowMenuItem,
            _recoveryMenuItem,
            _addDesktopShortcutMenuItem,
            new ToolStripSeparator(),
            _startAtSignInMenuItem,
            new ToolStripSeparator(),
            exitItem]);
        WindowsTheme.Apply(_trayMenu);
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = _trayMenu,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application,
            Text = AppStrings.ApplicationTitle,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainForm();

        if (startInBackground)
        {
            _ = _mainForm.Handle;
        }
        else
        {
            _mainForm.Show();
        }

        if (!_notifications.TryRegister(out _))
        {
            _mainForm.SetNotificationUnavailable();
        }

        _mainForm.BeginInvoke(new Action(async () => await InitializeAsync(rootProvider)));
    }

    protected override void ExitThreadCore()
    {
        if (_isExiting)
        {
            base.ExitThreadCore();
            return;
        }

        _isExiting = true;
        _monitor?.Dispose();
        _notifications.NotificationActivated -= OnNotificationActivated;
        _notifications.Dispose();
        _controller.StateChanged -= OnControllerStateChanged;
        _mainForm.ThemeChanged -= OnThemeChanged;
        _mainForm.CommandAvailabilityChanged -= UpdateTraySettings;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();

        if (!_mainForm.IsDisposed)
        {
            _mainForm.Close();
        }

        base.ExitThreadCore();
    }

    private async Task InitializeAsync(IDesktopRootProvider rootProvider)
    {
        try
        {
            await Task.Run(_controller.Initialize);
            _monitor = new DesktopMonitor(
                rootProvider,
                cancellationToken => _controller.RefreshAsync(cancellationToken));
            _monitor.ScanFailed += _ => _mainForm.SetRuntimeWarning(AppStrings.UnexpectedScanFailure);
            _monitor.WatchFailed += _mainForm.SetWatchFailure;
            _monitor.Start();
            _mainForm.SetReady();
            _startAtSignInMenuItem.Enabled = true;
            UpdateTraySettings();
        }
        catch (Exception exception)
        {
            string failureMessage = exception is StateStoreException stateException
                ? stateException.Kind switch
                {
                    StateStoreFailureKind.Corrupt or StateStoreFailureKind.UnsafePath => AppStrings.StateLoadFailure,
                    StateStoreFailureKind.TooLarge => AppStrings.StateTooLarge,
                    _ => AppStrings.StateUnavailable,
                }
                : AppStrings.InitializationFailure;
            _mainForm.SetUnavailable(failureMessage);
            _startAtSignInMenuItem.Enabled = false;
            if (_mainForm.Visible)
            {
                MessageBox.Show(
                    _mainForm,
                    failureMessage,
                    AppStrings.ApplicationTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    private async Task StartAtSignInMenuItem_ClickAsync()
    {
        bool enabled = _startAtSignInMenuItem.Checked;
        try
        {
            await _controller.SetStartAtSignInAsync(enabled, CancellationToken.None);
        }
        catch (Exception)
        {
            _startAtSignInMenuItem.Checked = _controller.State.StartAtSignIn;
            _mainForm.SetRuntimeWarning(AppStrings.StartupFailure);
        }
    }

    private void ShowMainForm()
    {
        _mainForm.ShowAndSelectShortcut(shortcutId: null);
    }

    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _mainForm.Hide();
    }

    private void OnMainFormResize(object? sender, EventArgs e)
    {
        if (_mainForm.WindowState == FormWindowState.Minimized)
        {
            _mainForm.Hide();
        }
    }

    private void OnNotificationActivated(Guid? shortcutId)
    {
        if (_mainForm.IsDisposed || !_mainForm.IsHandleCreated)
        {
            return;
        }

        try
        {
            _mainForm.BeginInvoke(new Action(() =>
            {
                Guid? validShortcutId = shortcutId is Guid id && _controller.ContainsShortcut(id) ? id : null;
                _mainForm.ShowAndSelectShortcut(validShortcutId);
            }));
        }
        catch (InvalidOperationException)
        {
            // The app is shutting down.
        }
    }

    private void OnControllerStateChanged()
    {
        if (_mainForm.IsDisposed || !_mainForm.IsHandleCreated)
        {
            return;
        }

        try
        {
            _mainForm.BeginInvoke(new Action(UpdateTraySettings));
        }
        catch (InvalidOperationException)
        {
            // The app is shutting down.
        }
    }

    private void OnThemeChanged()
    {
        if (!_mainForm.IsDisposed)
        {
            WindowsTheme.Apply(_trayMenu);
        }
    }

    private void UpdateTraySettings()
    {
        if (!_mainForm.IsDisposed)
        {
            _startAtSignInMenuItem.Checked = _controller.State.StartAtSignIn;
            _scanNowMenuItem.Enabled = _mainForm.CanScanNow;
            _recoveryMenuItem.Enabled = _mainForm.CanOpenRecoveryFolder;
            _addDesktopShortcutMenuItem.Visible = _mainForm.CanAddDesktopShortcut;
            _addDesktopShortcutMenuItem.Enabled = _mainForm.CanAddDesktopShortcut;
        }
    }
}
