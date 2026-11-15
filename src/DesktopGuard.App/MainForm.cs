using System.Diagnostics;
using DesktopGuard.App.Application;
using DesktopGuard.App.DesktopScanning;
using DesktopGuard.App.Persistence;
using DesktopGuard.App.Resources;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.Core.DesktopScanning;
using DesktopGuard.Core.Shortcuts;
using Microsoft.Win32;

namespace DesktopGuard.App;

internal partial class MainForm : Form
{
    private const int MaximumDisplayedIssues = 5;

    private readonly DesktopGuardController _controller;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<string> _runtimeWarnings = [];
    private readonly ShortcutListPresentation _presentation = new();
    private Button _shortcutActionsButton = null!;
    private ContextMenuStrip _shortcutActionsMenu = null!;
    private ToolStripMenuItem _renameShortcutMenuItem = null!;
    private ToolStripMenuItem _deleteShortcutMenuItem = null!;
    private ToolStripMenuItem _propertiesMenuItem = null!;
    private ToolStripMenuItem _makeVisibleMenuItem = null!;
    private bool _isUpdatingView;
    private bool _isBusy;
    private bool _canManage;
    private bool _issuesExpanded;
    private bool _shutdownStarted;

    public event Action? ThemeChanged;

    public event Action? CommandAvailabilityChanged;

    public MainForm(DesktopGuardController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        InitializeComponent();
        InitializeShortcutActions();
        Text = AppStrings.ApplicationTitle;
        titleLabel.Text = AppStrings.ApplicationTitle;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
            ?? SystemIcons.Application;
        scanButton.Text = AppStrings.ScanNowButton;
        addDesktopShortcutButton.Text = AppStrings.AddDesktopShortcutButton;
        moreButton.Text = AppStrings.More;
        openRecoveryFolderMenuItem.Text = AppStrings.OpenRecoveryFolderMenuItem;
        settingsMenuItem.Text = AppStrings.SettingsMenuItem;
        allFilterButton.Text = AppStrings.AllFilterFormat(0);
        needsReviewFilterButton.Text = AppStrings.NeedsReviewFilterFormat(0);
        approvedFilterButton.Text = AppStrings.ApprovedFilterFormat(0);
        recoveryFilterButton.Text = AppStrings.RecoveryFilterFormat(0);
        searchTextBox.PlaceholderText = AppStrings.SearchShortcuts;
        nameColumn.HeaderText = AppStrings.NameColumn;
        locationColumn.HeaderText = AppStrings.LocationColumn;
        statusColumn.HeaderText = AppStrings.StatusColumn;
        approveButton.Text = AppStrings.Approve;
        parkButton.Text = AppStrings.Park;
        restoreButton.Text = AppStrings.Restore;
        moveToUserButton.Text = AppStrings.MoveToUserDesktop;
        moveToPublicButton.Text = AppStrings.MoveToPublicDesktop;
        retryOperationButton.Text = AppStrings.RetryOperation;
        issuesToggleButton.Text = AppStrings.IssuesCountFormat(0);
        statusLabel.Text = AppStrings.Loading;
        selectedItemLabel.Text = AppStrings.SelectShortcutForActions;
        emptyStateLabel.Text = AppStrings.NoShortcuts;

        scanButton.Click += async (_, _) => await ScanNowAsync();
        addDesktopShortcutButton.Click += async (_, _) => await AddDesktopShortcutAsync();
        moreButton.Click += (_, _) => ShowMoreMenu();
        openRecoveryFolderMenuItem.Click += (_, _) => OpenRecoveryFolder();
        settingsMenuItem.Click += async (_, _) => await OpenSettingsAsync();
        approveButton.Click += async (_, _) => await ApproveSelectedAsync();
        parkButton.Click += async (_, _) => await ParkSelectedAsync();
        restoreButton.Click += async (_, _) => await RestoreSelectedAsync();
        moveToUserButton.Click += async (_, _) => await MoveSelectedAsync(DesktopRootKind.User);
        moveToPublicButton.Click += async (_, _) => await MoveSelectedAsync(DesktopRootKind.Public);
        retryOperationButton.Click += async (_, _) => await RetrySelectedOperationAsync();
        _shortcutActionsButton.Click += (_, _) => ShowShortcutActionsMenu();
        _renameShortcutMenuItem.Click += async (_, _) => await RenameSelectedAsync();
        _deleteShortcutMenuItem.Click += async (_, _) => await DeleteSelectedAsync();
        _propertiesMenuItem.Click += async (_, _) => await OpenPropertiesSelectedAsync();
        _makeVisibleMenuItem.Click += async (_, _) => await MakeVisibleSelectedAsync();
        allFilterButton.CheckedChanged += (_, _) => FilterButton_CheckedChanged(allFilterButton, ShortcutListFilter.All);
        needsReviewFilterButton.CheckedChanged += (_, _) => FilterButton_CheckedChanged(needsReviewFilterButton, ShortcutListFilter.NeedsReview);
        approvedFilterButton.CheckedChanged += (_, _) => FilterButton_CheckedChanged(approvedFilterButton, ShortcutListFilter.Approved);
        recoveryFilterButton.CheckedChanged += (_, _) => FilterButton_CheckedChanged(recoveryFilterButton, ShortcutListFilter.Recovery);
        searchTextBox.TextChanged += (_, _) => SearchTextBox_TextChanged();
        entriesGrid.SelectionChanged += (_, _) =>
        {
            if (!_isUpdatingView)
            {
                UpdateCommandAvailability();
            }
        };
        entriesGrid.ColumnHeaderMouseClick += EntriesGrid_ColumnHeaderMouseClick;
        issuesToggleButton.Click += (_, _) => ToggleIssues();
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;
        _controller.StateChanged += OnControllerStateChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplyTheme();
        RefreshView();
    }

    private void InitializeShortcutActions()
    {
        _shortcutActionsButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 4),
            Name = "shortcutActionsButton",
            Text = AppStrings.MoreShortcutActions,
            UseVisualStyleBackColor = true,
        };
        _shortcutActionsMenu = new ContextMenuStrip(components);
        _renameShortcutMenuItem = new ToolStripMenuItem(AppStrings.Rename);
        _propertiesMenuItem = new ToolStripMenuItem(AppStrings.Properties);
        _makeVisibleMenuItem = new ToolStripMenuItem(AppStrings.MakeVisible);
        _deleteShortcutMenuItem = new ToolStripMenuItem(AppStrings.Delete);
        _shortcutActionsMenu.Items.AddRange([
            _renameShortcutMenuItem,
            _propertiesMenuItem,
            _makeVisibleMenuItem,
            new ToolStripSeparator(),
            _deleteShortcutMenuItem,
        ]);
        actionPanel.Controls.Add(_shortcutActionsButton);
    }

    private void ShowShortcutActionsMenu()
    {
        if (_shortcutActionsButton.Enabled)
        {
            _shortcutActionsMenu.Show(_shortcutActionsButton, new Point(0, _shortcutActionsButton.Height));
        }
    }

    private async Task RenameSelectedAsync()
    {
        if (GetSelectedEntry()?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        using RenameShortcutDialog dialog = new(shortcut.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK
            || string.Equals(
                dialog.BaseName,
                Path.GetFileNameWithoutExtension(shortcut.Name),
                StringComparison.Ordinal))
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.RenameAsync(shortcut.Id, dialog.BaseName, _shutdown.Token),
            AppStrings.Renaming,
            showFailureDialog: true);
    }

    private async Task DeleteSelectedAsync()
    {
        if (GetSelectedEntry()?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        if (shortcut.Status == ShortcutStatus.Parked
            && MessageBox.Show(
                this,
                AppStrings.PermanentDeleteConfirmationFormat(shortcut.Name),
                AppStrings.ApplicationTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.DeleteAsync(shortcut.Id, _shutdown.Token),
            shortcut.Status == ShortcutStatus.Parked ? AppStrings.PermanentlyDeleting : AppStrings.MovingToRecycleBin,
            showFailureDialog: true);
    }

    private async Task OpenPropertiesSelectedAsync()
    {
        if (GetSelectedEntry()?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.OpenPropertiesAsync(shortcut.Id, _shutdown.Token),
            AppStrings.OpeningProperties,
            showFailureDialog: true);
    }

    private async Task MakeVisibleSelectedAsync()
    {
        if (GetSelectedEntry()?.Shortcut is not TrackedShortcut shortcut)
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.MakeVisibleAsync(shortcut.Id, _shutdown.Token),
            AppStrings.MakingShortcutVisible,
            showFailureDialog: true);
    }

    public bool CanScanNow => _canManage && !_isBusy;

    public bool CanOpenRecoveryFolder => _canManage;

    public bool CanAddDesktopShortcut => _canManage && !_isBusy && _controller.CanAddDesktopShortcut;

    public Task ScanNowAsync()
    {
        return RunCommandAsync(
            () => _controller.RefreshAsync(_shutdown.Token),
            AppStrings.Scanning);
    }

    public Task AddDesktopShortcutAsync()
    {
        return RunCommandAsync(
            () => _controller.AddDesktopShortcutAsync(_shutdown.Token),
            AppStrings.AddingDesktopShortcut,
            showFailureDialog: true);
    }

    public void OpenRecoveryFolder(bool showFailureDialog = false)
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
            if (showFailureDialog)
            {
                MessageBox.Show(
                    this,
                    AppStrings.RecoveryFolderUnavailable,
                    AppStrings.ApplicationTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else
            {
                AddRuntimeWarning(AppStrings.RecoveryFolderUnavailable);
            }
        }
    }

    public void SetNotificationUnavailable()
    {
        AddRuntimeWarning(AppStrings.NotificationUnavailable);
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

        DataGridViewRow? row = entriesGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(candidate => candidate.Tag is ShortcutListPresentationRow item
                && item.Entry.Shortcut?.Id == id);
        if (row is null)
        {
            _isUpdatingView = true;
            try
            {
                allFilterButton.Checked = true;
                searchTextBox.Clear();
            }
            finally
            {
                _isUpdatingView = false;
            }

            _presentation.SetFilter(ShortcutListFilter.All);
            _presentation.SetSearchText(string.Empty);
            UpdateFilterLabels();
            UpdateFilterButtonColors();
            RefreshGrid(selectedKey: null);
            row = entriesGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(candidate => candidate.Tag is ShortcutListPresentationRow item
                    && item.Entry.Shortcut?.Id == id);
        }

        if (row is not null)
        {
            row.Selected = true;
            entriesGrid.CurrentCell = row.Cells[nameColumn.Index];
            entriesGrid.FirstDisplayedScrollingRowIndex = row.Index;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_shutdownStarted)
        {
            _shutdownStarted = true;
            _controller.StateChanged -= OnControllerStateChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _shutdown.Cancel();
            _shutdown.Dispose();
        }

        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (issuesListBox is not null && mainLayout is not null)
        {
            issuesListBox.Width = Math.Max(400, mainLayout.ClientSize.Width - mainLayout.Padding.Horizontal);
        }
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

        string progressMessage = shortcut.Operation switch
        {
            ShortcutOperation.Creating => AppStrings.AddingDesktopShortcut,
            ShortcutOperation.Parking => AppStrings.Parking,
            ShortcutOperation.Restoring => AppStrings.Restoring,
            ShortcutOperation.Moving => AppStrings.Moving,
            ShortcutOperation.Renaming => AppStrings.Renaming,
            ShortcutOperation.DeletingToRecycleBin => AppStrings.MovingToRecycleBin,
            ShortcutOperation.DeletingPermanently => AppStrings.PermanentlyDeleting,
            _ => AppStrings.Moving,
        };
        await RunCommandAsync(
            () => _controller.RetryPendingOperationAsync(shortcut.Id, _shutdown.Token),
            progressMessage,
            showFailureDialog: true);
    }

    private async Task OpenSettingsAsync()
    {
        if (!_canManage || _isBusy)
        {
            return;
        }

        using SettingsForm settings = new(
            _controller.State.StartAtSignIn,
            _controller.State.StartMinimized);
        if (settings.ShowDialog(this) != DialogResult.OK
            || settings.StartAtSignIn == _controller.State.StartAtSignIn
                && settings.StartMinimized == _controller.State.StartMinimized)
        {
            return;
        }

        await RunCommandAsync(
            () => _controller.SetStartupPreferencesAsync(
                settings.StartAtSignIn,
                settings.StartMinimized,
                _shutdown.Token),
            string.Empty,
            showFailureDialog: true);
    }

    private void ShowMoreMenu()
    {
        if (!moreButton.Enabled)
        {
            return;
        }

        openRecoveryFolderMenuItem.Enabled = _canManage;
        settingsMenuItem.Enabled = _canManage && !_isBusy;
        moreMenu.Show(moreButton, new Point(0, moreButton.Height));
    }

    private void FilterButton_CheckedChanged(RadioButton button, ShortcutListFilter filter)
    {
        if (_isUpdatingView || !button.Checked)
        {
            return;
        }

        _presentation.SetFilter(filter);
        UpdateFilterButtonColors();
        RefreshGrid(GetCurrentSelectionKey());
        UpdateCommandAvailability();
    }

    private void SearchTextBox_TextChanged()
    {
        if (_isUpdatingView)
        {
            return;
        }

        _presentation.SetSearchText(searchTextBox.Text);
        UpdateFilterLabels();
        RefreshGrid(GetCurrentSelectionKey());
        UpdateCommandAvailability();
    }

    private void EntriesGrid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
        {
            return;
        }

        ShortcutListSortColumn column = entriesGrid.Columns[e.ColumnIndex].Name switch
        {
            "locationColumn" => ShortcutListSortColumn.Location,
            "statusColumn" => ShortcutListSortColumn.Status,
            _ => ShortcutListSortColumn.Name,
        };
        _presentation.SortBy(column);
        RefreshGrid(GetCurrentSelectionKey());
    }

    private void RefreshView(string? progressMessage = null)
    {
        if (IsDisposed)
        {
            return;
        }

        ShortcutSelectionKey? selectedKey = GetCurrentSelectionKey();
        _isUpdatingView = true;
        try
        {
            IReadOnlyList<ShortcutListPresentationRow> rows = _controller.GetShortcutList()
                .Select(CreatePresentationRow)
                .ToArray();
            _presentation.SetRows(rows);
            UpdateFilterLabels();
            UpdateFilterButtonColors();
            RefreshGrid(selectedKey);
            UpdateCommandAvailability();
            DisplayStatus(progressMessage, rows.Count);
            DisplayIssues();
        }
        finally
        {
            _isUpdatingView = false;
        }
    }

    private void RefreshGrid(ShortcutSelectionKey? selectedKey)
    {
        bool wasUpdating = _isUpdatingView;
        _isUpdatingView = true;
        try
        {
            entriesGrid.Rows.Clear();
            bool selectionRestored = false;
            foreach (ShortcutListPresentationRow item in _presentation.GetVisibleRows())
            {
                string displayName = item.Entry.IsHidden
                    ? AppStrings.HiddenNameFormat(item.Entry.Name)
                    : item.Entry.Name;
                int index = entriesGrid.Rows.Add(displayName, item.Location, item.Status);
                DataGridViewRow row = entriesGrid.Rows[index];
                row.Tag = item;
                WindowsTheme.ApplyStatusCell(
                    row.Cells[statusColumn.Index],
                    item.NeedsReview,
                    item.Approved,
                    item.Recovery);
                if (selectedKey is ShortcutSelectionKey key && key.Matches(item.Entry))
                {
                    row.Selected = true;
                    entriesGrid.CurrentCell = row.Cells[nameColumn.Index];
                    selectionRestored = true;
                }
            }

            emptyStateLabel.Text = _presentation.TotalCount == 0
                ? AppStrings.NoShortcuts
                : AppStrings.NoMatchingShortcuts;
            bool scanComplete = _controller.ScanIssues.Count == 0
                && _controller.LastFailure is null
                && _controller.StartupFailure is null;
            emptyStateLabel.Visible = entriesGrid.Rows.Count == 0
                && (_presentation.TotalCount > 0 || _canManage && scanComplete);

            if (!selectionRestored)
            {
                entriesGrid.ClearSelection();
                entriesGrid.CurrentCell = null;
            }

            nameColumn.HeaderCell.SortGlyphDirection = _presentation.SortColumn == ShortcutListSortColumn.Name
                ? _presentation.SortDescending ? SortOrder.Descending : SortOrder.Ascending
                : SortOrder.None;
            locationColumn.HeaderCell.SortGlyphDirection = _presentation.SortColumn == ShortcutListSortColumn.Location
                ? _presentation.SortDescending ? SortOrder.Descending : SortOrder.Ascending
                : SortOrder.None;
            statusColumn.HeaderCell.SortGlyphDirection = _presentation.SortColumn == ShortcutListSortColumn.Status
                ? _presentation.SortDescending ? SortOrder.Descending : SortOrder.Ascending
                : SortOrder.None;
        }
        finally
        {
            _isUpdatingView = wasUpdating;
        }
    }

    private ShortcutListPresentationRow CreatePresentationRow(ShortcutListEntry entry)
    {
        string status = GetStatusName(entry);
        TrackedShortcut? shortcut = entry.Shortcut;
        bool recovery = entry.Status == ShortcutStatus.Parked
            || shortcut is not null && (shortcut.Operation != ShortcutOperation.None
                || !_controller.IsManageableShortcut(shortcut.Id));
        bool needsReview = !recovery && (shortcut is null || entry.Status == ShortcutStatus.Pending);
        bool approved = !recovery && entry.Status == ShortcutStatus.ApprovedVisible;
        return new ShortcutListPresentationRow(
            entry,
            GetLocationName(entry),
            status,
            needsReview,
            approved,
            recovery,
            entry.IsHidden);
    }

    private void UpdateFilterLabels()
    {
        allFilterButton.Text = AppStrings.AllFilterFormat(_presentation.CountFor(ShortcutListFilter.All));
        needsReviewFilterButton.Text = AppStrings.NeedsReviewFilterFormat(_presentation.CountFor(ShortcutListFilter.NeedsReview));
        approvedFilterButton.Text = AppStrings.ApprovedFilterFormat(_presentation.CountFor(ShortcutListFilter.Approved));
        recoveryFilterButton.Text = AppStrings.RecoveryFilterFormat(_presentation.CountFor(ShortcutListFilter.Recovery));
    }

    private void UpdateFilterButtonColors()
    {
        foreach (RadioButton button in new[]
                 {
                     allFilterButton,
                     needsReviewFilterButton,
                     approvedFilterButton,
                     recoveryFilterButton,
                 })
        {
            WindowsTheme.ApplyFilterButton(button);
        }
    }

    private void ApplyTheme()
    {
        WindowsTheme.Apply(this);
        WindowsTheme.Apply(moreMenu);
        WindowsTheme.Apply(_shortcutActionsMenu);
        UpdateFilterButtonColors();
        foreach (DataGridViewRow row in entriesGrid.Rows)
        {
            if (row.Tag is ShortcutListPresentationRow item)
            {
                WindowsTheme.ApplyStatusCell(
                    row.Cells[statusColumn.Index],
                    item.NeedsReview,
                    item.Approved,
                    item.Recovery);
            }
        }

        ThemeChanged?.Invoke();
    }

    private void UpdateCommandAvailability()
    {
        ShortcutListPresentationRow? selectedRow = GetSelectedRow();
        ShortcutListEntry? selected = selectedRow?.Entry;
        TrackedShortcut? shortcut = selected?.Shortcut;
        bool manageable = shortcut is not null && _controller.IsManageableShortcut(shortcut.Id);
        bool noOperation = manageable && shortcut!.Operation == ShortcutOperation.None;
        bool activeShortcut = shortcut?.Status is ShortcutStatus.Pending or ShortcutStatus.ApprovedVisible;
        bool canApprove = _canManage && noOperation && shortcut?.Status == ShortcutStatus.Pending;
        bool canPark = _canManage && noOperation && activeShortcut;
        bool canRestore = _canManage && noOperation && shortcut?.Status == ShortcutStatus.Parked;
        bool canMoveToUser = _canManage && noOperation && activeShortcut
            && selected?.RootKind == DesktopRootKind.Public;
        bool canMoveToPublic = _canManage && noOperation && activeShortcut
            && selected?.RootKind == DesktopRootKind.User
            && shortcut is { IsDesktopGuardShortcut: false };
        bool canRetry = _canManage && manageable && shortcut?.Operation != ShortcutOperation.None
            && shortcut is not null
            && _controller.IsOperationUnresolved(shortcut.Id);
        bool canUseShortcutActions = _canManage && noOperation && shortcut is not null;

        SetActionButton(approveButton, canApprove);
        SetActionButton(parkButton, canPark);
        SetActionButton(restoreButton, canRestore);
        SetActionButton(moveToUserButton, canMoveToUser);
        SetActionButton(moveToPublicButton, canMoveToPublic);
        SetActionButton(retryOperationButton, canRetry);
        SetActionButton(_shortcutActionsButton, canUseShortcutActions);
        _renameShortcutMenuItem.Enabled = canUseShortcutActions;
        _deleteShortcutMenuItem.Enabled = canUseShortcutActions;
        _propertiesMenuItem.Enabled = canUseShortcutActions;
        _makeVisibleMenuItem.Enabled = canUseShortcutActions && selectedRow?.Entry.IsHidden == true;

        if (selectedRow is null)
        {
            selectedItemLabel.Text = AppStrings.SelectShortcutForActions;
        }
        else
        {
            selectedItemLabel.Text = AppStrings.SelectedShortcutFormat(selected!.Name, selectedRow.Status);
        }

        scanButton.Enabled = CanScanNow;
        addDesktopShortcutButton.Visible = _canManage && _controller.CanAddDesktopShortcut;
        addDesktopShortcutButton.Enabled = CanAddDesktopShortcut;
        moreButton.Enabled = _canManage;
        CommandAvailabilityChanged?.Invoke();
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

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)
            || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(ApplyTheme));
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    private void DisplayStatus(string? progressMessage, int itemCount)
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

        statusLabel.Text = _controller.ScanIssues.Count == 0
            ? string.Format(AppStrings.ScanCompleteFormat, itemCount)
            : string.Format(AppStrings.ScanCompleteWithIssuesFormat, itemCount, _controller.ScanIssues.Count);
    }

    private void DisplayIssues()
    {
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
        int totalMessageCount = (_controller.LastFailure is null ? 0 : 1)
            + (_controller.StartupFailure is null ? 0 : 1)
            + _runtimeWarnings.Count
            + _controller.ScanIssues.Count;
        issuesPanel.Visible = totalMessageCount > 0;
        if (totalMessageCount == 0)
        {
            _issuesExpanded = false;
            issuesListBox.Visible = false;
            return;
        }

        issuesToggleButton.Text = AppStrings.IssuesCountFormat(totalMessageCount);
        issuesToggleButton.TextAlign = ContentAlignment.MiddleLeft;
        issuesListBox.BeginUpdate();
        issuesListBox.Items.Clear();
        foreach (string message in messages.Take(MaximumDisplayedIssues))
        {
            issuesListBox.Items.Add(message);
        }

        if (totalMessageCount > MaximumDisplayedIssues)
        {
            issuesListBox.Items.Add(string.Format(
                AppStrings.AdditionalIssuesFormat,
                totalMessageCount - MaximumDisplayedIssues));
        }

        issuesListBox.EndUpdate();
        issuesListBox.Visible = _issuesExpanded;
    }

    private void ToggleIssues()
    {
        _issuesExpanded = !_issuesExpanded;
        issuesListBox.Visible = _issuesExpanded;
    }

    private void SetActionButton(Button button, bool canRun)
    {
        button.Visible = canRun;
        button.Enabled = canRun && !_isBusy;
    }

    private ShortcutListEntry? GetSelectedEntry()
    {
        return GetSelectedRow()?.Entry;
    }

    private ShortcutListPresentationRow? GetSelectedRow()
    {
        return entriesGrid.CurrentRow?.Tag as ShortcutListPresentationRow
            ?? entriesGrid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => row.Tag as ShortcutListPresentationRow)
                .FirstOrDefault(row => row is not null);
    }

    private ShortcutSelectionKey? GetCurrentSelectionKey()
    {
        return GetSelectionKey(GetSelectedEntry());
    }

    private static ShortcutSelectionKey? GetSelectionKey(ShortcutListEntry? entry)
    {
        return entry is null
            ? null
            : entry.Shortcut is TrackedShortcut shortcut
                ? new ShortcutSelectionKey(shortcut.Id, entry.RootKind, entry.Name)
                : new ShortcutSelectionKey(null, entry.RootKind, entry.Name);
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
            ShortcutOperation.Renaming => AppStrings.Renaming,
            ShortcutOperation.DeletingToRecycleBin => AppStrings.MovingToRecycleBin,
            ShortcutOperation.DeletingPermanently => AppStrings.PermanentlyDeleting,
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

    private async void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F12 && e.Modifiers == (Keys.Control | Keys.Shift | Keys.Alt))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            MessageBox.Show(
                this,
                EmbeddedContent.Read(),
                AppStrings.ApplicationTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (e.KeyCode == Keys.F && e.Modifiers == Keys.Control)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            searchTextBox.Focus();
            searchTextBox.SelectAll();
            return;
        }

        if (e.Modifiers != Keys.None || !entriesGrid.ContainsFocus)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.F2 when _renameShortcutMenuItem.Enabled && !_isBusy:
                e.Handled = true;
                e.SuppressKeyPress = true;
                await RenameSelectedAsync();
                break;
            case Keys.Delete when _deleteShortcutMenuItem.Enabled && !_isBusy:
                e.Handled = true;
                e.SuppressKeyPress = true;
                await DeleteSelectedAsync();
                break;
        }
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

    private sealed record ShortcutSelectionKey(Guid? Id, DesktopRootKind RootKind, string Name)
    {
        public bool Matches(ShortcutListEntry entry)
        {
            return Id is Guid id
                ? entry.Shortcut?.Id == id
                : entry.Shortcut is null
                    && entry.RootKind == RootKind
                    && string.Equals(entry.Name, Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
