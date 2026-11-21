using DesktopGuard.App.Resources;

namespace DesktopGuard.App.Presentation;

internal sealed class LicenseDialog : Form
{
    private readonly TextBox _licenseTextBox;

    public LicenseDialog(string licenseText)
    {
        ArgumentNullException.ThrowIfNull(licenseText);

        Text = AppStrings.LicenseDialogTitle;
        Name = nameof(LicenseDialog);
        AccessibleName = AppStrings.LicenseDialogTitle;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = true;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(720, 480);
        MinimumSize = new Size(560, 360);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
            ?? SystemIcons.Application;

        TableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _licenseTextBox = new TextBox
        {
            AccessibleName = AppStrings.LicenseTextAccessibleName,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point),
            Multiline = true,
            Name = "licenseTextBox",
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            TabIndex = 0,
            Text = NormalizeLineEndings(licenseText),
            WordWrap = false,
        };
        layout.Controls.Add(_licenseTextBox, 0, 0);

        Button closeButton = new()
        {
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Name = "closeButton",
            Text = AppStrings.CloseButtonText,
            TabIndex = 0,
        };
        FlowLayoutPanel buttonPanel = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 10, 0, 0),
            TabIndex = 1,
            WrapContents = false,
        };
        buttonPanel.Controls.Add(closeButton);
        layout.Controls.Add(buttonPanel, 0, 1);

        AcceptButton = closeButton;
        CancelButton = closeButton;
        Controls.Add(layout);
        WindowsTheme.Apply(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _licenseTextBox.Select(0, 0);
    }

    private static string NormalizeLineEndings(string text)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return normalized.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }
}
