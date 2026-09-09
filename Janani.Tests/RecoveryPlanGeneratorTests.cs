using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class RecoveryPlanGeneratorTests
{
    // ── PhaseForDay boundaries ──────────────────────────────────────────

    [Theory]
    [InlineData(0, RecoveryPhase.Days0to2)]
    [InlineData(2, RecoveryPhase.Days0to2)]
    [InlineData(3, RecoveryPhase.Days3to7)]
    [InlineData(7, RecoveryPhase.Days3to7)]
    [InlineData(8, RecoveryPhase.Days8to42)]
    [InlineData(42, RecoveryPhase.Days8to42)]
    [InlineData(43, RecoveryPhase.Day43Plus)]
    [InlineData(200, RecoveryPhase.Day43Plus)]
    public void PhaseForDay_MapsEachBoundaryCorrectly(int day, RecoveryPhase expected)
    {
        Assert.Equal(expected, RecoveryPlanGenerator.PhaseForDay(day));
    }

    [Fact]
    public void PhaseForDay_NullDay_ReturnsNull()
    {
        Assert.Null(RecoveryPlanGenerator.PhaseForDay(null));
    }

    // ── Every content item has a non-empty Key and SourceRef ────────────

    [Fact]
    public void EveryGeneralItem_HasKeyAndSourceRef()
    {
        Assert.All(RecoveryPlanGenerator.GeneralItems, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Key), $"Missing Key on item: {item.Text}");
            Assert.False(string.IsNullOrWhiteSpace(item.SourceRef), $"Missing SourceRef on item: {item.Key}");
        });
    }

    [Fact]
    public void EveryDoctorPrompt_HasKeyAndSourceRef()
    {
        Assert.All(RecoveryPlanGenerator.DoctorPrompts, prompt =>
        {
            Assert.False(string.IsNullOrWhiteSpace(prompt.Key), $"Missing Key on prompt: {prompt.Prompt}");
            Assert.False(string.IsNullOrWhiteSpace(prompt.SourceRef), $"Missing SourceRef on prompt: {prompt.Key}");
        });
    }

    [Fact]
    public void EveryRedFlag_HasKeyAndSourceRef()
    {
        Assert.All(RecoveryPlanGenerator.RedFlags, flag =>
        {
            Assert.False(string.IsNullOrWhiteSpace(flag.Key), $"Missing Key on red flag: {flag.Text}");
            Assert.False(string.IsNullOrWhiteSpace(flag.SourceRef), $"Missing SourceRef on red flag: {flag.Key}");
        });
    }

    [Fact]
    public void AllKeys_AreUniqueAcrossGeneralItemsDoctorPromptsAndRedFlags()
    {
        var allKeys = RecoveryPlanGenerator.GeneralItems.Select(i => i.Key)
            .Concat(RecoveryPlanGenerator.DoctorPrompts.Select(p => p.Key))
            .Concat(RecoveryPlanGenerator.RedFlags.Select(f => f.Key))
            .ToList();

        var duplicates = allKeys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    // ── General content: same for every mother, no per-complication or
    // per-delivery-type variance -- enforced structurally (the method
    // takes no delivery-type/complication/CareContext parameter at all),
    // verified here by confirming every phase has content in every one of
    // the seven general categories (Feeding and RedFlags are deliberately
    // excluded from GeneralItems -- see the file header).

    [Theory]
    [InlineData(RecoveryPhase.Days0to2)]
    [InlineData(RecoveryPhase.Days3to7)]
    [InlineData(RecoveryPhase.Days8to42)]
    [InlineData(RecoveryPhase.Day43Plus)]
    public void GeneralContentForPhase_CoversAllSevenPhysicalAndMentalCategories(RecoveryPhase phase)
    {
        var categories = RecoveryPlanGenerator.GeneralContentForPhase(phase).Select(i => i.Category).ToHashSet();

        var expected = new[]
        {
            RecoveryCategory.WoundCare, RecoveryCategory.Pain, RecoveryCategory.MobilityExercise,
            RecoveryCategory.Bleeding, RecoveryCategory.BladderBowel, RecoveryCategory.NutritionSleep,
            RecoveryCategory.MentalHealth
        };
        foreach (var category in expected)
            Assert.Contains(category, categories);

        Assert.DoesNotContain(RecoveryCategory.Feeding, categories);
        Assert.DoesNotContain(RecoveryCategory.RedFlags, categories);
    }

    // ── Red flags: a separate static list, identical for every profile,
    // never filtered by anything. ────────────────────────────────────────

    public static IEnumerable<object[]> VariedProfiles()
    {
        yield return [null!, DeliveryComplication.None, new CareContext(null)];
        yield return [DeliveryType.PlannedCesarean, DeliveryComplication.Infection | DeliveryComplication.Anemia, new CareContext(null)];
        yield return [DeliveryType.VBAC, DeliveryComplication.BloodClot, new CareContext(BirthOutcome.LiveBirth)];
        yield return [DeliveryType.Vaginal, DeliveryComplication.None, new CareContext(BirthOutcome.Stillbirth)];
        yield return [DeliveryType.VaginalWithTearOrEpisiotomy, DeliveryComplication.ThirdOrFourthDegreeTear, new CareContext(BirthOutcome.PartialLossMultiple)];
    }

    [Theory]
    [MemberData(nameof(VariedProfiles))]
    public void RedFlags_AreIdenticalRegardlessOfDeliveryTypeComplicationsOrCareContext(
        DeliveryType? deliveryType, DeliveryComplication complications, CareContext careContext)
    {
        // RedFlags takes no parameters at all -- this test documents and
        // locks in that "never filtered" means never filtered, full stop.
        // The unused parameters exist only to make each case's intent
        // legible (what a differently-shaped profile might otherwise try
        // to filter by), not because RedFlags reads them.
        _ = deliveryType;
        _ = complications;
        _ = careContext;

        var flags = RecoveryPlanGenerator.RedFlags;

        Assert.Equal(RecoveryPlanGenerator.RedFlags, flags);
        Assert.Equal(8, flags.Count);
    }

    [Fact]
    public void RedFlags_SameReferenceEveryCall()
    {
        // Same static list every time -- there is no code path that could
        // ever produce a shorter list for anyone.
        Assert.Same(RecoveryPlanGenerator.RedFlags, RecoveryPlanGenerator.RedFlags);
    }

    // ── DoctorPrompts: the one place DeliveryType/DeliveryComplication/
    // CareContext affect output -- and only as topic nudges, never
    // instructions (enforced by review, not testable directly, but the
    // filtering logic itself is tested here). ──────────────────────────

    [Fact]
    public void DoctorPrompts_NoTagsMatched_ExcludesEveryDeliveryTypeAndComplicationSpecificPrompt()
    {
        // CareContext(null) = no outcome recorded, the default for every
        // account that hasn't gone through delivery capture -- behaves as
        // HasAnySurvivingInfant: true (LiveBirth-equivalent), so the
        // feeding-routine prompt still applies; only bereavement-specific
        // prompts are excluded here.
        var prompts = RecoveryPlanGenerator.DoctorPromptsFor(null, DeliveryComplication.None, new CareContext(null));

        Assert.DoesNotContain(prompts, p => p.Key == "ask-cesarean-incision");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-perineal-tear");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-pph-followup");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-milk-after-loss");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-grief-support");
        Assert.Contains(prompts, p => p.Key == "ask-feeding-routine"); // no recorded outcome behaves as LiveBirth
    }

    [Fact]
    public void DoctorPrompts_Cesarean_IncludesCesareanSpecificPrompts()
    {
        var prompts = RecoveryPlanGenerator.DoctorPromptsFor(
            DeliveryType.PlannedCesarean, DeliveryComplication.None, new CareContext(null));

        Assert.Contains(prompts, p => p.Key == "ask-cesarean-incision");
        Assert.Contains(prompts, p => p.Key == "ask-cesarean-activity");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-perineal-tear");
    }

    [Fact]
    public void DoctorPrompts_ComplicationFlags_IncludeMatchingPromptsOnly()
    {
        var prompts = RecoveryPlanGenerator.DoctorPromptsFor(
            DeliveryType.Vaginal, DeliveryComplication.PostpartumHemorrhage | DeliveryComplication.Anemia, new CareContext(null));

        Assert.Contains(prompts, p => p.Key == "ask-pph-followup");
        Assert.Contains(prompts, p => p.Key == "ask-anemia-followup");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-preeclampsia-followup");
    }

    [Fact]
    public void DoctorPrompts_LiveBirth_GetsFeedingRoutinePromptNotMilkAfterLoss()
    {
        var prompts = RecoveryPlanGenerator.DoctorPromptsFor(
            DeliveryType.Vaginal, DeliveryComplication.None, new CareContext(BirthOutcome.LiveBirth));

        Assert.Contains(prompts, p => p.Key == "ask-feeding-routine");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-milk-after-loss");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-grief-support");
    }

    [Fact]
    public void DoctorPrompts_FullLoss_GetsMilkAfterLossAndGriefSupportNotFeedingRoutine()
    {
        var prompts = RecoveryPlanGenerator.DoctorPromptsFor(
            DeliveryType.EmergencyCesarean, DeliveryComplication.None, new CareContext(BirthOutcome.Stillbirth));

        Assert.Contains(prompts, p => p.Key == "ask-milk-after-loss");
        Assert.Contains(prompts, p => p.Key == "ask-grief-support");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-feeding-routine");
    }

    [Fact]
    public void DoctorPrompts_PartialLossMultiple_GetsFeedingRoutineAndGriefSupportBothNotMilkAfterLoss()
    {
        // The surviving twin still needs feeding support; the mother is
        // still bereaved and needs grief support too -- both apply at once.
        var prompts = RecoveryPlanGenerator.DoctorPromptsFor(
            DeliveryType.Vaginal, DeliveryComplication.None, new CareContext(BirthOutcome.PartialLossMultiple));

        Assert.Contains(prompts, p => p.Key == "ask-feeding-routine");
        Assert.Contains(prompts, p => p.Key == "ask-grief-support");
        Assert.DoesNotContain(prompts, p => p.Key == "ask-milk-after-loss");
    }
}
