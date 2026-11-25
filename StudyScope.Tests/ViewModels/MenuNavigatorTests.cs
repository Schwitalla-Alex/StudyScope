using StudyScope.ViewModels.Menu;

namespace StudyScope.Tests.ViewModels;

/// <summary>Tastatur- und Mausbedienung des Ausklappmenüs (Hamburger-Menü, „» Mehr“) ohne Oberfläche.</summary>
[TestFixture]
public class MenuNavigatorTests
{
    private static MenuNavigator Create()
    {
        var nav = new MenuNavigator("test");
        nav.SetItems(
        [
            new("a", "A"),
            new("sub", "Untermenü", Children: [new("s1", "S1"), new("s2", "S2"), new("s3", "S3")]),
            new("c", "C"),
        ]);
        return nav;
    }

    [Test]
    public void Open_HighlightsFirstItem_AndFocusesIt()
    {
        var nav = Create();
        nav.Open();
        Assert.Multiple(() =>
        {
            Assert.That(nav.IsOpen, Is.True);
            Assert.That(nav.ActiveIndex, Is.EqualTo(0));
            Assert.That(nav.FocusedElementId, Is.EqualTo("test-item-0"));
        });
    }

    [Test]
    public void Open_FocusLast_HighlightsLastItem()
    {
        var nav = Create();
        nav.Open(focusLast: true);
        Assert.That(nav.ActiveIndex, Is.EqualTo(2));
    }

    [Test]
    public void ClosedMenu_IgnoresKeys()
    {
        var nav = Create();
        var result = nav.HandleKey("ArrowDown");
        Assert.That(result.Handled, Is.False);
        Assert.That(nav.FocusedElementId, Is.Null);
    }

    [Test]
    public void ArrowKeys_WrapAround()
    {
        var nav = Create();
        nav.Open();
        nav.HandleKey("ArrowUp");
        Assert.That(nav.ActiveIndex, Is.EqualTo(2));
        nav.HandleKey("ArrowDown");
        Assert.That(nav.ActiveIndex, Is.EqualTo(0));
    }

    [Test]
    public void HomeAndEnd_JumpToEnds()
    {
        var nav = Create();
        nav.Open();
        nav.HandleKey("End");
        Assert.That(nav.ActiveIndex, Is.EqualTo(2));
        nav.HandleKey("Home");
        Assert.That(nav.ActiveIndex, Is.EqualTo(0));
    }

    [Test]
    public void Enter_OnLeaf_ActivatesAndClosesWithFocusReturn()
    {
        var nav = Create();
        nav.Open();
        var result = nav.HandleKey("Enter");
        Assert.Multiple(() =>
        {
            Assert.That(result.ActivatedId, Is.EqualTo("a"));
            Assert.That(result.Closed, Is.True);
            Assert.That(result.ReturnFocus, Is.True);
            Assert.That(nav.IsOpen, Is.False);
        });
    }

    [Test]
    public void Space_ActivatesLikeEnter()
    {
        var nav = Create();
        nav.Open(focusLast: true);
        Assert.That(nav.HandleKey(" ").ActivatedId, Is.EqualTo("c"));
    }

    [TestCase("ArrowRight")]
    [TestCase("Enter")]
    [TestCase(" ")]
    public void OpensSubmenu_WithFirstChildFocused(string key)
    {
        var nav = Create();
        nav.Open();
        nav.HandleKey("ArrowDown");
        var result = nav.HandleKey(key);
        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.ActivatedId, Is.Null);
            Assert.That(nav.SubmenuIndex, Is.EqualTo(1));
            Assert.That(nav.SubActiveIndex, Is.EqualTo(0));
            Assert.That(nav.FocusedElementId, Is.EqualTo("test-item-1-0"));
        });
    }

    [Test]
    public void ArrowRight_OnLeaf_DoesNothing()
    {
        var nav = Create();
        nav.Open();
        nav.HandleKey("ArrowRight");
        Assert.That(nav.SubmenuIndex, Is.Null);
    }

    [Test]
    public void Submenu_ArrowsWrap_AndEnterActivatesChild()
    {
        var nav = Create();
        nav.Open();
        nav.HandleKey("ArrowDown");
        nav.HandleKey("ArrowRight");
        nav.HandleKey("ArrowUp");
        Assert.That(nav.SubActiveIndex, Is.EqualTo(2));
        var result = nav.HandleKey("Enter");
        Assert.Multiple(() =>
        {
            Assert.That(result.ActivatedId, Is.EqualTo("s3"));
            Assert.That(result.ReturnFocus, Is.True);
            Assert.That(nav.IsOpen, Is.False);
            Assert.That(nav.SubmenuIndex, Is.Null);
        });
    }

    [TestCase("ArrowLeft")]
    [TestCase("Escape")]
    public void Submenu_LeftOrEscape_ClosesOneLevel(string key)
    {
        var nav = Create();
        nav.Open();
        nav.HandleKey("ArrowDown");
        nav.HandleKey("ArrowRight");
        var result = nav.HandleKey(key);
        Assert.Multiple(() =>
        {
            Assert.That(result.Closed, Is.False);
            Assert.That(nav.IsOpen, Is.True);
            Assert.That(nav.SubmenuIndex, Is.Null);
            Assert.That(nav.FocusedElementId, Is.EqualTo("test-item-1"));
        });
    }

    [Test]
    public void Escape_OnTopLevel_ClosesWithFocusReturn()
    {
        var nav = Create();
        nav.Open();
        var result = nav.HandleKey("Escape");
        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Closed, Is.True);
            Assert.That(result.ReturnFocus, Is.True);
            Assert.That(nav.IsOpen, Is.False);
        });
    }

    [Test]
    public void Tab_ClosesWithoutHandling_SoFocusMovesOn()
    {
        var nav = Create();
        nav.Open();
        nav.HandleKey("ArrowDown");
        nav.HandleKey("ArrowRight");
        var result = nav.HandleKey("Tab");
        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.False);
            Assert.That(result.Closed, Is.True);
            Assert.That(result.ReturnFocus, Is.False);
            Assert.That(nav.IsOpen, Is.False);
        });
    }

    [Test]
    public void OtherKeys_AreNotHandled()
    {
        var nav = Create();
        nav.Open();
        Assert.That(nav.HandleKey("b").Handled, Is.False);
        Assert.That(nav.IsOpen, Is.True);
    }

    [Test]
    public void MovingAwayFromParent_ClosesItsSubmenu()
    {
        var nav = Create();
        nav.Open();
        nav.Hover(1);
        Assert.That(nav.SubmenuIndex, Is.EqualTo(1));
        nav.HandleKey("ArrowDown");
        Assert.That(nav.SubmenuIndex, Is.Null);
    }

    [Test]
    public void Click_OnParent_TogglesSubmenu_OnLeafActivates()
    {
        var nav = Create();
        nav.Open();
        Assert.That(nav.Click(1), Is.Null);
        Assert.That(nav.SubmenuIndex, Is.EqualTo(1));
        Assert.That(nav.Click(1), Is.Null);
        Assert.That(nav.SubmenuIndex, Is.Null);
        Assert.That(nav.Click(2), Is.EqualTo("c"));
        Assert.That(nav.IsOpen, Is.False);
    }

    [Test]
    public void ClickSub_ActivatesChild()
    {
        var nav = Create();
        nav.Open();
        nav.Click(1);
        Assert.That(nav.ClickSub(1, 1), Is.EqualTo("s2"));
        Assert.That(nav.IsOpen, Is.False);
    }

    [Test]
    public void Hover_OnLeaf_ClosesOpenSubmenu()
    {
        var nav = Create();
        nav.Open();
        nav.Hover(1);
        nav.Hover(0);
        Assert.That(nav.SubmenuIndex, Is.Null);
        Assert.That(nav.ActiveIndex, Is.EqualTo(0));
    }

    [Test]
    public void Toggle_OpensAndCloses()
    {
        var nav = Create();
        nav.Toggle();
        Assert.That(nav.IsOpen, Is.True);
        nav.Toggle();
        Assert.That(nav.IsOpen, Is.False);
        Assert.That(nav.ActiveIndex, Is.EqualTo(-1));
    }

    [Test]
    public void EmptyMenu_DoesNotOpen()
    {
        var nav = new MenuNavigator("leer");
        nav.Open();
        Assert.That(nav.IsOpen, Is.False);
    }
}
