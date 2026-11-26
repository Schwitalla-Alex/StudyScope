using System.Globalization;
using StudyScope.Core.Localization;

namespace StudyScope.Tests.Localization;

[TestFixture]
public class LocTests
{
    private const string Slides = "{count, plural, one {# Folie} other {# Folien}}";

    [Test]
    [SetUICulture("de-DE")]
    public void Plural_German_OneAndOther()
    {
        Assert.That(Loc.Format(Slides, ("count", 1)), Is.EqualTo("1 Folie"));
        Assert.That(Loc.Format(Slides, ("count", 3)), Is.EqualTo("3 Folien"));
        Assert.That(Loc.Format(Slides, ("count", 0)), Is.EqualTo("0 Folien"));
    }

    [Test]
    [SetUICulture("pl-PL")]
    public void Plural_Polish_UsesCldrFewAndMany()
    {
        const string pattern = "{n, plural, one {# plik} few {# pliki} many {# plików} other {# pliku}}";
        Assert.That(Loc.Format(pattern, ("n", 1)), Is.EqualTo("1 plik"));
        Assert.That(Loc.Format(pattern, ("n", 3)), Is.EqualTo("3 pliki"));
        Assert.That(Loc.Format(pattern, ("n", 5)), Is.EqualTo("5 plików"));
        Assert.That(Loc.Format(pattern, ("n", 22)), Is.EqualTo("22 pliki"));
    }

    [Test]
    [SetUICulture("ar")]
    public void Plural_Arabic_UsesZeroAndTwo()
    {
        const string pattern = "{n, plural, zero {zero} one {one} two {two} few {few} many {many} other {other}}";
        Assert.That(Loc.Format(pattern, ("n", 0)), Is.EqualTo("zero"));
        Assert.That(Loc.Format(pattern, ("n", 2)), Is.EqualTo("two"));
        Assert.That(Loc.Format(pattern, ("n", 5)), Is.EqualTo("few"));
        Assert.That(Loc.Format(pattern, ("n", 11)), Is.EqualTo("many"));
    }

    [Test]
    [SetUICulture("en-US")]
    public void ExactMatch_TakesPrecedence()
    {
        const string pattern = "{count, plural, =0 {No slides} one {# slide} other {# slides}}";
        Assert.That(Loc.Format(pattern, ("count", 0)), Is.EqualTo("No slides"));
        Assert.That(Loc.Format(pattern, ("count", 1)), Is.EqualTo("1 slide"));
    }

    [Test]
    [SetUICulture("en-US")]
    public void NamedPlaceholder_AndLoneApostrophe_StayLiteral()
    {
        Assert.That(Loc.Format("Didn't find {name}.", ("name", "Testprofil")), Is.EqualTo("Didn't find Testprofil."));
        Assert.That(Loc.Format("Use '{'braces'}'", ("x", 1)), Is.EqualTo("Use {braces}"));
    }

    [Test]
    [SetUICulture("de-DE")]
    public void GermanQuotes_AreLiteral()
    {
        Assert.That(Loc.Format("Profil „{name}“ gelöscht.", ("name", "Testprofil")), Is.EqualTo("Profil „Testprofil“ gelöscht."));
    }

    [Test]
    public void Empty_ReturnsEmpty() => Assert.That(Loc.Format(string.Empty), Is.Empty);
}
