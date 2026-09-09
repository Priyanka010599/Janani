using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class PostpartumRecoveryCheckerTests
{
    private static PostpartumCheckIn Normal() => new()
    {
        PainLevel = 3,
        Bleeding = BleedingLevel.Light,
        TemperatureC = 36.8m,
        Wound = WoundStatus.HealingWell,
        Mood = MoodType.Peaceful
    };

    [Fact]
    public void AllNormalValues_ProduceNoConcerns()
    {
        var concerns = PostpartumRecoveryChecker.Check(Normal());
        Assert.Empty(concerns);
    }

    [Theory]
    [InlineData(7, null)]
    [InlineData(8, AlertSeverity.Warning)]
    [InlineData(9, AlertSeverity.Warning)]
    [InlineData(10, AlertSeverity.Critical)]
    public void PainLevel_TwoTierRange_FlagsCorrectSeverity(int pain, AlertSeverity? expected)
    {
        var checkIn = Normal();
        checkIn.PainLevel = pain;
        var concerns = PostpartumRecoveryChecker.Check(checkIn);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("Pain level"));
        else
            Assert.Contains(concerns, c => c.Description.Contains("Pain level") && c.Severity == expected);
    }

    [Theory]
    [InlineData(BleedingLevel.Moderate, null)]
    [InlineData(BleedingLevel.Heavy, AlertSeverity.Warning)]
    [InlineData(BleedingLevel.SoakingPadHourly, AlertSeverity.Critical)]
    public void Bleeding_FlagsCorrectSeverity(BleedingLevel bleeding, AlertSeverity? expected)
    {
        var checkIn = Normal();
        checkIn.Bleeding = bleeding;
        var concerns = PostpartumRecoveryChecker.Check(checkIn);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("Bleeding") || c.Description.Contains("Soaking"));
        else
            Assert.Contains(concerns, c => c.Severity == expected && (c.Description.Contains("Bleeding") || c.Description.Contains("Soaking")));
    }

    [Theory]
    [InlineData(37.9, null)]
    [InlineData(38.0, AlertSeverity.Warning)]
    [InlineData(38.9, AlertSeverity.Warning)]
    [InlineData(39.0, AlertSeverity.Critical)]
    public void Temperature_TwoTierFeverRange_FlagsCorrectSeverity(double temp, AlertSeverity? expected)
    {
        var checkIn = Normal();
        checkIn.TemperatureC = (decimal)temp;
        var concerns = PostpartumRecoveryChecker.Check(checkIn);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("Temperature"));
        else
            Assert.Contains(concerns, c => c.Description.Contains("Temperature") && c.Severity == expected);
    }

    [Fact]
    public void NullTemperature_IsNotFlagged()
    {
        var checkIn = Normal();
        checkIn.TemperatureC = null;
        var concerns = PostpartumRecoveryChecker.Check(checkIn);
        Assert.DoesNotContain(concerns, c => c.Description.Contains("Temperature"));
    }

    [Theory]
    [InlineData(WoundStatus.HealingWell, null)]
    [InlineData(WoundStatus.MildRednessOrSwelling, AlertSeverity.Warning)]
    [InlineData(WoundStatus.SignificantConcern, AlertSeverity.Critical)]
    public void Wound_FlagsCorrectSeverity(WoundStatus wound, AlertSeverity? expected)
    {
        var checkIn = Normal();
        checkIn.Wound = wound;
        var concerns = PostpartumRecoveryChecker.Check(checkIn);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("wound") || c.Description.Contains("redness"));
        else
            Assert.Contains(concerns, c => c.Severity == expected && (c.Description.Contains("wound") || c.Description.Contains("redness")));
    }

    [Fact]
    public void Mood_NeverProducesAConcern()
    {
        // Mood is informational context for the agent, not itself a
        // trigger -- a single day's self-reported mood word isn't a
        // diagnosis. EPDS-10 (EpdsScreeningChecker) is the validated
        // instrument for that.
        foreach (var mood in Enum.GetValues<MoodType>())
        {
            var checkIn = Normal();
            checkIn.Mood = mood;
            Assert.Empty(PostpartumRecoveryChecker.Check(checkIn));
        }
    }

    [Fact]
    public void MultipleOutOfRangeValues_FlagEachIndependently()
    {
        var checkIn = Normal();
        checkIn.PainLevel = 9;
        checkIn.Bleeding = BleedingLevel.SoakingPadHourly;
        checkIn.TemperatureC = 39.5m;
        checkIn.Wound = WoundStatus.SignificantConcern;

        var concerns = PostpartumRecoveryChecker.Check(checkIn);
        Assert.Equal(4, concerns.Count);
    }
}

public class PostpartumCheckInQuestionsTests
{
    [Theory]
    [InlineData(DeliveryType.PlannedCesarean)]
    [InlineData(DeliveryType.EmergencyCesarean)]
    public void CesareanDeliveries_AskAboutIncision(DeliveryType type)
    {
        Assert.Contains("incision", PostpartumCheckInQuestions.WoundQuestionFor(type), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DeliveryType.Vaginal)]
    [InlineData(DeliveryType.VaginalWithTearOrEpisiotomy)]
    [InlineData(DeliveryType.AssistedVaginal)]
    [InlineData(DeliveryType.VBAC)]
    public void VaginalFamilyDeliveries_AskAboutPerineum(DeliveryType type)
    {
        Assert.Contains("perineum", PostpartumCheckInQuestions.WoundQuestionFor(type), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullDeliveryType_AsksGenericQuestion()
    {
        var question = PostpartumCheckInQuestions.WoundQuestionFor(null);
        Assert.Contains("incision", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("perineum", question, StringComparison.OrdinalIgnoreCase);
    }
}
