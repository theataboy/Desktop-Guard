using Microsoft.Win32;

namespace DesktopGuard.App;

internal static class WindowsTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static ThemePalette _currentPalette;
    private static bool _hasCurrentPalette;

    public static void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ThemePalette palette = GetPalette();
        SetCurrentPalette(palette);
        ApplyToControl(root, palette);
        ApplyToChildren(root, palette);
    }

    public static void Apply(ToolStrip toolStrip)
    {
        ArgumentNullException.ThrowIfNull(toolStrip);
        ThemePalette palette = GetPalette();
        SetCurrentPalette(palette);
        toolStrip.BackColor = palette.Surface;
        toolStrip.ForeColor = palette.Text;
        toolStrip.RenderMode = ToolStripRenderMode.Professional;
        toolStrip.Renderer = new ThemeRenderer(palette);
        foreach (ToolStripItem item in toolStrip.Items)
        {
            item.BackColor = palette.Surface;
            item.ForeColor = palette.Text;
        }
    }

    public static void ApplyFilterButton(RadioButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        ApplyFilterButton(button, GetCurrentPalette());
    }

    public static void ApplyStatusCell(DataGridViewCell cell, bool needsReview, bool approved, bool recovery)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ThemePalette palette = GetCurrentPalette();
        (Color background, Color foreground) = needsReview
            ? (palette.ReviewBackground, palette.ReviewForeground)
            : approved
                ? (palette.ApprovedBackground, palette.ApprovedForeground)
                : recovery
                    ? (palette.RecoveryBackground, palette.RecoveryForeground)
                    : (palette.Surface, palette.Text);
        cell.Style.BackColor = background;
        cell.Style.ForeColor = foreground;
    }

    private static void ApplyFilterButton(RadioButton button, ThemePalette palette)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = palette.Border;
        button.BackColor = button.Checked ? palette.Accent : palette.Form;
        button.ForeColor = button.Checked ? palette.AccentText : palette.Text;
    }

    private static ThemePalette GetCurrentPalette()
    {
        return _hasCurrentPalette ? _currentPalette : GetPalette();
    }

    private static void SetCurrentPalette(ThemePalette palette)
    {
        _currentPalette = palette;
        _hasCurrentPalette = true;
    }

    private static ThemePalette GetPalette()
    {
        if (SystemInformation.HighContrast)
        {
            return new ThemePalette(
                SystemColors.Window,
                SystemColors.Control,
                SystemColors.Control,
                SystemColors.WindowText,
                SystemColors.ControlText,
                SystemColors.GrayText,
                SystemColors.Highlight,
                SystemColors.HighlightText,
                SystemColors.Highlight,
                SystemColors.HighlightText,
                SystemColors.ControlDark,
                SystemColors.Control,
                SystemColors.ControlText,
                SystemColors.Control,
                SystemColors.ControlText,
                SystemColors.Control,
                SystemColors.ControlText);
        }

        bool isLight = IsLightTheme();
        return isLight
            ? new ThemePalette(
                Color.FromArgb(246, 247, 249),
                Color.White,
                Color.FromArgb(239, 242, 246),
                Color.FromArgb(31, 35, 40),
                Color.FromArgb(31, 35, 40),
                Color.FromArgb(93, 100, 109),
                Color.FromArgb(0, 103, 192),
                Color.White,
                Color.FromArgb(232, 241, 251),
                Color.FromArgb(20, 49, 80),
                Color.FromArgb(218, 223, 230),
                Color.FromArgb(255, 244, 209),
                Color.FromArgb(128, 83, 0),
                Color.FromArgb(230, 245, 236),
                Color.FromArgb(28, 99, 63),
                Color.FromArgb(253, 236, 232),
                Color.FromArgb(154, 50, 31))
            : new ThemePalette(
                Color.FromArgb(30, 31, 34),
                Color.FromArgb(37, 39, 43),
                Color.FromArgb(45, 48, 53),
                Color.FromArgb(242, 243, 245),
                Color.FromArgb(235, 237, 240),
                Color.FromArgb(166, 172, 181),
                Color.FromArgb(54, 133, 217),
                Color.White,
                Color.FromArgb(45, 68, 91),
                Color.FromArgb(226, 237, 249),
                Color.FromArgb(65, 69, 76),
                Color.FromArgb(67, 55, 31),
                Color.FromArgb(241, 201, 107),
                Color.FromArgb(32, 58, 43),
                Color.FromArgb(114, 217, 155),
                Color.FromArgb(71, 41, 35),
                Color.FromArgb(255, 152, 135));
    }

    private static bool IsLightTheme()
    {
        try
        {
            using RegistryKey? personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            object? value = personalize?.GetValue("AppsUseLightTheme");
            return value switch
            {
                int setting => setting != 0,
                _ => true,
            };
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }

    private static void ApplyToChildren(Control parent, ThemePalette palette)
    {
        foreach (Control child in parent.Controls)
        {
            ApplyToControl(child, palette);
            ApplyToChildren(child, palette);
        }
    }

    private static void ApplyToControl(Control control, ThemePalette palette)
    {
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = palette.Surface;
            grid.GridColor = palette.Border;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.SubtleSurface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.SubtleSurface;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
            grid.DefaultCellStyle.BackColor = palette.Surface;
            grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = palette.SubtleSurface;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
            grid.RowHeadersDefaultCellStyle.BackColor = palette.SubtleSurface;
            grid.RowHeadersDefaultCellStyle.ForeColor = palette.Text;
            return;
        }

        if (control is Button button)
        {
            bool primary = button.Name == "scanButton";
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderColor = primary ? palette.Accent : palette.Border;
            button.BackColor = primary ? palette.Accent : palette.SubtleSurface;
            button.ForeColor = primary ? palette.AccentText : palette.Text;
            return;
        }

        if (control is TextBox textBox)
        {
            textBox.BackColor = palette.Surface;
            textBox.ForeColor = palette.Text;
            return;
        }

        if (control is Label label)
        {
            label.ForeColor = label.Name == "statusLabel" ? palette.MutedText : palette.Text;
            label.BackColor = Color.Transparent;
            return;
        }

        if (control is CheckBox checkBox)
        {
            checkBox.BackColor = Color.Transparent;
            checkBox.ForeColor = palette.ControlText;
            return;
        }

        if (control is RadioButton radioButton)
        {
            ApplyFilterButton(radioButton);
            return;
        }

        control.BackColor = control is Form ? palette.Form : palette.Surface;
        control.ForeColor = palette.ControlText;
    }

    private readonly record struct ThemePalette(
        Color Form,
        Color Surface,
        Color SubtleSurface,
        Color Text,
        Color ControlText,
        Color MutedText,
        Color Accent,
        Color AccentText,
        Color Selection,
        Color SelectionText,
        Color Border,
        Color ReviewBackground,
        Color ReviewForeground,
        Color ApprovedBackground,
        Color ApprovedForeground,
        Color RecoveryBackground,
        Color RecoveryForeground);

    private sealed class ThemeColorTable(ThemePalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Surface;

        public override Color MenuBorder => palette.Border;

        public override Color MenuItemSelected => palette.Selection;

        public override Color MenuItemSelectedGradientBegin => palette.Selection;

        public override Color MenuItemSelectedGradientEnd => palette.Selection;

        public override Color MenuItemPressedGradientBegin => palette.Selection;

        public override Color MenuItemPressedGradientEnd => palette.Selection;

        public override Color ImageMarginGradientBegin => palette.Surface;

        public override Color ImageMarginGradientMiddle => palette.Surface;

        public override Color ImageMarginGradientEnd => palette.Surface;

        public override Color SeparatorDark => palette.Border;

        public override Color SeparatorLight => palette.Surface;
    }

    private sealed class ThemeRenderer(ThemePalette palette)
        : ToolStripProfessionalRenderer(new ThemeColorTable(palette))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                e.TextColor = palette.SelectionText;
            }

            base.OnRenderItemText(e);
        }
    }
}
