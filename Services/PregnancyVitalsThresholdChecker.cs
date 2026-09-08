// Services/PregnancyVitalsThresholdChecker.cs
// Pregnancy-side counterpart to VitalsThresholdChecker (elder) — same rule:
// deliberately plain code, not an LLM call. PregnancyVitalsMonitorService is
// only invoked to explain a concern this already found, never to decide
// whether one exists.
//
// Blood pressure gets two tiers instead of elder's one flat range, because
// hypertensive disorders of pregnancy are clinically staged that way
// (ACOG): gestational hypertension/pre-eclampsia range vs. severe range.
// Thresholds below are reasonable general defaults, not medical guidance —
// a real deployment would want these reviewed by an OB-GYN and tunable per
// patient (existing conditions, baseline BP) rather than fixed constants.

using Janani.Models;

namespace Janani.Services;

public static class PregnancyVitalsThresholdChecker
{
    public static List<Concern> Check(PregnancyVitalsReading v)
    {
        var concerns = new List<Concern>();

        if (v.SystolicBp is >= 160 || v.DiastolicBp is >= 110)
            concerns.Add(new Concern($"Blood pressure {v.SystolicBp}/{v.DiastolicBp} is in the severe range — this needs prompt medical attention", AlertSeverity.Critical));
        else if (v.SystolicBp is >= 140 || v.DiastolicBp is >= 90)
            concerns.Add(new Concern($"Blood pressure {v.SystolicBp}/{v.DiastolicBp} is in the pre-eclampsia range — worth telling your doctor", AlertSeverity.Warning));

        if (v.HeartRate is > 120 or < 50)
            concerns.Add(new Concern($"Heart rate {v.HeartRate} bpm is outside the safe range (50-120)", AlertSeverity.Warning));
        if (v.TemperatureC is >= 39.0m)
            concerns.Add(new Concern($"Temperature {v.TemperatureC}°C is a high fever — needs prompt medical attention", AlertSeverity.Critical));
        else if (v.TemperatureC is >= 38.0m)
            concerns.Add(new Concern($"Temperature {v.TemperatureC}°C is a fever — worth telling your doctor", AlertSeverity.Warning));
        if (v.OxygenSaturation is < 92)
            concerns.Add(new Concern($"Oxygen saturation {v.OxygenSaturation}% is below the safe threshold (92%)", AlertSeverity.Critical));

        return concerns;
    }
}
