using NUnit.Framework;
using StudyScope.Core.Common;

namespace StudyScope.Tests.Common;

/// <summary>
/// Testschalter STUDYSCOPE_USER_DATA_DIR für den Benutzerdaten-Ordner.
/// Die Umgebungsvariable gilt prozessweit, daher nicht parallel ausführen.
/// </summary>
[TestFixture]
[NonParallelizable]
public class AppPathsTests
{
    private string? _previousValue;

    [SetUp]
    public void SetUp()
    {
        _previousValue = Environment.GetEnvironmentVariable(AppPaths.UserDataDirectoryVariable);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(AppPaths.UserDataDirectoryVariable, _previousValue);
    }

    [Test]
    public void UserDataDirectory_OhneVariable_LiegtImBenutzerdatenOrdner()
    {
        Environment.SetEnvironmentVariable(AppPaths.UserDataDirectoryVariable, null);

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppPaths.AppFolderName);
        Assert.That(AppPaths.UserDataDirectory, Is.EqualTo(expected));
    }

    [Test]
    public void UserDataDirectory_MitVariable_LiefertDiesenOrdnerAlsVollstaendigenPfad()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "StudyScope_Testschalter", "data");
        Environment.SetEnvironmentVariable(AppPaths.UserDataDirectoryVariable, sandbox);

        Assert.That(AppPaths.UserDataDirectory, Is.EqualTo(Path.GetFullPath(sandbox)));
    }

    [Test]
    public void UserDataDirectory_MitRelativerVariable_WirdZuVollstaendigemPfad()
    {
        Environment.SetEnvironmentVariable(AppPaths.UserDataDirectoryVariable, "sandbox_data");

        Assert.That(Path.IsPathFullyQualified(AppPaths.UserDataDirectory), Is.True);
        Assert.That(AppPaths.UserDataDirectory, Does.EndWith("sandbox_data"));
    }

    [Test]
    public void UserDataDirectory_MitLeererVariable_BleibtBeimStandard()
    {
        Environment.SetEnvironmentVariable(AppPaths.UserDataDirectoryVariable, "   ");

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppPaths.AppFolderName);
        Assert.That(AppPaths.UserDataDirectory, Is.EqualTo(expected));
    }
}
