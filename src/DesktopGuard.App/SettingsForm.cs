using DesktopGuard.App.Resources;

namespace DesktopGuard.App;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _startAtSignInCheckBox;
    private readonly CheckBox _startMinimizedCheckBox;
    private readonly NumericUpDown _autoParkDelayMinutesInput;
    private readonly Button _applyButton;
    private bool _savedStartAtSignIn;
    private bool _savedStartMinimized;
    private int _savedAutoParkDelayMinutes;
    private bool _cancelButtonSelected;

    public event Func<Task>? ApplyRequested;

    public SettingsForm(bool startAtSignIn, bool startMinimized, int autoParkDelayMinutes)
    {
        Text = AppStrings.Settings;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(520, 360);
        Padding = new Padding(20);

        TableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 4,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label descriptionLabel = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(460, 0),
            Text = AppStrings.SettingsDescription,
            Margin = new Padding(0, 0, 0, 14),
        };
        _startAtSignInCheckBox = new CheckBox
        {
            AutoSize = true,
            Checked = startAtSignIn,
            Text = AppStrings.StartAtSignIn,
            Margin = new Padding(0, 4, 0, 8),
        };
        _startMinimizedCheckBox = new CheckBox
        {
            AutoSize = true,
            Checked = startMinimized,
            Text = AppStrings.StartMinimized,
            Margin = new Padding(0, 4, 0, 12),
        };
        Label autoParkDelayLabel = new()
        {
            AutoSize = true,
            Text = AppStrings.AutoParkDelayMinutes,
            Margin = new Padding(0, 7, 8, 0),
        };
        _autoParkDelayMinutesInput = new NumericUpDown
        {
            Minimum = 2,
            Maximum = 30,
            Value = Math.Clamp(autoParkDelayMinutes, 2, 30),
            DecimalPlaces = 0,
            Increment = 1,
            Width = 68,
            Margin = new Padding(0, 3, 0, 10),
        };
        FlowLayoutPanel autoParkDelayPanel = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            WrapContents = false,
        };
        autoParkDelayPanel.Controls.Add(autoParkDelayLabel);
        autoParkDelayPanel.Controls.Add(_autoParkDelayMinutesInput);
        FlowLayoutPanel optionsPanel = new()
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            WrapContents = false,
        };
        optionsPanel.Controls.Add(_startAtSignInCheckBox);
        optionsPanel.Controls.Add(_startMinimizedCheckBox);
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
            Text = AppStrings.SaveSettingsButton,
            UseVisualStyleBackColor = true,
        };
        Button cancelButton = new()
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = AppStrings.CancelSettingsButton,
            UseVisualStyleBackColor = true,
        };
        _applyButton = new Button
        {
            AutoSize = true,
            Enabled = false,
            Text = AppStrings.ApplySettingsButton,
            UseVisualStyleBackColor = true,
        };
        _applyButton.Click += ApplyButton_Click;
        cancelButton.Click += (_, _) => _cancelButtonSelected = true;
        _startAtSignInCheckBox.CheckedChanged += (_, _) => UpdateApplyButton();
        _startMinimizedCheckBox.CheckedChanged += (_, _) => UpdateApplyButton();
        _autoParkDelayMinutesInput.ValueChanged += (_, _) => UpdateApplyButton();
        buttonPanel.Controls.Add(_applyButton);
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        layout.Controls.Add(descriptionLabel, 0, 0);
        layout.Controls.Add(optionsPanel, 0, 1);
        layout.Controls.Add(autoParkDelayPanel, 0, 2);
        layout.Controls.Add(buttonPanel, 0, 3);
        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        _savedStartAtSignIn = StartAtSignIn;
        _savedStartMinimized = StartMinimized;
        _savedAutoParkDelayMinutes = AutoParkDelayMinutes;
        WindowsTheme.Apply(this);
    }

    public bool StartAtSignIn => _startAtSignInCheckBox.Checked;

    public bool StartMinimized => _startMinimizedCheckBox.Checked;

    public int AutoParkDelayMinutes => decimal.ToInt32(_autoParkDelayMinutesInput.Value);

    public bool HasChanges => StartAtSignIn != _savedStartAtSignIn
        || StartMinimized != _savedStartMinimized
        || AutoParkDelayMinutes != _savedAutoParkDelayMinutes;

    public void MarkChangesSaved(bool startAtSignIn, bool startMinimized, int autoParkDelayMinutes)
    {
        _savedStartAtSignIn = startAtSignIn;
        _savedStartMinimized = startMinimized;
        _savedAutoParkDelayMinutes = autoParkDelayMinutes;
        UpdateApplyButton();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            AcceptButton?.PerformClick();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing
            && DialogResult != DialogResult.OK
            && !_cancelButtonSelected
            && HasChanges)
        {
            DialogResult choice = MessageBox.Show(
                this,
                AppStrings.SaveSettingsBeforeClosing,
                AppStrings.Settings,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (choice == DialogResult.Yes)
            {
                DialogResult = DialogResult.OK;
            }
            else if (choice == DialogResult.No)
            {
                _cancelButtonSelected = true;
                DialogResult = DialogResult.Cancel;
            }
            else
            {
                DialogResult = System.Windows.Forms.DialogResult.None;
                e.Cancel = true;
            }
        }

        base.OnFormClosing(e);
    }

    private async void ApplyButton_Click(object? sender, EventArgs e)
    {
        Func<Task>? applyRequested = ApplyRequested;
        if (applyRequested is null || !HasChanges)
        {
            return;
        }

        _applyButton.Enabled = false;
        try
        {
            await applyRequested();
        }
        catch (Exception)
        {
            MessageBox.Show(
                this,
                AppStrings.OperationFailure,
                AppStrings.Settings,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateApplyButton();
        }
    }

    private void UpdateApplyButton()
    {
        _applyButton.Enabled = HasChanges;
    }
}
