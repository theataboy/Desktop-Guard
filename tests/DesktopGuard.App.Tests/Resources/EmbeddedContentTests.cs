using System.Security.Cryptography;
using System.Text;
using DesktopGuard.App.Resources;

namespace DesktopGuard.App.Tests.Resources;

[TestClass]
public sealed class EmbeddedContentTests
{
    [TestMethod]
    public void Read_ReturnsTheExpectedEmbeddedContent()
    {
        byte[] actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(EmbeddedContent.Read()));
        byte[] expectedHash = Convert.FromHexString("DAF19A01EE57D9CABC9C70AC3DA309B1865EC7979535CFAEFC2106804CB937A6");

        CollectionAssert.AreEqual(expectedHash, actualHash);
    }
}
