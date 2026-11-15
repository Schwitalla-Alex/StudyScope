using NUnit.Framework;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Sicherheitstests: Kein Profilname darf eine Datei außerhalb des Profiles-Ordners erzeugen, lesen oder löschen.
/// </summary>
[TestFixture]
public class JsonStorageServiceProfilePathTests
{
    private string _baseDirectory = null!;
    private string _profilesDirectory = null!;
    private JsonStorageService _storageService = null!;

    [SetUp]
    public void SetUp()
    {
        // Eigener Unterordner, damit auch "../" (eine Ebene über dem Basisordner) noch im Testverzeichnis landet
        var testRoot = Path.Combine(Path.GetTempPath(), "StudyScope_PathTests_" + Guid.NewGuid());
        _baseDirectory = Path.Combine(testRoot, "data");
        _profilesDirectory = Path.Combine(_baseDirectory, "Profiles");
        _storageService = new JsonStorageService(_baseDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        var testRoot = Path.GetDirectoryName(_baseDirectory)!;
        if (Directory.Exists(testRoot))
            Directory.Delete(testRoot, recursive: true);
    }

    private string TestRoot => Path.GetDirectoryName(_baseDirectory)!;

    private List<string> AllFilesOutsideProfiles() =>
        Directory.GetFiles(TestRoot, "*", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetDirectoryName(f), _profilesDirectory, StringComparison.OrdinalIgnoreCase))
            .ToList();

    [TestCase("../evil")]
    [TestCase("../../evil")]
    [TestCase("..\\evil")]
    [TestCase("..\\..\\evil")]
    [TestCase("WSL/Test")]
    [TestCase("Unter\\Ordner")]
    [TestCase("/etc/passwd")]
    [TestCase("C:\\Windows\\evil")]
    [TestCase("..")]
    [TestCase(".")]
    public async Task SaveDocumentsAsync_PfadName_SchreibtNurImProfilesOrdner(string profileName)
    {
        await _storageService.SaveDocumentsAsync(profileName, new List<DocumentItem>());

        Assert.That(AllFilesOutsideProfiles(), Is.Empty, "Es wurde eine Datei außerhalb von Profiles geschrieben.");
        Assert.That(Directory.GetDirectories(_profilesDirectory), Is.Empty, "Es wurde ein Unterordner in Profiles angelegt.");
        Assert.That(Directory.GetFiles(_profilesDirectory, "*.json"), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task SaveUndLoad_NameMitSchraegstrich_FunktioniertAlsRundreise()
    {
        var docs = new List<DocumentItem> { new() { FilePath = "/tmp/a.pdf", FileName = "a.pdf" } };

        await _storageService.SaveDocumentsAsync("WSL/Test", docs);
        var loaded = await _storageService.LoadDocumentsAsync("WSL/Test");

        Assert.That(loaded.Select(d => d.FileName), Is.EqualTo(new[] { "a.pdf" }));
        Assert.That(File.Exists(Path.Combine(_profilesDirectory, "WSL_Test.json")), Is.True);
    }

    [Test]
    public async Task DeleteProfileAsync_PfadName_LoeschtKeineDateiAusserhalb()
    {
        // Datei, die "../opfer" ohne Absicherung treffen würde (data/opfer.json)
        var opfer = Path.Combine(_baseDirectory, "opfer.json");
        await File.WriteAllTextAsync(opfer, "{}");

        await _storageService.DeleteProfileAsync("../opfer");
        await _storageService.DeleteProfileAsync("..\\opfer");

        Assert.That(File.Exists(opfer), Is.True);
    }

    [Test]
    public async Task LoadDocumentsAsync_PfadName_LiestKeineDateiAusserhalb()
    {
        var fremd = Path.Combine(_baseDirectory, "fremd.json");
        await File.WriteAllTextAsync(fremd, "{\"Name\":\"fremd\",\"Documents\":[{\"FilePath\":\"x\",\"FileName\":\"geheim.pdf\"}]}");

        var loaded = await _storageService.LoadDocumentsAsync("../fremd");

        Assert.That(loaded.Any(d => d.FileName == "geheim.pdf"), Is.False);
    }

    [TestCase("Standard", "Standard")]
    [TestCase("BGB: Teil 1", null)]   // ':' ist nur unter Windows unzulässig → plattformabhängig, siehe unten
    [TestCase("Jura - Teil 1", "Jura - Teil 1")]
    [TestCase("", "Standard")]
    [TestCase("   ", "Standard")]
    [TestCase("a/b", "a_b")]
    [TestCase("a\\b", "a_b")]
    [TestCase("..", null)]            // Windows: "_" (Punkte am Ende entfernt), Linux: "__"
    public void ToSafeProfileFileName_LiefertSicherenNamen(string input, string? erwartet)
    {
        var ergebnis = JsonStorageService.ToSafeProfileFileName(input);

        if (erwartet != null)
            Assert.That(ergebnis, Is.EqualTo(erwartet));

        Assert.That(ergebnis, Does.Not.Contain("/").And.Not.Contain("\\"));
    }

    [Test]
    public void ToSafeProfileFileName_GueltigerNameBleibtUnveraendert()
    {
        // Bestehende Profildateien müssen nach dem Update weiter gefunden werden
        foreach (var name in new[] { "Testprofil", "Steuerrecht 2026", "Übung Ä Ö Ü", "Jura_Teil-1" })
            Assert.That(JsonStorageService.ToSafeProfileFileName(name), Is.EqualTo(name));
    }

    [Test]
    [Platform("Win")]
    public void ToSafeProfileFileName_Windows_ReservierteNamenUndDoppelpunkt()
    {
        Assert.That(JsonStorageService.ToSafeProfileFileName("CON"), Is.EqualTo("_CON"));
        Assert.That(JsonStorageService.ToSafeProfileFileName("nul"), Is.EqualTo("_nul"));
        Assert.That(JsonStorageService.ToSafeProfileFileName("COM1.backup"), Is.EqualTo("_COM1.backup"));
        Assert.That(JsonStorageService.ToSafeProfileFileName("BGB: Teil 1"), Is.EqualTo("BGB_ Teil 1"));
        Assert.That(JsonStorageService.ToSafeProfileFileName("Ende..."), Is.EqualTo("Ende"));
    }

    [Test]
    [Platform("Win")]
    public async Task SaveDocumentsAsync_Windows_ReservierterName_LegtNormaleDateiAn()
    {
        await _storageService.SaveDocumentsAsync("CON", new List<DocumentItem>());

        Assert.That(File.Exists(Path.Combine(_profilesDirectory, "_CON.json")), Is.True);
    }

    [Test]
    [Platform("Linux")]
    public void ToSafeProfileFileName_Linux_DoppelpunktBleibtFuerBestehendeProfile()
    {
        Assert.That(JsonStorageService.ToSafeProfileFileName("BGB: Teil 1"), Is.EqualTo("BGB: Teil 1"));
    }
}
