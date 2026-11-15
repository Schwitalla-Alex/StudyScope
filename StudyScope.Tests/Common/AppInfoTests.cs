using NUnit.Framework;
using StudyScope.Core.Common;

namespace StudyScope.Tests.Common;

[TestFixture]
public class AppInfoTests
{
    [Test]
    public void Version_EntsprichtAssemblyVersionAusDirectoryBuildProps()
    {
        var v = typeof(AppInfo).Assembly.GetName().Version!;

        Assert.That(AppInfo.Version, Is.EqualTo($"{v.Major}.{v.Minor}.{v.Build}"));
    }

    [Test]
    public void Version_EnthaeltKeinenGitHash()
    {
        Assert.That(AppInfo.Version, Does.Match(@"^\d+\.\d+\.\d+$"));
    }
}
