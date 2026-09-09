using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class EpdsScreeningCheckerTests
{
    private static EpdsScreening Zero() => new()
    {
        ScheduledDay = 10,
        Item1 = 0, Item2 = 0, Item3 = 0, Item4 = 0, Item5 = 0,
        Item6 = 0, Item7 = 0, Item8 = 0, Item9 = 0, Item10 = 0
    };

    [Fact]
    public void AllZero_ProducesNoConcerns()
    {
        Assert.Empty(EpdsScreeningChecker.Check(Zero()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Item10_AnyNonzeroValue_EscalatesCriticalRegardlessOfTotalScore(int item10Value)
    {
        // Total score is 0 except for item 10 -- confirms the escalation
        // is on item 10 alone, not on the total crossing a threshold.
        var screening = Zero();
        screening.Item10 = item10Value;

        var concerns = EpdsScreeningChecker.Check(screening);

        Assert.Contains(concerns, c => c.Description.Contains("self-harm") && c.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public void Item10Zero_HighTotalScore_DoesNotRaiseSelfHarmConcern()
    {
        var screening = Zero();
        screening.Item1 = 3; screening.Item2 = 3; screening.Item3 = 3; screening.Item4 = 3;
        screening.Item5 = 3; screening.Item6 = 3; screening.Item7 = 3; screening.Item8 = 3; screening.Item9 = 3;
        // Item10 stays 0.

        var concerns = EpdsScreeningChecker.Check(screening);

        Assert.DoesNotContain(concerns, c => c.Description.Contains("self-harm"));
        Assert.Equal(27, screening.TotalScore);
    }

    [Theory]
    [InlineData(9, null)]
    [InlineData(10, AlertSeverity.Warning)]
    [InlineData(12, AlertSeverity.Warning)]
    [InlineData(13, AlertSeverity.Critical)]
    public void TotalScore_TwoTierThreshold_FlagsCorrectSeverity(int desiredTotal, AlertSeverity? expected)
    {
        // Spread the desired total across items 1-9 (item 10 stays 0 so
        // this test isolates the total-score threshold from the item-10
        // override tested separately above).
        var screening = Zero();
        var remaining = desiredTotal;
        foreach (var setter in new Action<int>[]
                 {
                     v => screening.Item1 = v, v => screening.Item2 = v, v => screening.Item3 = v,
                     v => screening.Item4 = v, v => screening.Item5 = v, v => screening.Item6 = v,
                     v => screening.Item7 = v, v => screening.Item8 = v, v => screening.Item9 = v
                 })
        {
            var v = Math.Min(3, remaining);
            setter(v);
            remaining -= v;
        }

        Assert.Equal(desiredTotal, screening.TotalScore);
        var concerns = EpdsScreeningChecker.Check(screening);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("EPDS score"));
        else
            Assert.Contains(concerns, c => c.Description.Contains("EPDS score") && c.Severity == expected);
    }

    [Fact]
    public void Item10AndHighTotal_BothConcernsPresent()
    {
        var screening = Zero();
        screening.Item1 = 3; screening.Item2 = 3; screening.Item3 = 3; screening.Item4 = 3;
        screening.Item5 = 3; screening.Item6 = 1;
        screening.Item10 = 2;

        var concerns = EpdsScreeningChecker.Check(screening);

        Assert.Contains(concerns, c => c.Description.Contains("self-harm"));
        Assert.Contains(concerns, c => c.Description.Contains("EPDS score"));
    }

    // "No different threshold for bereaved profiles" -- structural, not
    // just behavioral: Check() takes only an EpdsScreening, no
    // BirthOutcome/CareContext parameter exists to even pass one in.
    [Fact]
    public void CheckSignature_TakesNoBereavementOrCareContextInput()
    {
        var method = typeof(EpdsScreeningChecker).GetMethod(nameof(EpdsScreeningChecker.Check));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(EpdsScreening), parameters[0].ParameterType);
    }
}
