using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class BereavementCareContextTests
{
    // ── CareContext resolution ────────────────────────────────────────────

    [Theory]
    [InlineData(null, false, true)]
    [InlineData(BirthOutcome.LiveBirth, false, true)]
    [InlineData(BirthOutcome.Stillbirth, true, false)]
    [InlineData(BirthOutcome.NeonatalDeath, true, false)]
    [InlineData(BirthOutcome.LateMiscarriage, true, false)]
    [InlineData(BirthOutcome.PartialLossMultiple, true, true)]
    public void CareContext_ResolvesIsBereavedAndHasSurvivingInfant(
        BirthOutcome? outcome, bool expectedIsBereaved, bool expectedHasSurvivingInfant)
    {
        var context = new CareContext(outcome);
        Assert.Equal(expectedIsBereaved, context.IsBereaved);
        Assert.Equal(expectedHasSurvivingInfant, context.HasAnySurvivingInfant);
        Assert.Equal(!expectedHasSurvivingInfant, context.SuppressInfantContent);
    }

    [Theory]
    [InlineData(BirthOutcome.Stillbirth, true)]
    [InlineData(BirthOutcome.NeonatalDeath, true)]
    [InlineData(BirthOutcome.LateMiscarriage, true)]
    [InlineData(BirthOutcome.PartialLossMultiple, false)] // surviving twin still feeds normally
    [InlineData(BirthOutcome.LiveBirth, false)]
    public void CareContext_SuppressesBreastfeedingGuidanceOnlyForFullLoss(BirthOutcome outcome, bool expectedSuppressed)
    {
        var context = new CareContext(outcome);
        Assert.Equal(expectedSuppressed, context.SuppressBreastfeedingGuidance);
    }

    [Theory]
    [InlineData(BirthOutcome.Stillbirth, true)]
    [InlineData(BirthOutcome.NeonatalDeath, true)]
    [InlineData(BirthOutcome.LateMiscarriage, true)]
    [InlineData(BirthOutcome.PartialLossMultiple, false)] // no separate suppression need of its own
    [InlineData(BirthOutcome.LiveBirth, false)]
    public void CareContext_ShowsLactationSuppressionGuidanceOnlyForFullLoss(BirthOutcome outcome, bool expectedShown)
    {
        var context = new CareContext(outcome);
        Assert.Equal(expectedShown, context.ShowLactationSuppressionGuidance);
    }

    [Theory]
    [InlineData(BirthOutcome.Stillbirth)]
    [InlineData(BirthOutcome.NeonatalDeath)]
    [InlineData(BirthOutcome.LateMiscarriage)]
    public void FullLoss_NeverBothHidesBreastfeedingAndSuppressesTheSuppressionGuidance(BirthOutcome outcome)
    {
        // The bug this split fixes: a single "suppress lactation" flag
        // would have also hidden the milk-suppression content itself.
        // These two must move in step, not be conflatable into one flag.
        var context = new CareContext(outcome);
        Assert.True(context.SuppressBreastfeedingGuidance);
        Assert.True(context.ShowLactationSuppressionGuidance);
    }

    [Fact]
    public void UserProfile_ExposesCareContextComputedFromBirthOutcome()
    {
        var profile = new UserProfile { BirthOutcome = BirthOutcome.Stillbirth };
        Assert.True(profile.CareContext.IsBereaved);
        Assert.False(profile.CareContext.HasAnySurvivingInfant);
    }

    // ── (f): no infant route is reachable for a bereaved profile ──────────
    // The actual route guard lives in InfantCare.razor / NavMenu.razor and
    // reads CareContext.HasAnySurvivingInfant directly — this is the value
    // both of those check, tested here at the level this test suite already
    // works at (deterministic logic, not rendered Blazor components).

    [Theory]
    [InlineData(BirthOutcome.Stillbirth)]
    [InlineData(BirthOutcome.NeonatalDeath)]
    [InlineData(BirthOutcome.LateMiscarriage)]
    public void FullLoss_InfantRouteIsUnreachable(BirthOutcome outcome)
    {
        var profile = new UserProfile { BirthOutcome = outcome };
        Assert.False(profile.CareContext.HasAnySurvivingInfant);
        Assert.True(profile.CareContext.SuppressInfantContent);
    }

    [Fact]
    public void PartialLossMultiple_InfantRouteStaysReachable()
    {
        var profile = new UserProfile { BirthOutcome = BirthOutcome.PartialLossMultiple };
        Assert.True(profile.CareContext.HasAnySurvivingInfant);
        Assert.False(profile.CareContext.SuppressInfantContent);
    }

    // ── (f): no schedule entry is reachable for a bereaved profile ────────

    [Fact]
    public void SuppressedVaccinationSchedule_IsAlwaysEmpty_RegardlessOfAgeOrGivenDoses()
    {
        var given = new List<VaccinationRecord> { new() { VaccineName = "BCG", GivenAt = DateTime.UtcNow } };

        var statuses = VaccinationSchedule.BuildStatus(ageInWeeks: 20, given, suppressed: true);

        Assert.Empty(statuses);
    }

    [Fact]
    public void PartialLossMultiple_StillGetsSurvivingChildsFullSchedule()
    {
        // The surviving child's own InfantProfile is real, so its own
        // CareContext-driven suppression flag (the mother's) is false —
        // the schedule renders exactly as it would for any living infant.
        var profile = new UserProfile { BirthOutcome = BirthOutcome.PartialLossMultiple };

        var statuses = VaccinationSchedule.BuildStatus(
            ageInWeeks: 0, given: [], suppressed: profile.CareContext.SuppressInfantContent);

        Assert.Equal(VaccinationSchedule.Schedule.Count, statuses.Count);
        Assert.Contains(statuses, s => s.Vaccine.Name == "BCG" && s.Status == VaccinationStatus.DueNow);
    }

    // ── (f): no non-postpartum notification is reachable for a bereaved profile ──

    [Theory]
    [InlineData(NotificationScope.General, false)]
    [InlineData(NotificationScope.Infant, false)] // no InfantProfile exists for a full loss
    [InlineData(NotificationScope.MaternalSafety, true)]
    [InlineData(NotificationScope.Elder, true)]
    public void Bereaved_OnlyMaternalSafetyAndElderAlertsSend_WhenNoInfantExists(
        NotificationScope scope, bool expectedShouldSend)
    {
        var shouldSend = NotificationGate.ShouldSend(scope, isBereaved: true, infantProfileExists: false);
        Assert.Equal(expectedShouldSend, shouldSend);
    }

    [Fact]
    public void Bereaved_InfantAlert_SendsWhenTiedToAStillExistingInfantProfile()
    {
        // PartialLossMultiple: the surviving twin's own growth/vaccine
        // alerts must not be swept up by the bereavement whitelist.
        var shouldSend = NotificationGate.ShouldSend(
            NotificationScope.Infant, isBereaved: true, infantProfileExists: true);

        Assert.True(shouldSend);
    }

    [Theory]
    [InlineData(NotificationScope.General)]
    [InlineData(NotificationScope.Infant)]
    [InlineData(NotificationScope.MaternalSafety)]
    [InlineData(NotificationScope.Elder)]
    public void NotBereaved_EveryScopeIsUnaffected(NotificationScope scope)
    {
        Assert.True(NotificationGate.ShouldSend(scope, isBereaved: false, infantProfileExists: false));
    }
}
