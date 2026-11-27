using StudyScope.Core.Common;
using StudyScope.ViewModels;
using StudyScope.ViewModels.Menu;

namespace StudyScope.Tests.ViewModels;

/// <summary>Einträge des Hamburger-Menüs, Spendeneintrag und Sperre im Vortrag.</summary>
[TestFixture]
public class AppMenuViewModelTests
{
    private static IEnumerable<string> AllIds(IEnumerable<MenuEntry> items) =>
        items.SelectMany(i => new[] { i.Id }.Concat(i.Children is null ? [] : AllIds(i.Children)));

    [Test]
    public void Items_ContainAllEntriesInOrder()
    {
        var items = AppMenuViewModel.BuildItems(donationAvailable: true, appearanceAvailable: true);
        Assert.That(items.Select(i => i.Id), Is.EqualTo(new[]
        {
            AppMenuViewModel.OpenFile, AppMenuViewModel.NewProfile, AppMenuViewModel.Settings,
            AppMenuViewModel.Help, AppMenuViewModel.Exit
        }));
        Assert.That(items[2].Children!.Select(c => c.Id), Is.EqualTo(new[]
        {
            AppMenuViewModel.SettingsAi, AppMenuViewModel.SettingsSpeech, AppMenuViewModel.SettingsExport,
            AppMenuViewModel.SettingsWebSearch, AppMenuViewModel.SettingsAppearance
        }));
        Assert.That(items[3].Children!.Select(c => c.Id), Is.EqualTo(new[] { AppMenuViewModel.About, AppMenuViewModel.Support }));
        Assert.That(items[4].IsDanger, Is.True);
    }

    [Test]
    public void EmptyDonationUrl_HidesSupportEntry()
    {
        var items = AppMenuViewModel.BuildItems(AppInfo.IsValidDonationUrl(""), appearanceAvailable: false);
        Assert.That(AllIds(items), Does.Not.Contain(AppMenuViewModel.Support));
    }

    [Test]
    public void ValidDonationUrl_ShowsSupportEntry()
    {
        var items = AppMenuViewModel.BuildItems(AppInfo.IsValidDonationUrl("https://example.org/spenden"), appearanceAvailable: false);
        Assert.That(AllIds(items), Does.Contain(AppMenuViewModel.Support));
    }

    [TestCase("http://example.org/spenden")]
    [TestCase("javascript:alert(1)")]
    [TestCase("   ")]
    public void DonationUrl_MustBeHttps(string url) =>
        Assert.That(AppInfo.IsValidDonationUrl(url), Is.False);

    [Test]
    public void ConfiguredDonationUrl_IsValid() =>
        Assert.That(AppInfo.IsValidDonationUrl(AppInfo.DonationUrl), Is.True);

    [Test]
    public void AppearanceEntry_OnlyWhenAvailable()
    {
        var items = AppMenuViewModel.BuildItems(donationAvailable: false, appearanceAvailable: false);
        Assert.That(AllIds(items), Does.Not.Contain(AppMenuViewModel.SettingsAppearance));
    }

    [Test]
    public void SettingsSectionOf_MapsIdsToSections()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AppMenuViewModel.SettingsSectionOf(AppMenuViewModel.SettingsAi), Is.EqualTo("ai"));
            Assert.That(AppMenuViewModel.SettingsSectionOf(AppMenuViewModel.SettingsSpeech), Is.EqualTo("speech"));
            Assert.That(AppMenuViewModel.SettingsSectionOf(AppMenuViewModel.SettingsExport), Is.EqualTo("export"));
            Assert.That(AppMenuViewModel.SettingsSectionOf(AppMenuViewModel.SettingsWebSearch), Is.EqualTo("websearch"));
            Assert.That(AppMenuViewModel.SettingsSectionOf(AppMenuViewModel.About), Is.Null);
        });
    }

    [Test]
    public void NotAvailableWhilePresenting()
    {
        Assert.That(AppMenuViewModel.IsAvailable(isPresenting: true), Is.False);
        Assert.That(AppMenuViewModel.IsAvailable(isPresenting: false), Is.True);
    }

    [Test]
    public void CtrlB_IsIgnoredWhileMenuIsOpen()
    {
        var key = new ShortcutKey("b", Ctrl: true);
        Assert.That(LayoutViewModel.IsToggleShortcut(key, isPresenting: false, menuOpen: true), Is.False);
        Assert.That(LayoutViewModel.IsToggleShortcut(key, isPresenting: false, menuOpen: false), Is.True);
    }

    [Test]
    public void KeyboardPath_OpensSpeechSettings()
    {
        var vm = new AppMenuViewModel();
        var nav = vm.Navigator;
        nav.Open();
        nav.HandleKey("ArrowDown");
        nav.HandleKey("ArrowDown");        // Einstellungen
        nav.HandleKey("ArrowRight");       // KI-Modell
        nav.HandleKey("ArrowDown");        // Sprachausgabe
        var result = nav.HandleKey("Enter");
        Assert.That(result.ActivatedId, Is.EqualTo(AppMenuViewModel.SettingsSpeech));
    }
}
