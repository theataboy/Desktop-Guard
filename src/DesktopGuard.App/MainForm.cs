using System.Diagnostics;
using DesktopGuard.App.Application;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Persistence;
using DesktopGuard.App.Resources;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.App;

internal partial class MainForm : Form
{
    private const int MaximumDisplayedIssues = 5;

    private readonly DesktopGuardController _controller;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<string> _runtimeWarnings = [];
    private bool _isUpdatingView;
    private bool _isBusy;
    private bool _canManage;

    public MainForm(DesktopGuardController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        InitializeComponent();
        Text = AppStrings.ApplicationTitle;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application;
        scanButton.Text = AppStrings.ScanNow;
        approveButton.Text = AppStrings.ApproveSelected;
        parkButton.Text = AppStrings.ParkSelected;
        restoreButton.Text = AppStrings.RestoreSelected;
        moveToUserButton.Text = AppStrings.MoveToUserDesktop;
        moveToPublicButton.Text = AppStrings.MoveToPublicDesktop;
        addDesktopShortcutButton.Text = AppStrings.AddDesktopShortcut;
        retryOperationButton.Text = AppStrings.RetryOperation;
        openRecoveryFolderButton.Text = AppStrings.OpenRecoveryFolder;
        startAtSignInCheckBox.Text = AppStrings.StartAtSignIn;
        statusLabel.Text = AppStrings.Loading;
        nameColumn.Text = AppStrings.NameColumn;
        locationColumn.Text = AppStrings.LocationColumn;
        statusColumn.Text = AppStrings.StatusColumn;
        issuesGroupBox.Text = AppStrings.Issues;

        scanButton.Click += async (_, _) => await RunCommandAsync(
            () => _controller.RefreshAsync(_shutdown.Token),
            AppStrings.Scanning);
        approveButton.Click += async (_, _) => await ApproveSelectedAsync();
        parkButton.Click += async (_, _) => await ParkSelectedAsync();
        restoreButton.Click += async (_, _) => await RestoreSelectedAsync();
        moveToUserButton.Click += async (_, _) => await MoveSelectedAsync(DesktopRootKind.User);
        moveToPublicButton.Click += async (_, _) => await MoveSelectedAsync(DesktopRootKind.Public);
        addDesktopShortcutButton.Click += async (_, _) => await AddDesktopShortcutAsync();
        retryOperationButton.Click += async (_, _) => await RetrySelectedOperationAsync();
        openRecoveryFolderButton.Click += (_, _) => OpenRecoveryFolder();
        entriesListView.SelectedIndexChanged += (_, _) => UpdateCommandAvailability();
        entriesListView.ItemCheck += EntriesListView_ItemCheck;
        startAtSignInCheckBox.CheckedChanged += async (_, _) => await StartAtSignInCheckBox_CheckedChangedAsync();
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;
        _controller.StateChanged += OnControllerStateChanged;
        RefreshView();
    }

    public void SetNotificationUnavailable()
    {
        AddRuntimeWarning(AppStrings.NotificationUnavailable);
    }

    public bool CanAddDesktopShortcut => _canManage && !_isBusy && _controller.CanAddDesktopShortcut;

    public Task AddDesktopShortcutAsync()
    {
        return RunCommandAsync(
            () => _controller.AddDesktopShortcutAsync(_shutdown.Token),
            AppStrings.AddingDesktopShortcut,
            showFailureDialog: true);
    }

    public void SetRuntimeWarning(string warning)
    {
        AddRuntimeWarning(warning);
    }

    public void SetUnavailable(string message)
    {
        _canManage = false;
        AddRuntimeWarning(message);
        UpdateCommandAvailability();
    }

    public void SetReady()
    {
        _canManage = true;
        RefreshView();
    }

    public void SetWatchFailure(DesktopRootKind rootKind)
    {
        string rootName = GetRootName(rootKind);
        AddRuntimeWarning(string.Format(AppStrings.RootWatchFailedFormat, rootName));
    }

    public void ShowAndSelectShortcut(Guid? shortcutId)
    {
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        if (shortcutId is not Guid id || id == Guid.Empty)
        {
            return;
        }

        foreach (ListViewItem item in entriesListView.Items)
        {
            if (item.Tag is ShortcutListEntry { Shortcut: { } shortcut } && shortcut.Id == id)
            {
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                break;
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controller.StateChanged -= OnControllerStateChanged;
        _shutdown.Cancel();
        _shutdown.Dispose();
        base.OnFormClosed(e);
    }

    private async Task ApproveSelectedAsync()
    {
        ShortcutListEntry? entry = GetSelectedEntry();
        if (entry?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.ApproveAsync(shortcut.Id, _shutdown.Token),
            string.Empty);
    }

    private async Task ParkSelectedAsync()
    {
        ShortcutListEntry? entry = GetSelectedEntry();
        if (entry?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.ParkAsync(shortcut.Id, _shutdown.Token),
            AppStrings.Parking);
    }

    private async Task RestoreSelectedAsync()
    {
        ShortcutListEntry? entry = GetSelectedEntry();
        if (entry?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.RestoreAsync(shortcut.Id, _shutdown.Token),
            AppStrings.Restoring);
    }

    private async Task MoveSelectedAsync(DesktopRootKind destinationRootKind)
    {
        ShortcutListEntry? entry = GetSelectedEntry();
        if (entry?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        if (destinationRootKind == DesktopRootKind.Public
            && MessageBox.Show(
                this,
                AppStrings.PublicVisibilityConfirmation,
                AppStrings.ApplicationTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.MoveAsync(shortcut.Id, destinationRootKind, _shutdown.Token),
            AppStrings.Moving);
    }

    private async Task RetrySelectedOperationAsync()
    {
        ShortcutListEntry? entry = GetSelectedEntry();
        if (entry?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        string progressMessage = shortcut.Operation == ShortcutOperation.Creating
            ? AppStrings.AddingDesktopShortcut
            : shortcut.Operation == ShortcutOperation.Parking
                ? AppStrings.Parking
                : shortcut.Operation == ShortcutOperation.Restoring
                    ? AppStrings.Restoring
                    : AppStrings.Moving;
        await RunCommandAsync(
            () => _controller.RetryPendingOperationAsync(shortcut.Id, _shutdown.Token),
            progressMessage,
            showFailureDialog: shortcut.Operation == ShortcutOperation.Creating);
    }

    private void OpenRecoveryFolder()
    {
        try
        {
            _controller.EnsureRecoveryDirectory();
            _ = Process.Start(new ProcessStartInfo(_controller.RecoveryDirectoryPath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            AddRuntimeWarning(AppStrings.RecoveryFolderUnavailable);
        }
    }

    private async Task StartAtSignInCheckBox_CheckedChangedAsync()
    {
        if (_isUpdatingView)
        {
            return;
        }

        bool enabled = startAtSignInCheckBox.Checked;
        await RunCommandAsync(
            () => _controller.SetStartAtSignInAsync(enabled, _shutdown.Token),
            string.Empty);
    }

    private void EntriesListView_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (!_canManage || _isUpdatingView || _isBusy || e.Index < 0 || e.Index >= entriesListView.Items.Count)
        {
            return;
        }

        ListViewItem item = entriesListView.Items[e.Index];
        if (item.Tag is not ShortcutListEntry entry
            || entry.Shortcut is not TrackedShortcut shortcut
            || !_controller.IsManageableShortcut(shortcut.Id)
            || shortcut.Operation != ShortcutOperation.None)
        {
            e.NewValue = e.CurrentValue;
            return;
        }

        if (shortcut.Status == ShortcutStatus.ApprovedVisible && e.NewValue == CheckState.Unchecked)
        {
            e.NewValue = e.CurrentValue;
            _ = BeginInvoke(new Action(async () => await RunCommandAsync(
                () => _controller.ParkAsync(shortcut.Id, _shutdown.Token),
                AppStrings.Parking)));
        }
        else if (shortcut.Status == ShortcutStatus.Parked && e.NewValue == CheckState.Checked)
        {
            e.NewValue = e.CurrentValue;
            _ = BeginInvoke(new Action(async () => await RunCommandAsync(
                () => _controller.RestoreAsync(shortcut.Id, _shutdown.Token),
                AppStrings.Restoring)));
        }
        else
        {
            e.NewValue = e.CurrentValue;
        }
    }

    private async Task RunCommandAsync(Func<Task> command, string progressMessage, bool showFailureDialog = false)
    {
        if (!_canManage || _isBusy)
        {
            return;
        }

        _isBusy = true;
        RefreshView(progressMessage);
        try
        {
            await command();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Detailed exception text can include untrusted paths, so show a bounded resource message instead.
            if (showFailureDialog)
            {
                MessageBox.Show(
                    this,
                    GetFailureMessage(exception),
                    AppStrings.ApplicationTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            _isBusy = false;
            RefreshView();
        }
    }

    private void OnControllerStateChanged()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => RefreshView()));
            }
            catch (InvalidOperationException)
            {
                // The window is closing.
            }

            return;
        }

        RefreshView();
    }

    private void RefreshView(string? progressMessage = null)
    {
        if (IsDisposed)
        {
            return;
        }

        Guid? selectedId = GetSelectedEntry()?.Shortcut?.Id;
        _isUpdatingView = true;
        try
        {
            entriesListView.BeginUpdate();
            entriesListView.Items.Clear();
            IReadOnlyList<ShortcutListEntry> entries = _controller.GetShortcutList();
            foreach (ShortcutListEntry entry in entries)
            {
                ListViewItem item = new(entry.Name)
                {
                    Checked = entry.Status == ShortcutStatus.ApprovedVisible,
                    Tag = entry,
                };
                item.SubItems.Add(GetLocationName(entry));
                item.SubItems.Add(GetStatusName(entry));
                entriesListView.Items.Add(item);

                if (selectedId is Guid id && entry.Shortcut?.Id == id)
                {
                    item.Selected = true;
                    item.Focused = true;
                }
            }

            entriesListView.EndUpdate();
            startAtSignInCheckBox.Checked = _controller.State.StartAtSignIn;
            UpdateCommandAvailability();
            DisplayStatus(progressMessage);
            DisplayIssues();
        }
        finally
        {
            _isUpdatingView = false;
        }
    }

    private void DisplayStatus(string? progressMessage)
    {
        if (!string.IsNullOrWhiteSpace(progressMessage))
        {
            statusLabel.Text = progressMessage;
            return;
        }

        if (_controller.LastFailure is not null)
        {
            statusLabel.Text = GetFailureMessage(_controller.LastFailure);
            return;
        }

        if (_controller.StartupFailure is not null)
        {
            statusLabel.Text = AppStrings.StartupFailure;
            return;
        }

        if (_runtimeWarnings.Count > 0)
        {
            statusLabel.Text = _runtimeWarnings[0];
            return;
        }

        int itemCount = _controller.GetShortcutList().Count;
        statusLabel.Text = _controller.ScanIssues.Count == 0
            ? string.Format(AppStrings.ScanCompleteFormat, itemCount)
            : string.Format(AppStrings.ScanCompleteWithIssuesFormat, itemCount, _controller.ScanIssues.Count);
    }

    private void DisplayIssues()
    {
        issuesListBox.BeginUpdate();
        issuesListBox.Items.Clear();

        List<string> messages = [];
        if (_controller.LastFailure is not null)
        {
            messages.Add(GetFailureMessage(_controller.LastFailure));
        }

        if (_controller.StartupFailure is not null)
        {
            messages.Add(AppStrings.StartupFailure);
        }

        messages.AddRange(_runtimeWarnings);
        messages.AddRange(_controller.ScanIssues.Take(MaximumDisplayedIssues).Select(GetIssueMessage));

        if (messages.Count == 0)
        {
            issuesListBox.Items.Add(_controller.GetShortcutList().Count == 0
                ? AppStrings.NoShortcuts
                : AppStrings.NoIssuesFound);
        }
        else
        {
            foreach (string message in messages.Take(MaximumDisplayedIssues))
            {
                issuesListBox.Items.Add(message);
            }

            if (messages.Count > MaximumDisplayedIssues)
            {
                issuesListBox.Items.Add(string.Format(
                    AppStrings.AdditionalIssuesFormat,
                    messages.Count - MaximumDisplayedIssues));
            }
        }

        issuesListBox.EndUpdate();
    }

    private void UpdateCommandAvailability()
    {
        ShortcutListEntry? selected = GetSelectedEntry();
        TrackedShortcut? shortcut = selected?.Shortcut;
        bool manageable = shortcut is not null && _controller.IsManageableShortcut(shortcut.Id);
        bool noOperation = manageable && shortcut!.Operation == ShortcutOperation.None;
        bool activeShortcut = shortcut?.Status is ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible;
        approveButton.Enabled = _canManage && !_isBusy && noOperation
            && shortcut?.Status == ShortcutStatus.Pending;
        parkButton.Enabled = _canManage && !_isBusy && noOperation && activeShortcut;
        restoreButton.Enabled = _canManage && !_isBusy && noOperation
            && shortcut?.Status == ShortcutStatus.Parked;
        moveToUserButton.Enabled = _canManage && !_isBusy && noOperation && activeShortcut
            && selected?.RootKind == DesktopRootKind.Public;
        moveToPublicButton.Enabled = _canManage && !_isBusy && noOperation && activeShortcut
            && selected?.RootKind == DesktopRootKind.User;
        retryOperationButton.Enabled = _canManage && !_isBusy && manageable
            && shortcut?.Operation != ShortcutOperation.None
            && shortcut is not null
            && _controller.IsOperationUnresolved(shortcut.Id);
        scanButton.Enabled = _canManage && !_isBusy;
        startAtSignInCheckBox.Enabled = _canManage && !_isBusy;
        openRecoveryFolderButton.Enabled = _canManage && !_isBusy;
        addDesktopShortcutButton.Enabled = CanAddDesktopShortcut;
    }

    private ShortcutListEntry? GetSelectedEntry()
    {
        return entriesListView.SelectedItems.Count == 0
            ? null
            : entriesListView.SelectedItems[0].Tag as ShortcutListEntry;
    }

    private static string GetLocationName(ShortcutListEntry entry)
    {
        return entry.Status == ShortcutStatus.Parked
            ? AppStrings.RecoveryFolder
            : entry.RootKind == DesktopRootKind.Public
                ? AppStrings.PublicDesktop
                : AppStrings.UserDesktop;
    }

    private string GetStatusName(ShortcutListEntry entry)
    {
        if (entry.Shortcut is not TrackedShortcut shortcut)
        {
            return AppStrings.ReviewRequired;
        }

        if (_controller.IsOperationUnresolved(shortcut.Id))
        {
            return AppStrings.RecoveryCheckRequired;
        }

        if (!_controller.IsManageableShortcut(shortcut.Id))
        {
            return AppStrings.RecoveryCheckRequired;
        }

        return shortcut.Operation switch
        {
            ShortcutOperation.Parking => AppStrings.Parking,
            ShortcutOperation.Restoring => AppStrings.Restoring,
            ShortcutOperation.Moving => AppStrings.Moving,
            ShortcutOperation.Creating => AppStrings.CreatingDesktopShortcut,
            _ => shortcut.Status switch
            {
                ShortcutStatus.Pending => AppStrings.PendingReview,
                ShortcutStatus.ApprovedVisible => AppStrings.ApprovedVisible,
                ShortcutStatus.Parked => AppStrings.Parked,
                _ => AppStrings.RecoveryCheckRequired,
            },
        };
    }

    private static string GetRootName(DesktopRootKind rootKind)
    {
        return rootKind == DesktopRootKind.Public ? AppStrings.PublicDesktop : AppStrings.UserDesktop;
    }

    private static string GetIssueMessage(DesktopScanIssue issue)
    {
        string rootName = GetRootName(issue.RootKind);
        return issue.Kind switch
        {
            DesktopScanIssueKind.RootUnavailable => string.Format(AppStrings.RootUnavailableFormat, rootName),
            DesktopScanIssueKind.RootPathChanged => string.Format(AppStrings.RootPathChangedFormat, rootName),
            DesktopScanIssueKind.RootCouldNotBeRead => string.Format(AppStrings.RootCouldNotBeReadFormat, rootName),
            DesktopScanIssueKind.EntryCouldNotBeInspected => string.Format(AppStrings.EntryCouldNotBeInspectedFormat, rootName),
            DesktopScanIssueKind.EntryOutsideRootSkipped => string.Format(AppStrings.EntryOutsideRootSkippedFormat, rootName),
            DesktopScanIssueKind.ReparsePointSkipped => string.Format(AppStrings.ReparsePointSkippedFormat, rootName),
            DesktopScanIssueKind.EntryLimitReached => string.Format(AppStrings.EntryLimitReachedFormat, rootName),
            _ => AppStrings.UnknownScanIssue,
        };
    }

    private static string GetFailureMessage(Exception exception)
    {
        if (exception is ShortcutFileOperationException fileException)
        {
            return fileException.Failure switch
            {
                ShortcutFileOperationFailure.DestinationConflict => AppStrings.DestinationConflict,
                ShortcutFileOperationFailure.DesktopShortcutAlreadyExists => AppStrings.DesktopShortcutAlreadyExists,
                ShortcutFileOperationFailure.FileNotStable => AppStrings.FileNotStable,
                ShortcutFileOperationFailure.SourceChanged => AppStrings.FileNotStable,
                ShortcutFileOperationFailure.SourceMissing => AppStrings.SourceMissing,
                ShortcutFileOperationFailure.AccessDenied => AppStrings.AccessDenied,
                ShortcutFileOperationFailure.FileLocked => AppStrings.FileLocked,
                ShortcutFileOperationFailure.ElevationCancelled => AppStrings.ElevationCancelled,
                ShortcutFileOperationFailure.ElevationFailed => AppStrings.ElevationFailed,
                ShortcutFileOperationFailure.InvalidPath => AppStrings.InvalidPath,
                ShortcutFileOperationFailure.RecoveryUnavailable => AppStrings.RecoveryUnavailable,
                ShortcutFileOperationFailure.AmbiguousRecovery => AppStrings.AmbiguousRecovery,
                ShortcutFileOperationFailure.IoFailure => AppStrings.IoFailure,
                _ => AppStrings.OperationFailure,
            };
        }

        if (exception is StateStoreException stateException)
        {
            return stateException.Kind switch
            {
                StateStoreFailureKind.Corrupt or StateStoreFailureKind.UnsafePath => AppStrings.StateLoadFailure,
                StateStoreFailureKind.TooLarge => AppStrings.StateTooLarge,
                _ => AppStrings.StateUnavailable,
            };
        }

        return exception is UnauthorizedAccessException or System.Security.SecurityException
            ? AppStrings.AccessDenied
            : AppStrings.OperationFailure;
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F12 || e.Modifiers != (Keys.Control | Keys.Shift | Keys.Alt))
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        MessageBox.Show(
            this,
            EmbeddedContent.Read(),
            AppStrings.ApplicationTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void AddRuntimeWarning(string warning)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => AddRuntimeWarning(warning)));
            }
            catch (InvalidOperationException)
            {
                // The window is closing.
            }

            return;
        }

        if (!_runtimeWarnings.Contains(warning, StringComparer.Ordinal))
        {
            _runtimeWarnings.Add(warning);
            RefreshView();
        }
    }
}
