// Services/GrowthThresholdChecker.cs
// Same split as VitalsThresholdChecker: plain deterministic code decides
// whether a growth entry is concerning, an LLM is never asked to judge it.
//
// This deliberately does NOT implement WHO growth-standard percentile
// curves (those are sex-specific, day-of-age LMS tables — real clinical
// data Janani doesn't have and shouldn't approximate). InfantProfile also
// has no sex field yet. What follows is wide, unisex sanity bands plus
// trend checks against the infant's own history — good enough to catch a
// likely data-entry error or a genuinely stalled/dropping trend and nudge
// "mention this to your pediatrician," not to diagnose anything.

using Janani.Models;

namespace Janani.Services;

public static class GrowthThresholdChecker
{
    public static List<Concern> Check(InfantProfile infant, GrowthEntry latest, GrowthEntry? previous)
    {
        var concerns = new List<Concern>();
        var ageWeeks = infant.AgeInWeeks;

        if (latest.WeightKg is { } weight)
        {
            var (min, max) = ageWeeks switch
            {
                <= 4 => (2.0m, 5.5m),
                <= 26 => (3.0m, 10.5m),
                <= 52 => (5.0m, 13.5m),
                _ => (6.5m, 16.5m)
            };
            if (weight < min || weight > max)
                concerns.Add(new Concern(
                    $"Weight {weight}kg is outside the typical range for {infant.Name}'s age — worth mentioning to your pediatrician",
                    AlertSeverity.Warning));

            if (previous?.WeightKg is { } prevWeight)
            {
                var daysSince = (latest.RecordedAt - previous.RecordedAt).TotalDays;
                if (ageWeeks > 4 && weight < prevWeight)
                    concerns.Add(new Concern(
                        $"Weight dropped from {prevWeight}kg to {weight}kg since the last check-in",
                        AlertSeverity.Warning));
                else if (daysSince >= 30 && weight <= prevWeight)
                    concerns.Add(new Concern(
                        "No weight gain over the last month or more",
                        AlertSeverity.Warning));
            }
        }

        return concerns;
    }
}
