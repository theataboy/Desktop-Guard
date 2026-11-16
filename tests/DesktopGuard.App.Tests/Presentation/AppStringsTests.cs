using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using DesktopGuard.App.Resources;

namespace DesktopGuard.App.Tests.Presentation;

[TestClass]
public sealed class AppStringsTests
{
    [TestMethod]
    public void SettingsMenuMnemonic_DoesNotChangeSettingsDialogTitle()
    {
        Assert.AreEqual("Settings", AppStrings.Settings);
        Assert.AreEqual("S&ettings", AppStrings.SettingsMenuItem);
    }

    [TestMethod]
    public void EveryStringAccessorResolvesAResourceAndValidFormat()
    {
        Type wrapper = typeof(AppStrings);
        foreach (PropertyInfo property in wrapper.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            Assert.IsNotNull(property.GetValue(null), $"Resource property {property.Name} must resolve.");
        }

        foreach (MethodInfo method in wrapper.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(string)))
        {
            object?[] arguments = method.GetParameters()
                .Select(parameter => parameter.ParameterType == typeof(string)
                    ? (object?)"example"
                    : parameter.ParameterType == typeof(int)
                        ? 1
                        : null)
                .ToArray();
            string? result = method.Invoke(null, arguments) as string;
            Assert.IsNotNull(result, $"Resource method {method.Name} must resolve and format.");
        }
    }

    [TestMethod]
    public void EveryNeutralResourceHasAnAppStringsAccessor()
    {
        ResourceManager manager = new("DesktopGuard.App.Resources.Strings", typeof(AppStrings).Assembly);
        ResourceSet resources = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
        HashSet<string> resourceNames = resources.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        object[] formatArguments = Enumerable.Repeat<object>("example", 32).ToArray();
        foreach (DictionaryEntry entry in resources)
        {
            string value = (string)entry.Value!;
            try
            {
                _ = string.Format(CultureInfo.InvariantCulture, value, formatArguments);
            }
            catch (FormatException exception)
            {
                Assert.Fail($"Resource {entry.Key} has invalid format placeholders: {exception.Message}");
            }
        }

        HashSet<string> accessorNames = typeof(AppStrings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Concat(typeof(AppStrings)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType == typeof(string) && !method.IsSpecialName)
                .Select(method => method.Name))
            .ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(accessorNames.ToArray(), resourceNames.ToArray());
    }
}
