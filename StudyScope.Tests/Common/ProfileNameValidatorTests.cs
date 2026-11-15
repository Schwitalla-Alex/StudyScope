using NUnit.Framework;
using StudyScope.Core.Common;

namespace StudyScope.Tests.Common;

[TestFixture]
public class ProfileNameValidatorTests
{
    [TestCase("BGB")]
    [TestCase("Steuerrecht 2026")]
    [TestCase("Jura - Teil 1")]
    [TestCase("Übung_Ä.Ö.Ü")]
    [TestCase("  Mit Leerzeichen außen  ")]
    [TestCase("CONTRA")]        // nur exakte Gerätenamen sind reserviert
    [TestCase("Konsole")]
    public void Validate_GueltigerName_LiefertNull(string name)
    {
        Assert.That(ProfileNameValidator.Validate(name), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_LeererName_LiefertMeldung(string? name)
    {
        Assert.That(ProfileNameValidator.Validate(name), Is.EqualTo("Bitte geben Sie einen Namen ein."));
    }

    [TestCase("WSL/Test", "/")]
    [TestCase("..\\evil", "\\")]
    [TestCase("a<b", "<")]
    [TestCase("a>b", ">")]
    [TestCase("C:Daten", ":")]
    [TestCase("Zitat\"x", "\"")]
    [TestCase("a|b", "|")]
    [TestCase("Was?", "?")]
    [TestCase("Stern*", "*")]
    public void Validate_UnzulaessigesZeichen_NenntDasZeichen(string name, string zeichen)
    {
        var meldung = ProfileNameValidator.Validate(name);

        Assert.That(meldung, Does.StartWith("Der Name darf diese Zeichen nicht enthalten:"));
        Assert.That(meldung, Does.Contain(zeichen));
    }

    [Test]
    public void Validate_Steuerzeichen_LiefertMeldung()
    {
        Assert.That(ProfileNameValidator.Validate("Tab\tName"), Is.EqualTo("Der Name enthält unzulässige Steuerzeichen."));
    }

    [TestCase("CON")]
    [TestCase("con")]
    [TestCase("PRN")]
    [TestCase("AUX")]
    [TestCase("NUL")]
    [TestCase("COM1")]
    [TestCase("com9")]
    [TestCase("LPT1")]
    [TestCase("LPT9")]
    [TestCase("CON.backup")]   // Windows prüft den Teil vor dem ersten Punkt
    [TestCase("nul .txt")]
    public void Validate_ReservierterWindowsName_WirdAbgelehnt(string name)
    {
        Assert.That(ProfileNameValidator.Validate(name), Does.Contain("reservierter Systemname"));
    }

    [TestCase(".")]
    [TestCase("..")]
    [TestCase("Ende.")]
    public void Validate_PunktAmEnde_WirdAbgelehnt(string name)
    {
        Assert.That(ProfileNameValidator.Validate(name), Is.EqualTo("Der Name darf nicht mit einem Punkt enden."));
    }

    [Test]
    public void Validate_ZuLang_WirdAbgelehnt()
    {
        var name = new string('a', ProfileNameValidator.MaxLength + 1);

        Assert.That(ProfileNameValidator.Validate(name), Does.Contain($"höchstens {ProfileNameValidator.MaxLength} Zeichen"));
        Assert.That(ProfileNameValidator.Validate(name[..ProfileNameValidator.MaxLength]), Is.Null);
    }
}
