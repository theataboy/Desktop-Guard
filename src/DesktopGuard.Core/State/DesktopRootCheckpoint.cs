using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.Core.State;

public sealed record DesktopRootCheckpoint(
    DesktopRootKind RootKind,
    string RootPath,
    bool IsBaselined,
    IReadOnlyList<string> KnownPaths);
