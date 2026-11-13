using DesktopGuard.Core.State;

namespace DesktopGuard.App.Persistence;

internal interface IDesktopGuardStateStore
{
    string RecoveryDirectoryPath { get; }

    DesktopGuardState Load();

    void Save(DesktopGuardState state);

    void EnsureRecoveryDirectory();
}
