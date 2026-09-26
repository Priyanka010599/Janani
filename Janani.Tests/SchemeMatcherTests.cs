using Janani.Services;
using Xunit;

namespace Janani.Tests;

// Each test pins a rule to its source (see the header of SchemeMatcher.cs) -- if a scheme's
// rules change, these should fail loudly rather than quietly keep telling people yes.
public class SchemeMatcherTests
{
    private static SchemeInput Input(
        string? state = "Kerala", bool? rural = true, int? age = 26,
        bool expecting = true, bool lactating = false, bool infant = false,
        int births = 0, bool? disadvantaged = true, bool? govt = false) =>
        new(state, rural, age, expecting, lactating, infant, births, disadvantaged, govt);

    private static SchemeMatch Of(SchemeInput i, Scheme s) => SchemeMatcher.Match(i).Single(m => m.Scheme == s);

    // ── PMMVY ────────────────────────────────────────────────────────────

    [Fact]
    public void Pmmvy_FirstChild_IsFiveThousand()
    {
        var m = Of(Input(), Scheme.Pmmvy);
        Assert.Equal(MatchStatus.Likely, m.Status);
        Assert.Equal(5000, m.AmountRupees);
    }

    [Fact]
    public void Pmmvy_SecondChild_IsOnlyPossible_BecauseTheSixThousandNeedsAGirl()
    {
        var m = Of(Input(births: 1), Scheme.Pmmvy);
        Assert.Equal(MatchStatus.Possible, m.Status);
        Assert.Equal(6000, m.AmountRupees);
        Assert.Contains("PmmvySecondChildGirlOnly", m.Reasons);
    }

    [Fact]
    public void Pmmvy_ThirdChild_IsUnlikely()
    {
        var m = Of(Input(births: 2), Scheme.Pmmvy);
        Assert.Equal(MatchStatus.Unlikely, m.Status);
        Assert.Null(m.AmountRupees);
    }

    [Fact]
    public void Pmmvy_GovernmentEmployee_IsExcluded()
    {
        var m = Of(Input(govt: true), Scheme.Pmmvy);
        Assert.Equal(MatchStatus.Unlikely, m.Status);
        Assert.Contains("PmmvyGovtEmployee", m.Reasons);
    }

    [Theory]
    [InlineData(17, MatchStatus.Unlikely)]
    [InlineData(18, MatchStatus.Possible)]   // band starts at 18y 7m, so 18 needs confirming
    [InlineData(19, MatchStatus.Likely)]
    [InlineData(55, MatchStatus.Likely)]
    [InlineData(56, MatchStatus.Unlikely)]
    public void Pmmvy_AgeBand(int age, MatchStatus expected) =>
        Assert.Equal(expected, Of(Input(age: age), Scheme.Pmmvy).Status);

    [Fact]
    public void Pmmvy_UnknownAnswers_NeverCountAsYes()
    {
        var m = Of(Input(age: null, govt: null, disadvantaged: null), Scheme.Pmmvy);
        Assert.Equal(MatchStatus.Possible, m.Status);
    }

    [Fact]
    public void Pmmvy_NotDisadvantaged_IsAPromptToConfirm_NotARejection()
    {
        Assert.Equal(MatchStatus.Possible, Of(Input(disadvantaged: false), Scheme.Pmmvy).Status);
    }

    [Fact]
    public void Pmmvy_LactatingMother_Qualifies()
    {
        var m = Of(Input(expecting: false, lactating: true), Scheme.Pmmvy);
        Assert.Equal(MatchStatus.Likely, m.Status);
    }

    [Fact]
    public void Pmmvy_NotPregnantOrLactating_IsUnlikely()
    {
        Assert.Equal(MatchStatus.Unlikely, Of(Input(expecting: false), Scheme.Pmmvy).Status);
    }

    // ── JSY ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Uttar Pradesh", true, 1400)]
    [InlineData("Bihar", false, 1000)]
    [InlineData("odisha", true, 1400)]   // case-insensitive
    public void Jsy_LowPerformingState_PaysTheSourcedAmount(string state, bool rural, int expected)
    {
        var m = Of(Input(state: state, rural: rural, disadvantaged: false, age: 17, births: 3), Scheme.Jsy);
        // Wider rule in these states: category, age and parity don't gate it.
        Assert.Equal(MatchStatus.Likely, m.Status);
        Assert.Equal(expected, m.AmountRupees);
    }

    [Fact]
    public void Jsy_LowPerformingState_UnknownRuralUrban_IsPossibleWithNoAmount()
    {
        var m = Of(Input(state: "Bihar", rural: null), Scheme.Jsy);
        Assert.Equal(MatchStatus.Possible, m.Status);
        Assert.Null(m.AmountRupees);
    }

    [Fact]
    public void Jsy_OtherState_NeedsCategoryAgeAndParity_AndNeverGuessesAnAmount()
    {
        var ok = Of(Input(state: "Kerala", disadvantaged: true, age: 24, births: 1), Scheme.Jsy);
        Assert.Equal(MatchStatus.Likely, ok.Status);
        Assert.Null(ok.AmountRupees);
        Assert.Contains("JsyConfirmAmount", ok.Reasons);

        Assert.Equal(MatchStatus.Unlikely, Of(Input(state: "Kerala", disadvantaged: false), Scheme.Jsy).Status);
        Assert.Equal(MatchStatus.Unlikely, Of(Input(state: "Kerala", age: 18), Scheme.Jsy).Status);
        Assert.Equal(MatchStatus.Unlikely, Of(Input(state: "Kerala", births: 2), Scheme.Jsy).Status);
    }

    [Fact]
    public void Jsy_NoState_IsPossible_NotGuessed()
    {
        Assert.Equal(MatchStatus.Possible, Of(Input(state: null), Scheme.Jsy).Status);
    }

    // ── JSSK / UIP ───────────────────────────────────────────────────────

    [Fact]
    public void Jssk_AppliesToAnyoneExpectingDeliveredOrWithANewborn()
    {
        Assert.Equal(MatchStatus.Likely, Of(Input(expecting: true), Scheme.Jssk).Status);
        Assert.Equal(MatchStatus.Likely, Of(Input(expecting: false, infant: true), Scheme.Jssk).Status);
        Assert.Equal(MatchStatus.Unlikely, Of(Input(expecting: false), Scheme.Jssk).Status);
    }

    [Fact]
    public void Uip_NeedsAnInfant()
    {
        Assert.Equal(MatchStatus.Likely, Of(Input(expecting: false, infant: true), Scheme.Uip).Status);
        Assert.Equal(MatchStatus.Unlikely, Of(Input(), Scheme.Uip).Status);
    }

    [Fact]
    public void Match_AlwaysReturnsAllFourSchemes()
    {
        Assert.Equal(4, SchemeMatcher.Match(Input()).Count);
    }
}
