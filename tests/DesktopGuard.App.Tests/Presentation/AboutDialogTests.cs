using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using DesktopGuard.App.Presentation;

namespace DesktopGuard.App.Tests.Presentation;

[TestClass]
public sealed class AboutDialogTests
{
    [TestMethod]
    public void Dialog_ShowsRequestedTitleLogoAndOptionsWithoutCloseButtons()
    {
        RunOnSta(() =>
        {
            AboutInformation information = new("1.0.1", "21-11-2026");
            AboutActionLauncher launcher = new(AppContext.BaseDirectory, _ => null);
            using AboutDialog dialog = new(information, launcher);

            Assert.AreEqual("About Desktop Guard", dialog.Text);
            Assert.IsTrue(dialog.ControlBox);
            Assert.IsFalse(dialog.MinimizeBox);
            Assert.IsFalse(dialog.MaximizeBox);
            Assert.AreEqual(FormBorderStyle.FixedDialog, dialog.FormBorderStyle);

            Control[] controls = GetDescendants(dialog).ToArray();
            Assert.AreEqual(0, controls.OfType<Button>().Count());
            PictureBox logo = controls.OfType<PictureBox>().Single(control => control.Name == "aboutLogoPictureBox");
            Assert.AreEqual(new Size(152, 152), logo.Size);
            Assert.IsNotNull(logo.Image);
            Assert.IsTrue(controls.OfType<Label>().Any(label => label.Text == "Desktop Guard"));
            Assert.IsTrue(controls.OfType<Label>().Any(label => label.Text == "Version: 1.0.1"));
            Assert.IsTrue(controls.OfType<Label>().Any(label => label.Text == "Build date: 21-11-2026"));
            CollectionAssert.AreEquivalent(
                new[] { "Desktop Guard releases", "Open LICENSE", "Open README.html", "Copy version information" },
                controls.OfType<LinkLabel>().Select(link => link.Text).ToArray());
        });
    }

    private static IEnumerable<Control> GetDescendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in GetDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        Thread uiThread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        Assert.IsTrue(uiThread.Join(TimeSpan.FromSeconds(15)), "The About dialog UI test did not finish.");
        if (failure is not null)
        {
            throw new AssertFailedException($"The About dialog UI check failed: {failure}");
        }
    }
}
