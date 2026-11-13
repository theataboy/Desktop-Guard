namespace DesktopGuard.Core.State;

public sealed record DesktopEvent(string FileName, string Path, DateTimeOffset TimestampUtc);
