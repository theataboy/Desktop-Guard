using DesktopGuard.App.Application;

namespace DesktopGuard.App.Presentation;

internal sealed record ShortcutListPresentationRow(
    ShortcutListEntry Entry,
    string Location,
    string Status,
    bool NeedsReview,
    bool Approved,
    bool Recovery,
    bool IsHidden = false);
