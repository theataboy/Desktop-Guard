namespace DesktopGuard.Core.Shortcuts;

public sealed record RecoveryShortcutSnapshot(Guid ShortcutId, byte[] IdentityHash, DateTimeOffset LastWriteUtc);
