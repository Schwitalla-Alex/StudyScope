using StudyScope.ViewModels.Menu;

namespace StudyScope.Tests.ViewModels;

/// <summary>Einzeilige Reiterleiste: Aufteilung auf sichtbare Reiter und „» Mehr“.</summary>
[TestFixture]
public class TabOverflowTests
{
    private static readonly double[] Widths = [100, 100, 100, 100, 100];

    [Test]
    public void EverythingFits_NoOverflow()
    {
        var layout = TabOverflow.Compute(Widths, 500, 60, activeIndex: 0);
        Assert.That(layout.Visible, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
        Assert.That(layout.HasOverflow, Is.False);
    }

    [Test]
    public void TooNarrow_MovesTrailingTabsToOverflow()
    {
        // 360 - 60 („» Mehr“) = 300 → drei Reiter
        var layout = TabOverflow.Compute(Widths, 360, 60, activeIndex: 0);
        Assert.That(layout.Visible, Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(layout.Overflow, Is.EqualTo(new[] { 3, 4 }));
    }

    [Test]
    public void ActiveTabInOverflowRange_StaysVisible()
    {
        var layout = TabOverflow.Compute(Widths, 360, 60, activeIndex: 4);
        Assert.That(layout.Visible, Is.EqualTo(new[] { 0, 1, 4 }));
        Assert.That(layout.Overflow, Is.EqualTo(new[] { 2, 3 }));
    }

    [Test]
    public void VeryNarrow_KeepsOnlyActiveTab()
    {
        var layout = TabOverflow.Compute(Widths, 120, 60, activeIndex: 2);
        Assert.That(layout.Visible, Is.EqualTo(new[] { 2 }));
        Assert.That(layout.Overflow, Is.EqualTo(new[] { 0, 1, 3, 4 }));
    }

    [Test]
    public void OrderIsKept_NoSmallTabSqueezedInAfterGap()
    {
        double[] widths = [100, 200, 50];
        var layout = TabOverflow.Compute(widths, 300, 60, activeIndex: -1);
        Assert.That(layout.Visible, Is.EqualTo(new[] { 0 }));
        Assert.That(layout.Overflow, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void UnknownWidth_ShowsAll()
    {
        Assert.That(TabOverflow.Compute(Widths, 0, 60, 0).HasOverflow, Is.False);
        Assert.That(TabOverflow.Compute([100, double.NaN], 50, 60, 0).HasOverflow, Is.False);
    }

    [Test]
    public void NoTabs_EmptyLayout()
    {
        var layout = TabOverflow.Compute([], 100, 60, 0);
        Assert.That(layout.Visible, Is.Empty);
        Assert.That(layout.Overflow, Is.Empty);
    }

    [Test]
    public void AppTabs_HaveUniqueIdsAndKnownIndex()
    {
        Assert.That(AppTabs.All.Select(t => t.Id), Is.Unique);
        Assert.That(AppTabs.IndexOf(AppTabs.Reports), Is.EqualTo(AppTabs.All.Count - 1));
        Assert.That(AppTabs.IndexOf("gibt-es-nicht"), Is.EqualTo(-1));
    }
}
