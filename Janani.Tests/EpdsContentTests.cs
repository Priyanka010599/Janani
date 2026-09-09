using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class EpdsContentTests
{
    [Fact]
    public void ExactlyTenItems_NumberedOneThroughTen_EachWithFourOptions()
    {
        Assert.Equal(10, EpdsContent.EnglishItems.Count);
        Assert.Equal(Enumerable.Range(1, 10), EpdsContent.EnglishItems.Select(i => i.Number));
        Assert.All(EpdsContent.EnglishItems, i => Assert.Equal(4, i.Options.Length));
    }

    // Items 1, 2, 4 are ascending (first option = 0, last = 3); items 3, 5,
    // 6, 7, 8, 9, 10 are descending (first option = 3, last = 0) — per the
    // published scale. Getting this backwards would silently invert real
    // depression screening scores.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    public void DescendingScoreFlag_MatchesPublishedScaleForEveryItem(int itemNumber, bool expectedDescending)
    {
        var item = EpdsContent.EnglishItems.Single(i => i.Number == itemNumber);
        Assert.Equal(expectedDescending, item.DescendingScore);
    }

    [Fact]
    public void AscendingItem_ScoresSelectionIndexDirectly()
    {
        var item1 = EpdsContent.EnglishItems.Single(i => i.Number == 1);
        Assert.Equal(0, EpdsContent.ScoreForSelection(item1, selectedOptionIndex: 0));
        Assert.Equal(3, EpdsContent.ScoreForSelection(item1, selectedOptionIndex: 3));
    }

    [Fact]
    public void DescendingItem_ScoresSelectionIndexInReverse()
    {
        var item3 = EpdsContent.EnglishItems.Single(i => i.Number == 3);
        Assert.Equal(3, EpdsContent.ScoreForSelection(item3, selectedOptionIndex: 0));
        Assert.Equal(0, EpdsContent.ScoreForSelection(item3, selectedOptionIndex: 3));
    }

    [Fact]
    public void Item10_SelfHarmItem_IsDescendingSoFirstOptionIsHighestRisk()
    {
        // "Yes, quite often" (index 0) must map to 3, matching
        // EpdsScreeningChecker's Item10 > 0 escalation being meaningful.
        var item10 = EpdsContent.EnglishItems.Single(i => i.Number == 10);
        Assert.Equal("Yes, quite often", item10.Options[0]);
        Assert.Equal("Never", item10.Options[3]);
        Assert.Equal(3, EpdsContent.ScoreForSelection(item10, selectedOptionIndex: 0));
        Assert.Equal(0, EpdsContent.ScoreForSelection(item10, selectedOptionIndex: 3));
    }

    // "Mark other languages unavailable rather than falling back" —
    // English is the only currently-available language; every other
    // AppLanguage value must report unavailable, not silently substitute.
    [Fact]
    public void OnlyEnglish_IsAvailable()
    {
        Assert.True(EpdsContent.IsLanguageAvailable(AppLanguage.English));

        foreach (var language in Enum.GetValues<AppLanguage>().Where(l => l != AppLanguage.English))
        {
            Assert.False(EpdsContent.IsLanguageAvailable(language));
        }
    }

    [Fact]
    public void HindiAndTelugu_AreNotYetAvailable()
    {
        // Explicit regression guard: don't let these get silently added
        // without sourcing/verifying real validated translation text
        // first (see the comment on AvailableLanguages).
        Assert.False(EpdsContent.IsLanguageAvailable(AppLanguage.Hindi));
        Assert.False(EpdsContent.IsLanguageAvailable(AppLanguage.Telugu));
    }
}
