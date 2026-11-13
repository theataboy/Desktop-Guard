using System.Text;
using System.Text.Json;
using DesktopGuard.App.ShortcutManagement;
using DesktopGuard.Core.DesktopScanning;

namespace DesktopGuard.App.Tests.ShortcutManagement;

[TestClass]
public sealed class ElevatedShortcutOperationRunnerTests
{
    [TestMethod]
    public void Run_RejectsShortcutNameWithPathTraversalBeforeResolvingFolders()
    {
        ElevatedShortcutOperationRequest request = CreateRequest() with { Name = "..\\Other.lnk" };

        int exitCode = ElevatedShortcutOperationRunner.Run(EncodeRequest(request));

        Assert.AreEqual(10, exitCode);
    }

    [TestMethod]
    public void Run_RejectsAlternateDataStreamName()
    {
        ElevatedShortcutOperationRequest request = CreateRequest() with { Name = "Program:stream.lnk" };

        int exitCode = ElevatedShortcutOperationRunner.Run(EncodeRequest(request));

        Assert.AreEqual(10, exitCode);
    }

    [TestMethod]
    public void Run_RejectsRequestsWithCallerSuppliedPathFields()
    {
        string json = JsonSerializer.Serialize(CreateRequest());
        string requestWithPath = $"{json[..^1]},\"Path\":\"C:\\\\Windows\\\\example.lnk\"}}";

        int exitCode = ElevatedShortcutOperationRunner.Run(EncodeJson(requestWithPath));

        Assert.AreEqual(10, exitCode);
    }

    [TestMethod]
    public void Run_RejectsMalformedPayload()
    {
        int exitCode = ElevatedShortcutOperationRunner.Run("not-valid-base64!");

        Assert.AreEqual(10, exitCode);
    }

    [TestMethod]
    public void ResolvePaths_DesktopMoveDoesNotRequireRecoveryFolderId()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string userDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "UserDesktop")).FullName;
        string publicDesktop = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "PublicDesktop")).FullName;
        string localAppData = Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "LocalAppData")).FullName;
        ElevatedShortcutOperationRequest request = CreateRequest();
        ElevatedShortcutOperationRunner.InteractiveUserFolders userFolders = new(
            userDesktop,
            publicDesktop,
            localAppData);

        (string sourcePath, string destinationPath) = ElevatedShortcutOperationRunner.ResolvePaths(request, userFolders);

        Assert.AreEqual(Path.Combine(userDesktop, request.Name), sourcePath);
        Assert.AreEqual(Path.Combine(publicDesktop, request.Name), destinationPath);
    }

    private static ElevatedShortcutOperationRequest CreateRequest()
    {
        return new ElevatedShortcutOperationRequest(
            ElevatedShortcutOperationKind.MoveBetweenDesktops,
            Guid.NewGuid(),
            Guid.NewGuid(),
            RecoveryFolderId: null,
            Name: "Program.lnk",
            SourceRootKind: DesktopRootKind.User,
            DestinationRootKind: DesktopRootKind.Public);
    }

    private static string EncodeRequest(ElevatedShortcutOperationRequest request)
    {
        return EncodeJson(JsonSerializer.Serialize(request));
    }

    private static string EncodeJson(string json)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"DesktopGuard-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
