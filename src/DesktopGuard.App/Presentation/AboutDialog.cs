using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using DesktopGuard.App.Resources;
using Microsoft.Win32;

namespace DesktopGuard.App.Presentation;

internal sealed class AboutDialog : Form
{
    private const int LogoSize = 152;
    private const string LogoResourceName = "DesktopGuard.App.Resources.DesktopGuard.ico";

    private readonly AboutActionLauncher _actionLauncher;
    private readonly Label _copyStatusLabel;
    private bool _themeEventsSubscribed;

    public AboutDialog(AboutInformation information, AboutActionLauncher actionLauncher)
    {
        ArgumentNullException.ThrowIfNull(information);
        ArgumentNullException.ThrowIfNull(actionLauncher);
        _actionLauncher = actionLauncher;

        Text = AppStrings.AboutTitle;
        Name = nameof(AboutDialog);
        AccessibleName = AppStrings.AboutTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = true;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(600, 360);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
            ?? SystemIcons.Application;

        TableLayoutPanel layout = new()
        {
            AutoSize = false,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogoSize + 12));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label productNameLabel = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 16),
            Name = "productNameLabel",
            Text = AppStrings.AboutProductName,
        };
        layout.Controls.Add(productNameLabel, 0, 0);
        layout.SetColumnSpan(productNameLabel, 2);

        PictureBox logoPictureBox = new()
        {
            AccessibleName = AppStrings.AboutLogoAccessibleName,
            AccessibleRole = AccessibleRole.Graphic,
            Image = LoadLogo(),
            Margin = new Padding(0, 0, 12, 0),
            Name = "aboutLogoPictureBox",
            Size = new Size(LogoSize, LogoSize),
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        layout.Controls.Add(logoPictureBox, 0, 1);

        FlowLayoutPanel detailsPanel = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
            Name = "aboutDetailsPanel",
            WrapContents = false,
        };
        detailsPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            Name = "versionLabel",
            Text = AppStrings.AboutVersionFormat(information.Version),
        });
        detailsPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            Name = "buildDateLabel",
            Text = AppStrings.AboutBuildDateFormat(information.BuildDate),
        });

        detailsPanel.Controls.Add(CreateLink(
            "releasesLink",
            AppStrings.AboutReleasesLink,
            (_, _) => RunExternalAction(_actionLauncher.OpenReleases, AppStrings.AboutReleasesOpenFailure)));
        detailsPanel.Controls.Add(CreateLink(
            "licenseLink",
            AppStrings.AboutLicenseLink,
            (_, _) => ShowLicense()));
        detailsPanel.Controls.Add(CreateLink(
            "readmeLink",
            AppStrings.AboutReadmeLink,
            (_, _) => RunExternalAction(
                _actionLauncher.OpenReadme,
                AppStrings.AboutOpenFailureFormat("README.html"))));
        detailsPanel.Controls.Add(CreateLink(
            "copyVersionInformationLink",
            AppStrings.AboutCopyVersionInformationLink,
            (_, _) => CopyVersionInformation(information)));

        _copyStatusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
            Name = "copyStatusLabel",
            Text = string.Empty,
        };
        detailsPanel.Controls.Add(_copyStatusLabel);
        layout.Controls.Add(detailsPanel, 1, 1);
        Controls.Add(layout);

        WindowsTheme.Apply(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _themeEventsSubscribed = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnsubscribeFromThemeChanges();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeFromThemeChanges();
            foreach (PictureBox pictureBox in FindControls<PictureBox>(this))
            {
                pictureBox.Image?.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private static LinkLabel CreateLink(string name, string text, LinkLabelLinkClickedEventHandler onClick)
    {
        LinkLabel link = new()
        {
            AccessibleName = text,
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(0, 0, 0, 7),
            Name = name,
            Text = text,
            TabStop = true,
        };
        link.LinkClicked += onClick;
        return link;
    }

    private static Bitmap LoadLogo()
    {
        using Stream stream = typeof(AboutDialog).Assembly.GetManifestResourceStream(LogoResourceName)
            ?? throw new InvalidOperationException("The Desktop Guard icon resource is missing.");
        using Icon logo = new(stream, new Size(LogoSize, LogoSize));
        return logo.ToBitmap();
    }

    private static IEnumerable<TControl> FindControls<TControl>(Control parent)
        where TControl : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is TControl match)
            {
                yield return match;
            }

            foreach (TControl descendant in FindControls<TControl>(child))
            {
                yield return descendant;
            }
        }
    }

    private void RunExternalAction(Action action, string failureMessage)
    {
        try
        {
            action();
        }
        catch (AboutActionLauncher.PackageFileMissingException exception)
        {
            ShowExternalActionFailure(AppStrings.AboutPackageFileMissingFormat(
                exception.PackageFileName,
                exception.PackagePath));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or Win32Exception
            or InvalidOperationException
            or ArgumentException)
        {
            ShowExternalActionFailure(failureMessage);
        }
    }

    private void ShowLicense()
    {
        try
        {
            using LicenseDialog dialog = new(_actionLauncher.ReadLicenseText());
            dialog.ShowDialog(this);
        }
        catch (AboutActionLauncher.PackageFileMissingException exception)
        {
            ShowExternalActionFailure(AppStrings.AboutPackageFileMissingFormat(
                exception.PackageFileName,
                exception.PackagePath));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException)
        {
            ShowExternalActionFailure(AppStrings.AboutLicenseReadFailure);
        }
    }

    private void ShowExternalActionFailure(string message)
    {
        MessageBox.Show(
            this,
            message,
            AppStrings.AboutTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void CopyVersionInformation(AboutInformation information)
    {
        try
        {
            Clipboard.SetText(information.ToClipboardText());
            _copyStatusLabel.Text = AppStrings.AboutVersionInformationCopied;
        }
        catch (ExternalException)
        {
            MessageBox.Show(
                this,
                AppStrings.AboutClipboardFailure,
                AppStrings.AboutTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
            BeginInvoke(new Action(() => WindowsTheme.Apply(this)));
        }
        catch (InvalidOperationException)
        {
            // The About window is closing.
        }
    }

    private void UnsubscribeFromThemeChanges()
    {
        if (_themeEventsSubscribed)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _themeEventsSubscribed = false;
        }
    }
}
