namespace DesktopGuard.App.ShortcutManagement;

internal sealed record ShortcutIdentityReadResult(byte[] IdentityHash, byte[] SourceContentHash, int SourceLength, DateTimeOffset LastWriteUtc);
