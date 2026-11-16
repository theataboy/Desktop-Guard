using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.ShortcutManagement;

internal sealed record ElevatedShortcutOperationRequest(
    ElevatedShortcutOperationKind Kind,
    Guid ShortcutId,
    Guid OperationId,
    Guid? RecoveryFolderId,
    string Name,
    DesktopRootKind? SourceRootKind,
    DesktopRootKind? DestinationRootKind,
    bool IsRetry = false,
    string? TargetName = null);
