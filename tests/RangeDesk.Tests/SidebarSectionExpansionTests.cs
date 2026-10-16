using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class SidebarSectionExpansionTests
{
    [Fact]
    public void GroupsInitiallyExpandIndependently()
    {
        var sections = new SidebarSectionExpansion();
        Assert.True(sections.IsExpanded("categories"));
        Assert.True(sections.IsExpanded("labels"));
        Assert.True(sections.IsExpanded("sites"));
        sections = sections.Toggle("labels");
        Assert.True(sections.IsExpanded("categories"));
        Assert.False(sections.IsExpanded("labels"));
        Assert.True(sections.IsExpanded("sites"));
        sections = sections.Toggle("sites");
        Assert.False(sections.IsExpanded("labels"));
        Assert.False(sections.IsExpanded("sites"));
    }

    [Fact]
    public void CompactSidebarDoesNotDestroyExpansionPreferences()
    {
        var state = new SidebarSectionExpansion().Toggle("categories");
        Assert.False(state.IsVisible("categories", false));
        Assert.True(state.IsVisible("categories", true));
        Assert.True(state.IsVisible("labels", true));
        Assert.True(state.IsVisible("sites", true));
        Assert.True(state.IsVisible("labels", false));
        Assert.False(state.Toggle("labels").IsExpanded("labels"));
        // Re-expanding returns to the saved group preferences.
        Assert.False(state.IsVisible("categories", false));
        Assert.True(state.IsVisible("sites", false));
    }

    [Fact]
    public void UnrecognizedSectionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SidebarSectionExpansion().Toggle("history"));
    }
}
