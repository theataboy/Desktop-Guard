using System.ComponentModel;
using DesktopGuard.App.Resources;

namespace DesktopGuard.App;

partial class MainForm
{
    private IContainer components = null!;

    private TableLayoutPanel mainLayout = null!;

    private Label titleLabel = null!;

    private TableLayoutPanel titlePanel = null!;

    private Button aboutButton = null!;

    private Label statusLabel = null!;

    private FlowLayoutPanel commandPanel = null!;

    private Button scanButton = null!;

    private Button addDesktopShortcutButton = null!;

    private Button moreButton = null!;

    private ContextMenuStrip moreMenu = null!;

    private ToolStripMenuItem openRecoveryFolderMenuItem = null!;

    private ToolStripMenuItem settingsMenuItem = null!;

    private FlowLayoutPanel filterPanel = null!;

    private RadioButton allFilterButton = null!;

    private RadioButton needsReviewFilterButton = null!;

    private RadioButton approvedFilterButton = null!;

    private RadioButton recoveryFilterButton = null!;

    private TextBox searchTextBox = null!;

    private Panel entriesPanel = null!;

    private DataGridView entriesGrid = null!;

    private Label emptyStateLabel = null!;

    private DataGridViewTextBoxColumn nameColumn = null!;

    private DataGridViewTextBoxColumn locationColumn = null!;

    private DataGridViewTextBoxColumn statusColumn = null!;

    private FlowLayoutPanel actionPanel = null!;

    private Label selectedItemLabel = null!;

    private Button approveButton = null!;

    private Button parkButton = null!;

    private Button restoreButton = null!;

    private Button moveToUserButton = null!;

    private Button moveToPublicButton = null!;

    private Button retryOperationButton = null!;

    private FlowLayoutPanel issuesPanel = null!;

    private Button issuesToggleButton = null!;

    private ListBox issuesListBox = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        mainLayout = new TableLayoutPanel();
        titlePanel = new TableLayoutPanel();
        titleLabel = new Label();
        aboutButton = new Button();
        statusLabel = new Label();
        commandPanel = new FlowLayoutPanel();
        scanButton = new Button();
        addDesktopShortcutButton = new Button();
        moreButton = new Button();
        moreMenu = new ContextMenuStrip(components);
        openRecoveryFolderMenuItem = new ToolStripMenuItem();
        settingsMenuItem = new ToolStripMenuItem();
        filterPanel = new FlowLayoutPanel();
        allFilterButton = new RadioButton();
        needsReviewFilterButton = new RadioButton();
        approvedFilterButton = new RadioButton();
        recoveryFilterButton = new RadioButton();
        searchTextBox = new TextBox();
        entriesPanel = new Panel();
        entriesGrid = new DataGridView();
        emptyStateLabel = new Label();
        nameColumn = new DataGridViewTextBoxColumn();
        locationColumn = new DataGridViewTextBoxColumn();
        statusColumn = new DataGridViewTextBoxColumn();
        actionPanel = new FlowLayoutPanel();
        selectedItemLabel = new Label();
        approveButton = new Button();
        parkButton = new Button();
        restoreButton = new Button();
        moveToUserButton = new Button();
        moveToPublicButton = new Button();
        retryOperationButton = new Button();
        issuesPanel = new FlowLayoutPanel();
        issuesToggleButton = new Button();
        issuesListBox = new ListBox();
        mainLayout.SuspendLayout();
        titlePanel.SuspendLayout();
        commandPanel.SuspendLayout();
        moreMenu.SuspendLayout();
        filterPanel.SuspendLayout();
        ((ISupportInitialize)entriesGrid).BeginInit();
        actionPanel.SuspendLayout();
        issuesPanel.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout
        //
        mainLayout.ColumnCount = 1;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.Controls.Add(titlePanel, 0, 0);
        mainLayout.Controls.Add(statusLabel, 0, 1);
        mainLayout.Controls.Add(commandPanel, 0, 2);
        mainLayout.Controls.Add(filterPanel, 0, 3);
        mainLayout.Controls.Add(entriesPanel, 0, 4);
        mainLayout.Controls.Add(actionPanel, 0, 5);
        mainLayout.Controls.Add(issuesPanel, 0, 6);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Padding = new Padding(20, 16, 20, 16);
        mainLayout.RowCount = 7;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        //
        // titleLabel
        //
        titleLabel.AutoSize = true;
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point);
        titleLabel.Margin = new Padding(0, 0, 0, 2);
        titleLabel.Name = "titleLabel";
        //
        // titlePanel
        //
        titlePanel.AutoSize = true;
        titlePanel.ColumnCount = 2;
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titlePanel.Controls.Add(titleLabel, 0, 0);
        titlePanel.Controls.Add(aboutButton, 1, 0);
        titlePanel.Dock = DockStyle.Fill;
        titlePanel.Margin = new Padding(0, 0, 0, 2);
        titlePanel.Name = "titlePanel";
        titlePanel.RowCount = 1;
        titlePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        //
        // aboutButton
        //
        aboutButton.AccessibleName = AppStrings.AboutButtonAccessibleName;
        aboutButton.AutoSize = false;
        aboutButton.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point);
        aboutButton.Margin = new Padding(4, 0, 0, 2);
        aboutButton.Name = "aboutButton";
        aboutButton.Size = new Size(30, 30);
        aboutButton.TabIndex = 1;
        aboutButton.Text = "i";
        aboutButton.UseVisualStyleBackColor = true;
        //
        // statusLabel
        //
        statusLabel.AutoEllipsis = true;
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.MaximumSize = new Size(1200, 0);
        statusLabel.Margin = new Padding(0, 0, 0, 14);
        statusLabel.Name = "statusLabel";
        //
        // commandPanel
        //
        commandPanel.AutoSize = true;
        commandPanel.Controls.Add(scanButton);
        commandPanel.Controls.Add(addDesktopShortcutButton);
        commandPanel.Controls.Add(moreButton);
        commandPanel.Dock = DockStyle.Fill;
        commandPanel.FlowDirection = FlowDirection.LeftToRight;
        commandPanel.Margin = new Padding(0, 0, 0, 12);
        commandPanel.Name = "commandPanel";
        commandPanel.WrapContents = true;
        //
        // scanButton
        //
        scanButton.AutoSize = true;
        scanButton.Margin = new Padding(0, 0, 8, 4);
        scanButton.Name = "scanButton";
        scanButton.UseVisualStyleBackColor = true;
        //
        // addDesktopShortcutButton
        //
        addDesktopShortcutButton.AutoSize = true;
        addDesktopShortcutButton.Margin = new Padding(0, 0, 8, 4);
        addDesktopShortcutButton.Name = "addDesktopShortcutButton";
        addDesktopShortcutButton.UseVisualStyleBackColor = true;
        //
        // moreButton
        //
        moreButton.AutoSize = true;
        moreButton.Margin = new Padding(0, 0, 8, 4);
        moreButton.Name = "moreButton";
        moreButton.UseVisualStyleBackColor = true;
        //
        // moreMenu
        //
        moreMenu.Items.AddRange([openRecoveryFolderMenuItem, settingsMenuItem]);
        moreMenu.Name = "moreMenu";
        moreMenu.Size = new Size(190, 48);
        //
        // openRecoveryFolderMenuItem
        //
        openRecoveryFolderMenuItem.Name = "openRecoveryFolderMenuItem";
        openRecoveryFolderMenuItem.Size = new Size(189, 22);
        //
        // settingsMenuItem
        //
        settingsMenuItem.Name = "settingsMenuItem";
        settingsMenuItem.Size = new Size(189, 22);
        //
        // filterPanel
        //
        filterPanel.AutoSize = true;
        filterPanel.Controls.Add(allFilterButton);
        filterPanel.Controls.Add(needsReviewFilterButton);
        filterPanel.Controls.Add(approvedFilterButton);
        filterPanel.Controls.Add(recoveryFilterButton);
        filterPanel.Controls.Add(searchTextBox);
        filterPanel.Dock = DockStyle.Fill;
        filterPanel.FlowDirection = FlowDirection.LeftToRight;
        filterPanel.Margin = new Padding(0, 0, 0, 8);
        filterPanel.Name = "filterPanel";
        filterPanel.WrapContents = true;
        //
        // filter buttons
        //
        allFilterButton.Appearance = Appearance.Button;
        allFilterButton.AutoSize = true;
        allFilterButton.Checked = true;
        allFilterButton.FlatStyle = FlatStyle.Flat;
        allFilterButton.Margin = new Padding(0, 0, 6, 4);
        allFilterButton.Name = "allFilterButton";
        allFilterButton.UseVisualStyleBackColor = true;
        needsReviewFilterButton.Appearance = Appearance.Button;
        needsReviewFilterButton.AutoSize = true;
        needsReviewFilterButton.FlatStyle = FlatStyle.Flat;
        needsReviewFilterButton.Margin = new Padding(0, 0, 6, 4);
        needsReviewFilterButton.Name = "needsReviewFilterButton";
        needsReviewFilterButton.UseVisualStyleBackColor = true;
        approvedFilterButton.Appearance = Appearance.Button;
        approvedFilterButton.AutoSize = true;
        approvedFilterButton.FlatStyle = FlatStyle.Flat;
        approvedFilterButton.Margin = new Padding(0, 0, 6, 4);
        approvedFilterButton.Name = "approvedFilterButton";
        approvedFilterButton.UseVisualStyleBackColor = true;
        recoveryFilterButton.Appearance = Appearance.Button;
        recoveryFilterButton.AutoSize = true;
        recoveryFilterButton.FlatStyle = FlatStyle.Flat;
        recoveryFilterButton.Margin = new Padding(0, 0, 12, 4);
        recoveryFilterButton.Name = "recoveryFilterButton";
        recoveryFilterButton.UseVisualStyleBackColor = true;
        //
        // searchTextBox
        //
        searchTextBox.Margin = new Padding(0, 0, 0, 4);
        searchTextBox.Name = "searchTextBox";
        searchTextBox.PlaceholderText = "";
        searchTextBox.Size = new Size(250, 23);
        //
        // entriesGrid
        //
        entriesGrid.AllowUserToAddRows = false;
        entriesGrid.AllowUserToDeleteRows = false;
        entriesGrid.AllowUserToResizeRows = false;
        entriesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        entriesGrid.BackgroundColor = SystemColors.Window;
        entriesGrid.BorderStyle = BorderStyle.FixedSingle;
        entriesGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        entriesGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        entriesGrid.Columns.AddRange([nameColumn, locationColumn, statusColumn]);
        entriesGrid.Dock = DockStyle.Fill;
        entriesGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        entriesGrid.MultiSelect = false;
        entriesGrid.Name = "entriesGrid";
        entriesGrid.ReadOnly = true;
        entriesGrid.RowHeadersVisible = false;
        entriesGrid.RowTemplate.Height = 30;
        entriesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        entriesGrid.ShowCellToolTips = false;
        entriesPanel.Controls.Add(entriesGrid);
        entriesPanel.Controls.Add(emptyStateLabel);
        entriesPanel.Dock = DockStyle.Fill;
        entriesPanel.Name = "entriesPanel";
        //
        // emptyStateLabel
        //
        emptyStateLabel.BackColor = Color.Transparent;
        emptyStateLabel.Dock = DockStyle.Fill;
        emptyStateLabel.Name = "emptyStateLabel";
        emptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
        emptyStateLabel.Visible = false;
        emptyStateLabel.BringToFront();
        //
        // columns
        //
        nameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        nameColumn.FillWeight = 52F;
        nameColumn.MinimumWidth = 220;
        nameColumn.Name = "nameColumn";
        nameColumn.ReadOnly = true;
        nameColumn.SortMode = DataGridViewColumnSortMode.Programmatic;
        locationColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        locationColumn.FillWeight = 23F;
        locationColumn.MinimumWidth = 130;
        locationColumn.Name = "locationColumn";
        locationColumn.ReadOnly = true;
        locationColumn.SortMode = DataGridViewColumnSortMode.Programmatic;
        statusColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        statusColumn.FillWeight = 25F;
        statusColumn.MinimumWidth = 150;
        statusColumn.Name = "statusColumn";
        statusColumn.ReadOnly = true;
        statusColumn.SortMode = DataGridViewColumnSortMode.Programmatic;
        //
        // actionPanel
        //
        actionPanel.AutoSize = true;
        actionPanel.Controls.Add(selectedItemLabel);
        actionPanel.Controls.Add(approveButton);
        actionPanel.Controls.Add(parkButton);
        actionPanel.Controls.Add(restoreButton);
        actionPanel.Controls.Add(moveToUserButton);
        actionPanel.Controls.Add(moveToPublicButton);
        actionPanel.Controls.Add(retryOperationButton);
        actionPanel.Dock = DockStyle.Fill;
        actionPanel.FlowDirection = FlowDirection.LeftToRight;
        actionPanel.Margin = new Padding(0, 10, 0, 4);
        actionPanel.Name = "actionPanel";
        actionPanel.WrapContents = true;
        //
        // selectedItemLabel
        //
        selectedItemLabel.AutoEllipsis = true;
        selectedItemLabel.AutoSize = false;
        selectedItemLabel.Margin = new Padding(0, 7, 12, 4);
        selectedItemLabel.MinimumSize = new Size(200, 0);
        selectedItemLabel.Name = "selectedItemLabel";
        selectedItemLabel.Size = new Size(260, 24);
        selectedItemLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // action buttons
        //
        approveButton.AutoSize = true;
        approveButton.Margin = new Padding(0, 0, 6, 4);
        approveButton.Name = "approveButton";
        approveButton.UseVisualStyleBackColor = true;
        parkButton.AutoSize = true;
        parkButton.Margin = new Padding(0, 0, 6, 4);
        parkButton.Name = "parkButton";
        parkButton.UseVisualStyleBackColor = true;
        restoreButton.AutoSize = true;
        restoreButton.Margin = new Padding(0, 0, 6, 4);
        restoreButton.Name = "restoreButton";
        restoreButton.UseVisualStyleBackColor = true;
        moveToUserButton.AutoSize = true;
        moveToUserButton.Margin = new Padding(0, 0, 6, 4);
        moveToUserButton.Name = "moveToUserButton";
        moveToUserButton.UseVisualStyleBackColor = true;
        moveToPublicButton.AutoSize = true;
        moveToPublicButton.Margin = new Padding(0, 0, 6, 4);
        moveToPublicButton.Name = "moveToPublicButton";
        moveToPublicButton.UseVisualStyleBackColor = true;
        retryOperationButton.AutoSize = true;
        retryOperationButton.Margin = new Padding(0, 0, 6, 4);
        retryOperationButton.Name = "retryOperationButton";
        retryOperationButton.UseVisualStyleBackColor = true;
        //
        // issuesPanel
        //
        issuesPanel.AutoSize = true;
        issuesPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        issuesPanel.Controls.Add(issuesToggleButton);
        issuesPanel.Controls.Add(issuesListBox);
        issuesPanel.Dock = DockStyle.Fill;
        issuesPanel.FlowDirection = FlowDirection.TopDown;
        issuesPanel.Margin = new Padding(0, 2, 0, 0);
        issuesPanel.Name = "issuesPanel";
        issuesPanel.WrapContents = false;
        //
        // issuesToggleButton
        //
        issuesToggleButton.AutoSize = true;
        issuesToggleButton.FlatAppearance.BorderSize = 0;
        issuesToggleButton.FlatStyle = FlatStyle.Flat;
        issuesToggleButton.Margin = new Padding(0, 0, 0, 4);
        issuesToggleButton.Name = "issuesToggleButton";
        issuesToggleButton.UseVisualStyleBackColor = true;
        //
        // issuesListBox
        //
        issuesListBox.BorderStyle = BorderStyle.FixedSingle;
        issuesListBox.IntegralHeight = false;
        issuesListBox.Margin = new Padding(0);
        issuesListBox.Name = "issuesListBox";
        issuesListBox.ScrollAlwaysVisible = true;
        issuesListBox.Size = new Size(800, 96);
        issuesListBox.Visible = false;
        //
        // MainForm
        //
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1040, 720);
        Controls.Add(mainLayout);
        MinimumSize = new Size(820, 560);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        titlePanel.ResumeLayout(false);
        titlePanel.PerformLayout();
        commandPanel.ResumeLayout(false);
        commandPanel.PerformLayout();
        moreMenu.ResumeLayout(false);
        filterPanel.ResumeLayout(false);
        filterPanel.PerformLayout();
        ((ISupportInitialize)entriesGrid).EndInit();
        actionPanel.ResumeLayout(false);
        actionPanel.PerformLayout();
        issuesPanel.ResumeLayout(false);
        issuesPanel.PerformLayout();
        ResumeLayout(false);
    }
}
