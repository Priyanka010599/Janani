using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class GrowthThresholdCheckerTests
{
    private static InfantProfile Infant(int ageInWeeks) => new()
    {
        Name = "Test Baby",
        DateOfBirth = DateOnly.FromDateTime(DateTime.Today.AddDays(-ageInWeeks * 7))
    };

    [Fact]
    public void WeightWithinTypicalRange_NoFirstEntry_ProducesNoConcerns()
    {
        var infant = Infant(ageInWeeks: 10);
        var entry = new GrowthEntry { WeightKg = 5.0m, RecordedAt = DateTime.UtcNow };

        var concerns = GrowthThresholdChecker.Check(infant, entry, previous: null);

        Assert.Empty(concerns);
    }

    [Theory]
    [InlineData(4, 1.5)]   // below the <=4wk band (2.0-5.5)
    [InlineData(4, 6.0)]   // above the <=4wk band
    [InlineData(26, 2.5)]  // below the <=26wk band (3.0-10.5)
    [InlineData(52, 14.0)] // above the <=52wk band (5.0-13.5)
    public void WeightOutsideAgeBand_FlagsWarning(int ageWeeks, double weightKg)
    {
        var infant = Infant(ageWeeks);
        var entry = new GrowthEntry { WeightKg = (decimal)weightKg, RecordedAt = DateTime.UtcNow };

        var concerns = GrowthThresholdChecker.Check(infant, entry, previous: null);

        Assert.Contains(concerns, c => c.Description.Contains("outside the typical range") && c.Severity == AlertSeverity.Warning);
    }

    [Fact]
    public void WeightDrop_AfterNewbornWindow_FlagsWarning()
    {
        var infant = Infant(ageInWeeks: 10); // > 4 weeks, past the normal newborn-dip exemption
        var previous = new GrowthEntry { WeightKg = 5.0m, RecordedAt = DateTime.UtcNow.AddDays(-10) };
        var latest = new GrowthEntry { WeightKg = 4.8m, RecordedAt = DateTime.UtcNow };

        var concerns = GrowthThresholdChecker.Check(infant, latest, previous);

        Assert.Contains(concerns, c => c.Description.Contains("dropped"));
    }

    [Fact]
    public void WeightDrop_WithinNewbornWindow_IsNotFlaggedAsADrop()
    {
        // Under 4 weeks old — a small weight dip is normal/expected, so the
        // drop-specific concern should not fire (the age-band check still
        // can independently, but that's covered by a separate test).
        var infant = Infant(ageInWeeks: 2);
        var previous = new GrowthEntry { WeightKg = 3.5m, RecordedAt = DateTime.UtcNow.AddDays(-5) };
        var latest = new GrowthEntry { WeightKg = 3.4m, RecordedAt = DateTime.UtcNow };

        var concerns = GrowthThresholdChecker.Check(infant, latest, previous);

        Assert.DoesNotContain(concerns, c => c.Description.Contains("dropped"));
    }

    [Fact]
    public void NoWeightGain_Over30Days_FlagsWarning()
    {
        var infant = Infant(ageInWeeks: 20);
        var previous = new GrowthEntry { WeightKg = 6.0m, RecordedAt = DateTime.UtcNow.AddDays(-31) };
        var latest = new GrowthEntry { WeightKg = 6.0m, RecordedAt = DateTime.UtcNow };

        var concerns = GrowthThresholdChecker.Check(infant, latest, previous);

        Assert.Contains(concerns, c => c.Description.Contains("No weight gain"));
    }

    [Fact]
    public void WeightGain_Over30Days_ProducesNoStalledConcern()
    {
        var infant = Infant(ageInWeeks: 20);
        var previous = new GrowthEntry { WeightKg = 6.0m, RecordedAt = DateTime.UtcNow.AddDays(-31) };
        var latest = new GrowthEntry { WeightKg = 6.3m, RecordedAt = DateTime.UtcNow };

        var concerns = GrowthThresholdChecker.Check(infant, latest, previous);

        Assert.DoesNotContain(concerns, c => c.Description.Contains("No weight gain"));
    }

    [Fact]
    public void NoWeightRecorded_ProducesNoConcerns()
    {
        var infant = Infant(ageInWeeks: 10);
        var entry = new GrowthEntry { WeightKg = null, HeightCm = 60, RecordedAt = DateTime.UtcNow };

        var concerns = GrowthThresholdChecker.Check(infant, entry, previous: null);

        Assert.Empty(concerns);
    }
}
