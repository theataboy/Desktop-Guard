using System.ComponentModel;

namespace DesktopGuard.App;

partial class MainForm
{
    private IContainer components = null!;

    private Button scanButton = null!;

    private Button approveButton = null!;

    private Button parkButton = null!;

    private Button restoreButton = null!;

    private Button moveToUserButton = null!;

    private Button moveToPublicButton = null!;

    private Button addDesktopShortcutButton = null!;

    private Button retryOperationButton = null!;

    private Button openRecoveryFolderButton = null!;

    private CheckBox startAtSignInCheckBox = null!;

    private Label statusLabel = null!;

    private ListView entriesListView = null!;

    private ColumnHeader nameColumn = null!;

    private ColumnHeader locationColumn = null!;

    private ColumnHeader statusColumn = null!;

    private GroupBox issuesGroupBox = null!;

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
        TableLayoutPanel mainLayout = new();
        FlowLayoutPanel commandPanel = new();
        scanButton = new Button();
        approveButton = new Button();
        parkButton = new Button();
        restoreButton = new Button();
        moveToUserButton = new Button();
        moveToPublicButton = new Button();
        addDesktopShortcutButton = new Button();
        retryOperationButton = new Button();
        openRecoveryFolderButton = new Button();
        startAtSignInCheckBox = new CheckBox();
        statusLabel = new Label();
        entriesListView = new ListView();
        nameColumn = new ColumnHeader();
        locationColumn = new ColumnHeader();
        statusColumn = new ColumnHeader();
        issuesGroupBox = new GroupBox();
        issuesListBox = new ListBox();
        mainLayout.SuspendLayout();
        commandPanel.SuspendLayout();
        issuesGroupBox.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout
        //
        mainLayout.ColumnCount = 1;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.Controls.Add(commandPanel, 0, 0);
        mainLayout.Controls.Add(statusLabel, 0, 1);
        mainLayout.Controls.Add(entriesListView, 0, 2);
        mainLayout.Controls.Add(issuesGroupBox, 0, 3);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Padding = new Padding(12);
        mainLayout.RowCount = 4;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F));
        //
        // commandPanel
        //
        commandPanel.AutoSize = true;
        commandPanel.Controls.Add(scanButton);
        commandPanel.Controls.Add(approveButton);
        commandPanel.Controls.Add(parkButton);
        commandPanel.Controls.Add(restoreButton);
        commandPanel.Controls.Add(moveToUserButton);
        commandPanel.Controls.Add(moveToPublicButton);
        commandPanel.Controls.Add(addDesktopShortcutButton);
        commandPanel.Controls.Add(retryOperationButton);
        commandPanel.Controls.Add(openRecoveryFolderButton);
        commandPanel.Controls.Add(startAtSignInCheckBox);
        commandPanel.Dock = DockStyle.Fill;
        commandPanel.FlowDirection = FlowDirection.LeftToRight;
        commandPanel.Margin = new Padding(0, 0, 0, 8);
        commandPanel.WrapContents = true;
        //
        // buttons and start option
        //
        scanButton.AutoSize = true;
        scanButton.Margin = new Padding(0, 0, 8, 4);
        scanButton.UseVisualStyleBackColor = true;
        approveButton.AutoSize = true;
        approveButton.Margin = new Padding(0, 0, 8, 4);
        approveButton.UseVisualStyleBackColor = true;
        parkButton.AutoSize = true;
        parkButton.Margin = new Padding(0, 0, 8, 4);
        parkButton.UseVisualStyleBackColor = true;
        restoreButton.AutoSize = true;
        restoreButton.Margin = new Padding(0, 0, 16, 4);
        restoreButton.UseVisualStyleBackColor = true;
        moveToUserButton.AutoSize = true;
        moveToUserButton.Margin = new Padding(0, 0, 8, 4);
        moveToUserButton.UseVisualStyleBackColor = true;
        moveToPublicButton.AutoSize = true;
        moveToPublicButton.Margin = new Padding(0, 0, 8, 4);
        moveToPublicButton.UseVisualStyleBackColor = true;
        addDesktopShortcutButton.AutoSize = true;
        addDesktopShortcutButton.Margin = new Padding(0, 0, 8, 4);
        addDesktopShortcutButton.UseVisualStyleBackColor = true;
        retryOperationButton.AutoSize = true;
        retryOperationButton.Margin = new Padding(0, 0, 8, 4);
        retryOperationButton.UseVisualStyleBackColor = true;
        openRecoveryFolderButton.AutoSize = true;
        openRecoveryFolderButton.Margin = new Padding(0, 0, 16, 4);
        openRecoveryFolderButton.UseVisualStyleBackColor = true;
        startAtSignInCheckBox.AutoSize = true;
        startAtSignInCheckBox.Margin = new Padding(0, 6, 0, 4);
        startAtSignInCheckBox.UseVisualStyleBackColor = true;
        //
        // statusLabel
        //
        statusLabel.AutoEllipsis = true;
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Margin = new Padding(0, 0, 0, 8);
        statusLabel.MaximumSize = new Size(1000, 0);
        //
        // entriesListView
        //
        entriesListView.Columns.AddRange([nameColumn, locationColumn, statusColumn]);
        entriesListView.CheckBoxes = true;
        entriesListView.Dock = DockStyle.Fill;
        entriesListView.FullRowSelect = true;
        entriesListView.HideSelection = false;
        entriesListView.MultiSelect = false;
        entriesListView.UseCompatibleStateImageBehavior = false;
        entriesListView.View = View.Details;
        nameColumn.Width = 450;
        locationColumn.Width = 190;
        statusColumn.Width = 220;
        //
        // issuesGroupBox
        //
        issuesGroupBox.Controls.Add(issuesListBox);
        issuesGroupBox.Dock = DockStyle.Fill;
        issuesGroupBox.Padding = new Padding(8);
        //
        // issuesListBox
        //
        issuesListBox.BorderStyle = BorderStyle.None;
        issuesListBox.Dock = DockStyle.Fill;
        issuesListBox.IntegralHeight = false;
        issuesListBox.ScrollAlwaysVisible = true;
        //
        // MainForm
        //
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(940, 620);
        Controls.Add(mainLayout);
        MinimumSize = new Size(760, 480);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        commandPanel.ResumeLayout(false);
        commandPanel.PerformLayout();
        issuesGroupBox.ResumeLayout(false);
        ResumeLayout(false);
    }
}
