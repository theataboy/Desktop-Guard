namespace DesktopGuard.App.Notifications;

internal sealed class NotificationActivationBuffer
{
    private bool _isReady;
    private bool _hasPendingActivation;
    private Guid? _pendingShortcutId;

    public bool TryGetReadyShortcutId(Guid? shortcutId, out Guid? readyShortcutId)
    {
        if (!_isReady)
        {
            _pendingShortcutId = shortcutId;
            _hasPendingActivation = true;
            readyShortcutId = null;
            return false;
        }

        readyShortcutId = shortcutId;
        return true;
    }

    public bool MarkReady(out Guid? pendingShortcutId)
    {
        _isReady = true;
        pendingShortcutId = _pendingShortcutId;
        _pendingShortcutId = null;
        bool hasPendingActivation = _hasPendingActivation;
        _hasPendingActivation = false;
        return hasPendingActivation;
    }
}
