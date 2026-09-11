// Services/VitalsThresholdChecker.cs
// The actual "is this dangerous" decision — deliberately plain code, not an
// LLM call. Same reasoning as BirthPlanAgent's static acknowledgment
// templates: cheap, deterministic logic doesn't need a model, and for
// something safety-critical the trigger decision shouldn't depend on LLM
// judgment at all. HealthMonitorAgent is only invoked to explain a concern
// this already found, never to decide whether one exists.
//
// Thresholds below are reasonable general defaults for an adult/elder, not
// medical guidance — a real deployment would want these tunable per elder
// (existing conditions, baseline vitals) rather than fixed constants.

using Janani.Models;

namespace Janani.Services;

public static class VitalsThresholdChecker
{
    public static List<Concern> Check(VitalsReading v)
    {
        var concerns = new List<Concern>();

        // Upper bound (>180/>120): AHA's 2024 scientific statement on
        // elevated BP in acute care defines "markedly elevated BP" (formerly
        // "hypertensive crisis") as SBP/DBP >180/110-120 mmHg. Lower bound
        // (<90/<60): conventional clinical hypotension threshold, not a
        // single cited guideline. TODO-clinical-review: confirm this
        // convention with an elder-care clinician.
        if (v.SystolicBp is > 180 or < 90)
            concerns.Add(new Concern($"Systolic blood pressure {v.SystolicBp} is outside the safe range (90-180)", AlertSeverity.Critical));
        if (v.DiastolicBp is > 120 or < 60)
            concerns.Add(new Concern($"Diastolic blood pressure {v.DiastolicBp} is outside the safe range (60-120)", AlertSeverity.Critical));
        // Normal resting adult HR is 60-100 bpm (AHA); 50-120 is this app's
        // own widened alert band to avoid flagging mild rest-state
        // bradycardia/tachycardia. TODO-clinical-review: the widening margin
        // itself isn't sourced to a guideline.
        if (v.HeartRate is > 120 or < 50)
            concerns.Add(new Concern($"Heart rate {v.HeartRate} bpm is outside the safe range (50-120)", AlertSeverity.Warning));
        // Lower bound (<35°C): conventional clinical hypothermia threshold.
        // Upper bound (>38.5°C): TODO-clinical-review — most sources (CDC,
        // general clinical teaching) cite fever as >=38.0°C, half a degree
        // below this Warning threshold; confirm whether 38.5 was a
        // deliberate low-grade-fever false-positive reduction.
        if (v.TemperatureC is > 38.5m or < 35m)
            concerns.Add(new Concern($"Temperature {v.TemperatureC}°C is outside the safe range (35-38.5)", AlertSeverity.Warning));
        // NHS home pulse-oximetry guidance: SpO2 <=92% -> seek urgent
        // medical attention. (WHO's <90% hypoxemia threshold is a pediatric,
        // low-altitude criterion and isn't the source for this number.)
        if (v.OxygenSaturation is < 92)
            concerns.Add(new Concern($"Oxygen saturation {v.OxygenSaturation}% is below the safe threshold (92%)", AlertSeverity.Critical));

        return concerns;
    }
}
