namespace DesktopGuard.App.Notifications;

internal static class NotificationActivation
{
    public static Guid? ParseShortcutId(IEnumerable<KeyValuePair<string, string>> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        using IEnumerator<KeyValuePair<string, string>> enumerator = arguments.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return null;
        }

        KeyValuePair<string, string> argument = enumerator.Current;
        if (enumerator.MoveNext()
            || !string.Equals(argument.Key, "shortcutId", StringComparison.Ordinal)
            || !Guid.TryParseExact(argument.Value, "N", out Guid parsed)
            || parsed == Guid.Empty)
        {
            return null;
        }

        return parsed;
    }

    public static Guid? ResolveCurrentShortcutId(Guid? requestedId, Func<Guid, bool> containsShortcut)
    {
        ArgumentNullException.ThrowIfNull(containsShortcut);
        return requestedId is Guid id && id != Guid.Empty && containsShortcut(id)
            ? id
            : null;
    }
}
