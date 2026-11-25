using System.Diagnostics;
using System.Runtime.InteropServices;
using StudyScope.Core.Common;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>Webadressen im System-Browser öffnen: nur http/https, ein Argument, kein echter Prozessstart im Test.</summary>
[TestFixture]
public class ExternalLinkOpenerTests
{
    private readonly List<ProcessStartInfo> _started = [];

    [SetUp]
    public void SetUp() => _started.Clear();

    private ExternalLinkOpener Create(OSPlatform platform) => new(info => { _started.Add(info); return true; }, platform);

    [TestCase("https://example.org/seite")]
    [TestCase("http://example.org/seite")]
    public void OpensHttpAndHttps(string url)
    {
        Assert.That(Create(OSPlatform.Windows).OpenUrl(url), Is.True);
        Assert.That(_started, Has.Count.EqualTo(1));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("file:///C:/Windows/System32/calc.exe")]
    [TestCase("javascript:alert(1)")]
    [TestCase("mailto:test@example.org")]
    [TestCase("ftp://example.org/datei")]
    [TestCase("example.org")]
    [TestCase("/relativ/pfad")]
    [TestCase("https://benutzer:geheim@example.org/")]
    public void RejectsOtherAddresses_WithoutStartingAProcess(string url)
    {
        Assert.That(Create(OSPlatform.Windows).OpenUrl(url), Is.False);
        Assert.That(_started, Is.Empty);
    }

    [Test]
    public void Windows_UsesShellExecute()
    {
        Create(OSPlatform.Windows).OpenUrl("https://example.org/a b");
        var info = _started.Single();
        Assert.Multiple(() =>
        {
            Assert.That(info.UseShellExecute, Is.True);
            Assert.That(info.FileName, Is.EqualTo("https://example.org/a%20b"));
        });
    }

    [Test]
    public void Linux_UsesXdgOpenWithSingleArgument()
    {
        Create(OSPlatform.Linux).OpenUrl("https://example.org/?q=a&b=\"c\"");
        var info = _started.Single();
        Assert.Multiple(() =>
        {
            Assert.That(info.FileName, Is.EqualTo("xdg-open"));
            Assert.That(info.UseShellExecute, Is.False);
            Assert.That(info.ArgumentList, Has.Count.EqualTo(1));
            Assert.That(info.ArgumentList[0], Does.StartWith("https://example.org/"));
        });
    }

    [Test]
    public void StartFailure_ReturnsFalse()
    {
        var opener = new ExternalLinkOpener(_ => throw new InvalidOperationException("kein Browser"), OSPlatform.Linux);
        Assert.That(opener.OpenUrl("https://example.org"), Is.False);
    }

    [Test]
    public void ExternalUrl_HttpsOnly_RejectsHttp()
    {
        Assert.That(ExternalUrl.TryParse("http://example.org", out _, httpsOnly: true), Is.False);
        Assert.That(ExternalUrl.TryParse("https://example.org", out _, httpsOnly: true), Is.True);
    }
}
