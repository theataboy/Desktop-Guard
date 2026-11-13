using System.Globalization;
using System.Resources;

namespace DesktopGuard.App.Resources;

internal static class AppStrings
{
    private static readonly ResourceManager ResourceManager =
        new("DesktopGuard.App.Resources.Strings", typeof(AppStrings).Assembly);

    public static string ApplicationTitle => GetRequiredString(nameof(ApplicationTitle));

    public static string ScanNow => GetRequiredString(nameof(ScanNow));

    public static string ApproveSelected => GetRequiredString(nameof(ApproveSelected));

    public static string ParkSelected => GetRequiredString(nameof(ParkSelected));

    public static string RestoreSelected => GetRequiredString(nameof(RestoreSelected));

    public static string MoveToUserDesktop => GetRequiredString(nameof(MoveToUserDesktop));

    public static string MoveToPublicDesktop => GetRequiredString(nameof(MoveToPublicDesktop));

    public static string RetryOperation => GetRequiredString(nameof(RetryOperation));

    public static string PublicVisibilityConfirmation => GetRequiredString(nameof(PublicVisibilityConfirmation));

    public static string StartAtSignIn => GetRequiredString(nameof(StartAtSignIn));

    public static string OpenDesktopGuard => GetRequiredString(nameof(OpenDesktopGuard));

    public static string OpenRecoveryFolder => GetRequiredString(nameof(OpenRecoveryFolder));

    public static string Exit => GetRequiredString(nameof(Exit));

    public static string RecoveryFolderUnavailable => GetRequiredString(nameof(RecoveryFolderUnavailable));

    public static string Scanning => GetRequiredString(nameof(Scanning));

    public static string ScanCompleteFormat => GetRequiredString(nameof(ScanCompleteFormat));

    public static string ScanCompleteWithIssuesFormat => GetRequiredString(nameof(ScanCompleteWithIssuesFormat));

    public static string UnexpectedScanFailure => GetRequiredString(nameof(UnexpectedScanFailure));

    public static string Loading => GetRequiredString(nameof(Loading));

    public static string NoShortcuts => GetRequiredString(nameof(NoShortcuts));

    public static string PendingReview => GetRequiredString(nameof(PendingReview));

    public static string ApprovedVisible => GetRequiredString(nameof(ApprovedVisible));

    public static string Parked => GetRequiredString(nameof(Parked));

    public static string RecoveryFolder => GetRequiredString(nameof(RecoveryFolder));

    public static string Parking => GetRequiredString(nameof(Parking));

    public static string Restoring => GetRequiredString(nameof(Restoring));

    public static string Moving => GetRequiredString(nameof(Moving));

    public static string ReviewRequired => GetRequiredString(nameof(ReviewRequired));

    public static string RecoveryCheckRequired => GetRequiredString(nameof(RecoveryCheckRequired));

    public static string StartupFailure => GetRequiredString(nameof(StartupFailure));

    public static string NotificationUnavailable => GetRequiredString(nameof(NotificationUnavailable));

    public static string StateLoadFailure => GetRequiredString(nameof(StateLoadFailure));

    public static string StateTooLarge => GetRequiredString(nameof(StateTooLarge));

    public static string StateUnavailable => GetRequiredString(nameof(StateUnavailable));

    public static string InitializationFailure => GetRequiredString(nameof(InitializationFailure));

    public static string OperationFailure => GetRequiredString(nameof(OperationFailure));

    public static string DestinationConflict => GetRequiredString(nameof(DestinationConflict));

    public static string FileNotStable => GetRequiredString(nameof(FileNotStable));

    public static string SourceMissing => GetRequiredString(nameof(SourceMissing));

    public static string AccessDenied => GetRequiredString(nameof(AccessDenied));

    public static string FileLocked => GetRequiredString(nameof(FileLocked));

    public static string ElevationCancelled => GetRequiredString(nameof(ElevationCancelled));

    public static string ElevationFailed => GetRequiredString(nameof(ElevationFailed));

    public static string InvalidPath => GetRequiredString(nameof(InvalidPath));

    public static string RecoveryUnavailable => GetRequiredString(nameof(RecoveryUnavailable));

    public static string AmbiguousRecovery => GetRequiredString(nameof(AmbiguousRecovery));

    public static string IoFailure => GetRequiredString(nameof(IoFailure));

    public static string ShortcutNotificationTitle => GetRequiredString(nameof(ShortcutNotificationTitle));

    public static string PublicShortcutNotificationTitle => GetRequiredString(nameof(PublicShortcutNotificationTitle));

    public static string ShortcutNotificationBodyFormat => GetRequiredString(nameof(ShortcutNotificationBodyFormat));

    public static string UnknownShortcutName => GetRequiredString(nameof(UnknownShortcutName));

    public static string RootWatchFailedFormat => GetRequiredString(nameof(RootWatchFailedFormat));

    public static string RootPathChangedFormat => GetRequiredString(nameof(RootPathChangedFormat));

    public static string NameColumn => GetRequiredString(nameof(NameColumn));

    public static string LocationColumn => GetRequiredString(nameof(LocationColumn));

    public static string StatusColumn => GetRequiredString(nameof(StatusColumn));

    public static string UserDesktop => GetRequiredString(nameof(UserDesktop));

    public static string PublicDesktop => GetRequiredString(nameof(PublicDesktop));

    public static string Issues => GetRequiredString(nameof(Issues));

    public static string NoIssuesFound => GetRequiredString(nameof(NoIssuesFound));

    public static string RootUnavailableFormat => GetRequiredString(nameof(RootUnavailableFormat));

    public static string RootCouldNotBeReadFormat => GetRequiredString(nameof(RootCouldNotBeReadFormat));

    public static string EntryCouldNotBeInspectedFormat => GetRequiredString(nameof(EntryCouldNotBeInspectedFormat));

    public static string EntryOutsideRootSkippedFormat => GetRequiredString(nameof(EntryOutsideRootSkippedFormat));

    public static string ReparsePointSkippedFormat => GetRequiredString(nameof(ReparsePointSkippedFormat));

    public static string EntryLimitReachedFormat => GetRequiredString(nameof(EntryLimitReachedFormat));

    public static string AdditionalIssuesFormat => GetRequiredString(nameof(AdditionalIssuesFormat));

    public static string UnknownScanIssue => GetRequiredString(nameof(UnknownScanIssue));

    private static string GetRequiredString(string name)
    {
        return ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new MissingManifestResourceException($"The application resource '{name}' is missing.");
    }
}
