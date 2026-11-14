using DesktopGuard.Core.Shortcuts;

namespace DesktopGuard.Core.State;

public sealed record DesktopGuardState
{
    public const int CurrentSchemaVersion = 6;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool StartAtSignIn { get; init; } = true;

    public bool StartMinimized { get; init; } = true;

    public List<DesktopRootCheckpoint> Roots { get; init; } = [];

    public List<TrackedShortcut> Shortcuts { get; init; } = [];

    public List<DesktopEvent> Events { get; init; } = [];
}
