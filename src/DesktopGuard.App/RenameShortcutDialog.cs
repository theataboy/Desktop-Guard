using DesktopGuard.App.Resources;

namespace DesktopGuard.App;

internal sealed class RenameShortcutDialog : Form
{
    private readonly TextBox _nameTextBox = new();

    public RenameShortcutDialog(string currentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        string extension = Path.GetExtension(currentName);

        Text = AppStrings.RenameShortcutTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 156);

        Label prompt = new()
        {
            AutoSize = true,
            Text = AppStrings.RenameShortcutPrompt,
            Margin = new Padding(0, 0, 0, 12),
        };
        Label extensionLabel = new()
        {
            AutoSize = true,
            Text = $"{AppStrings.RenameShortcutExtensionLabel}: {extension}",
            Anchor = AnchorStyles.Left,
            Margin = new Padding(10, 0, 0, 0),
        };
        _nameTextBox.Dock = DockStyle.Fill;
        _nameTextBox.Text = Path.GetFileNameWithoutExtension(currentName);
        _nameTextBox.SelectAll();

        Button okButton = new()
        {
            AutoSize = true,
            Text = AppStrings.Save,
            DialogResult = DialogResult.OK,
            MinimumSize = new Size(84, 0),
        };
        Button cancelButton = new()
        {
            AutoSize = true,
            Text = AppStrings.Cancel,
            DialogResult = DialogResult.Cancel,
            MinimumSize = new Size(84, 0),
        };
        FlowLayoutPanel buttons = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        TableLayoutPanel layout = new()
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(prompt, 0, 0);
        layout.SetColumnSpan(prompt, 2);
        layout.Controls.Add(_nameTextBox, 0, 1);
        layout.Controls.Add(extensionLabel, 1, 1);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        WindowsTheme.Apply(this);
    }

    public string BaseName => _nameTextBox.Text;
}
