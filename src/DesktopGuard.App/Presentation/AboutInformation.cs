using System.Globalization;
using System.Reflection;
using DesktopGuard.App.Resources;

namespace DesktopGuard.App.Presentation;

internal sealed record AboutInformation(string Version, string BuildDate)
{
    private const string BuildDateMetadataKey = "BuildDateUtc";

    public static AboutInformation FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? AppStrings.AboutValueUnavailable;

        string? buildDateMetadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, BuildDateMetadataKey, StringComparison.Ordinal))
            ?.Value;
        string buildDate = DateTime.TryParseExact(
            buildDateMetadata,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime buildDateUtc)
                ? buildDateUtc.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
                : AppStrings.AboutValueUnavailable;

        return new AboutInformation(version, buildDate);
    }

    public string ToClipboardText() => AppStrings.AboutVersionInformationFormat(Version, BuildDate);
}
