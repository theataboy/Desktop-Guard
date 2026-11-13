using DesktopGuard.App.Resources;

namespace DesktopGuard.App;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _startAtSignInCheckBox;

    public SettingsForm(bool startAtSignIn)
    {
        Text = AppStrings.Settings;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(430, 190);
        Padding = new Padding(20);

        TableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label descriptionLabel = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(390, 0),
            Text = AppStrings.SettingsDescription,
            Margin = new Padding(0, 0, 0, 14),
        };
        _startAtSignInCheckBox = new CheckBox
        {
            AutoSize = true,
            Checked = startAtSignIn,
            Dock = DockStyle.Top,
            Text = AppStrings.StartAtSignIn,
            Margin = new Padding(0, 4, 0, 12),
        };
        FlowLayoutPanel buttonPanel = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        Button saveButton = new()
        {
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Text = AppStrings.Save,
            UseVisualStyleBackColor = true,
        };
        Button cancelButton = new()
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = AppStrings.Cancel,
            UseVisualStyleBackColor = true,
        };
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        layout.Controls.Add(descriptionLabel, 0, 0);
        layout.Controls.Add(_startAtSignInCheckBox, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 2);
        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        WindowsTheme.Apply(this);
    }

    public bool StartAtSignIn => _startAtSignInCheckBox.Checked;
}
