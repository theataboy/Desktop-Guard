namespace DesktopGuard.App.Presentation;

internal sealed class RuntimeWarningSet
{
    private readonly Dictionary<string, string> _messages = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Messages => _messages.Values.ToArray();

    public int Count => _messages.Count;

    public bool Set(string source, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (_messages.TryGetValue(source, out string? current)
            && string.Equals(current, message, StringComparison.Ordinal))
        {
            return false;
        }

        _messages[source] = message;
        return true;
    }

    public bool Clear(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return _messages.Remove(source);
    }
}
