using System.Globalization;
using System.Resources;

namespace DesktopGuard.App.Resources;

internal static class AppStrings
{
    private static readonly ResourceManager ResourceManager =
        new("DesktopGuard.App.Resources.Strings", typeof(AppStrings).Assembly);

    public static string ApplicationTitle => GetRequiredString(nameof(ApplicationTitle));

    public static string AboutButtonAccessibleName => GetRequiredString(nameof(AboutButtonAccessibleName));

    public static string AboutTitle => GetRequiredString(nameof(AboutTitle));

    public static string AboutProductName => GetRequiredString(nameof(AboutProductName));

    public static string AboutLogoAccessibleName => GetRequiredString(nameof(AboutLogoAccessibleName));

    public static string AboutValueUnavailable => GetRequiredString(nameof(AboutValueUnavailable));

    public static string AboutVersionFormat(string version) => Format(nameof(AboutVersionFormat), version);

    public static string AboutBuildDateFormat(string date) => Format(nameof(AboutBuildDateFormat), date);

    public static string AboutReleasesLink => GetRequiredString(nameof(AboutReleasesLink));

    public static string AboutLicenseLink => GetRequiredString(nameof(AboutLicenseLink));

    public static string AboutReadmeLink => GetRequiredString(nameof(AboutReadmeLink));

    public static string AboutCopyVersionInformationLink => GetRequiredString(nameof(AboutCopyVersionInformationLink));

    public static string AboutReleasesOpenFailure => GetRequiredString(nameof(AboutReleasesOpenFailure));

    public static string AboutVersionInformationCopied => GetRequiredString(nameof(AboutVersionInformationCopied));

    public static string AboutClipboardFailure => GetRequiredString(nameof(AboutClipboardFailure));

    public static string AboutVersionInformationFormat(string version, string date) =>
        Format(nameof(AboutVersionInformationFormat), version, date);

    public static string AboutOpenFailureFormat(string target) => Format(nameof(AboutOpenFailureFormat), target);

    public static string AboutPackageFileMissingFormat(string fileName, string path) =>
        Format(nameof(AboutPackageFileMissingFormat), fileName, path);

    public static string ScanNow => GetRequiredString(nameof(ScanNow));

    public static string ScanNowButton => GetRequiredString(nameof(ScanNowButton));

    public static string MoveToUserDesktop => GetRequiredString(nameof(MoveToUserDesktop));

    public static string MoveToPublicDesktop => GetRequiredString(nameof(MoveToPublicDesktop));

    public static string AddDesktopShortcut => GetRequiredString(nameof(AddDesktopShortcut));

    public static string AddDesktopShortcutButton => GetRequiredString(nameof(AddDesktopShortcutButton));

    public static string AddingDesktopShortcut => GetRequiredString(nameof(AddingDesktopShortcut));

    public static string CreatingDesktopShortcut => GetRequiredString(nameof(CreatingDesktopShortcut));

    public static string RetryOperation => GetRequiredString(nameof(RetryOperation));

    public static string PublicVisibilityConfirmation => GetRequiredString(nameof(PublicVisibilityConfirmation));

    public static string StartAtSignIn => GetRequiredString(nameof(StartAtSignIn));

    public static string StartMinimized => GetRequiredString(nameof(StartMinimized));

    public static string AutoParkDelayMinutes => GetRequiredString(nameof(AutoParkDelayMinutes));

    public static string ScheduledForAutoParking => GetRequiredString(nameof(ScheduledForAutoParking));

    public static string RecoveryDuplicatesRemovedTitle => GetRequiredString(nameof(RecoveryDuplicatesRemovedTitle));

    public static string RecoveryDuplicatesRemovedFormat(int count) => Format(nameof(RecoveryDuplicatesRemovedFormat), count);

    public static string RecoveryDuplicateRemovalFailedFormat(int count) => Format(nameof(RecoveryDuplicateRemovalFailedFormat), count);

    public static string ShortcutIdentityUnavailableFormat(int count) =>
        Format(nameof(ShortcutIdentityUnavailableFormat), count);

    public static string More => GetRequiredString(nameof(More));

    public static string Settings => GetRequiredString(nameof(Settings));

    public static string SettingsMenuItem => GetRequiredString(nameof(SettingsMenuItem));

    public static string SettingsDescription => GetRequiredString(nameof(SettingsDescription));

    public static string Save => GetRequiredString(nameof(Save));

    public static string Cancel => GetRequiredString(nameof(Cancel));

    public static string SaveSettingsButton => GetRequiredString(nameof(SaveSettingsButton));

    public static string ApplySettingsButton => GetRequiredString(nameof(ApplySettingsButton));

    public static string CancelSettingsButton => GetRequiredString(nameof(CancelSettingsButton));

    public static string SaveSettingsBeforeClosing => GetRequiredString(nameof(SaveSettingsBeforeClosing));

    public static string SearchShortcuts => GetRequiredString(nameof(SearchShortcuts));

    public static string SelectShortcutForActions => GetRequiredString(nameof(SelectShortcutForActions));

    public static string Approve => GetRequiredString(nameof(Approve));

    public static string Rename => GetRequiredString(nameof(Rename));

    public static string Delete => GetRequiredString(nameof(Delete));

    public static string Properties => GetRequiredString(nameof(Properties));

    public static string MakeVisible => GetRequiredString(nameof(MakeVisible));

    public static string MoreShortcutActions => GetRequiredString(nameof(MoreShortcutActions));

    public static string RenameShortcutTitle => GetRequiredString(nameof(RenameShortcutTitle));

    public static string RenameShortcutPrompt => GetRequiredString(nameof(RenameShortcutPrompt));

    public static string RenameShortcutExtensionLabel => GetRequiredString(nameof(RenameShortcutExtensionLabel));

    public static string Renaming => GetRequiredString(nameof(Renaming));

    public static string MovingToRecycleBin => GetRequiredString(nameof(MovingToRecycleBin));

    public static string PermanentlyDeleting => GetRequiredString(nameof(PermanentlyDeleting));

    public static string OpeningProperties => GetRequiredString(nameof(OpeningProperties));

    public static string MakingShortcutVisible => GetRequiredString(nameof(MakingShortcutVisible));

    public static string HiddenNameFormat(string name) => Format(nameof(HiddenNameFormat), name);

    public static string PermanentDeleteConfirmationFormat(string name) =>
        Format(nameof(PermanentDeleteConfirmationFormat), name);

    public static string Park => GetRequiredString(nameof(Park));

    public static string Restore => GetRequiredString(nameof(Restore));

    public static string AllFilterFormat(int count) => Format(nameof(AllFilterFormat), count);

    public static string NeedsReviewFilterFormat(int count) => Format(nameof(NeedsReviewFilterFormat), count);

    public static string ApprovedFilterFormat(int count) => Format(nameof(ApprovedFilterFormat), count);

    public static string RecoveryFilterFormat(int count) => Format(nameof(RecoveryFilterFormat), count);

    public static string IssuesCountFormat(int count) => Format(nameof(IssuesCountFormat), count);

    public static string SelectedShortcutFormat(string name, string status) =>
        Format(nameof(SelectedShortcutFormat), name, status);

    public static string OpenDesktopGuard => GetRequiredString(nameof(OpenDesktopGuard));

    public static string OpenRecoveryFolder => GetRequiredString(nameof(OpenRecoveryFolder));

    public static string OpenRecoveryFolderMenuItem => GetRequiredString(nameof(OpenRecoveryFolderMenuItem));

    public static string Exit => GetRequiredString(nameof(Exit));

    public static string RecoveryFolderUnavailable => GetRequiredString(nameof(RecoveryFolderUnavailable));

    public static string Scanning => GetRequiredString(nameof(Scanning));

    public static string ScanCompleteFormat => GetRequiredString(nameof(ScanCompleteFormat));

    public static string ScanCompleteWithIssuesFormat => GetRequiredString(nameof(ScanCompleteWithIssuesFormat));

    public static string UnexpectedScanFailure => GetRequiredString(nameof(UnexpectedScanFailure));

    public static string Loading => GetRequiredString(nameof(Loading));

    public static string NoShortcuts => GetRequiredString(nameof(NoShortcuts));

    public static string NoMatchingShortcuts => GetRequiredString(nameof(NoMatchingShortcuts));

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

    public static string DesktopShortcutAlreadyExists => GetRequiredString(nameof(DesktopShortcutAlreadyExists));

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

    private static string Format(string name, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, GetRequiredString(name), arguments);
    }
}
