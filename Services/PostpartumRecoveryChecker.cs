// Services/PostpartumRecoveryChecker.cs
// Postpartum-side counterpart to PregnancyVitalsThresholdChecker — same
// rule: deliberately plain code, not an LLM call. PostpartumRecoveryMonitorService
// is only invoked to explain a concern this already found, never to decide
// whether one exists.
//
// Thresholds below are reasonable general defaults (CDC urgent maternal
// warning signs; the same 38/39°C fever tiers already used for pregnancy
// vitals), not medical guidance — a real deployment would want these
// reviewed by a clinician and tunable per patient rather than fixed
// constants, same caveat as PregnancyVitalsThresholdChecker's own header.

using Janani.Models;

namespace Janani.Services;

public static class PostpartumRecoveryChecker
{
    public static List<Concern> Check(PostpartumCheckIn checkIn)
    {
        var concerns = new List<Concern>();

        if (checkIn.PainLevel >= 10)
            concerns.Add(new Concern($"Pain level {checkIn.PainLevel}/10 is the maximum on the scale — this needs prompt medical attention", AlertSeverity.Critical));
        else if (checkIn.PainLevel >= 8)
            concerns.Add(new Concern($"Pain level {checkIn.PainLevel}/10 is severe — worth telling your doctor", AlertSeverity.Warning));

        if (checkIn.Bleeding == BleedingLevel.SoakingPadHourly)
            concerns.Add(new Concern("Soaking through a pad within an hour is a warning sign — this needs prompt medical attention", AlertSeverity.Critical));
        else if (checkIn.Bleeding == BleedingLevel.Heavy)
            concerns.Add(new Concern("Bleeding heavier than expected — worth telling your doctor", AlertSeverity.Warning));

        if (checkIn.TemperatureC is >= 39.0m)
            concerns.Add(new Concern($"Temperature {checkIn.TemperatureC}°C is a high fever — needs prompt medical attention", AlertSeverity.Critical));
        else if (checkIn.TemperatureC is >= 38.0m)
            concerns.Add(new Concern($"Temperature {checkIn.TemperatureC}°C is a fever — worth telling your doctor", AlertSeverity.Warning));

        if (checkIn.Wound == WoundStatus.SignificantConcern)
            concerns.Add(new Concern("Your wound shows signs (spreading redness, pus, foul smell, or the wound opening) that need prompt medical attention", AlertSeverity.Critical));
        else if (checkIn.Wound == WoundStatus.MildRednessOrSwelling)
            concerns.Add(new Concern("Some redness or swelling at your wound — worth telling your doctor", AlertSeverity.Warning));

        return concerns;
    }
}

// Selects which wording to show for the daily wound question — a UI
// concern, not a severity concern: PostpartumRecoveryChecker evaluates the
// resulting WoundStatus value identically regardless of delivery type,
// this only decides whether to ask about an incision or a perineum.
public static class PostpartumCheckInQuestions
{
    public static string WoundQuestionFor(DeliveryType? deliveryType) => deliveryType switch
    {
        DeliveryType.PlannedCesarean or DeliveryType.EmergencyCesarean =>
            "How does your incision look and feel today?",
        DeliveryType.Vaginal or DeliveryType.VaginalWithTearOrEpisiotomy
            or DeliveryType.AssistedVaginal or DeliveryType.VBAC =>
            "How does your perineum look and feel today?",
        _ => "How does your wound (incision or perineum) look and feel today?"
    };
}
