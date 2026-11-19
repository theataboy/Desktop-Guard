using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.Core.Shortcuts;

public sealed record TrackedShortcut(
    Guid Id,
    DesktopRootKind RootKind,
    string RootPath,
    string Name,
    string OriginalPath,
    ShortcutStatus Status,
    ShortcutStatus? StatusBeforeParking,
    ShortcutOperation Operation,
    Guid? RecoveryFolderId,
    DesktopRootKind? TargetRootKind = null,
    string? TargetRootPath = null,
    Guid? OperationId = null,
    byte[]? OperationContentHash = null,
    string? TargetName = null,
    bool IsHidden = false,
    bool IsDesktopGuardShortcut = false,
    byte[]? ShortcutIdentityHash = null,
    DateTimeOffset? AutoParkingSinceUtc = null,
    byte[]? AutoParkingContentHash = null,
    bool AutoParkingEvaluated = false);
