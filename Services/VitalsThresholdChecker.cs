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
    public record Concern(string Description, AlertSeverity Severity);

    public static List<Concern> Check(VitalsReading v)
    {
        var concerns = new List<Concern>();

        if (v.SystolicBp is > 180 or < 90)
            concerns.Add(new Concern($"Systolic blood pressure {v.SystolicBp} is outside the safe range (90-180)", AlertSeverity.Critical));
        if (v.DiastolicBp is > 120 or < 60)
            concerns.Add(new Concern($"Diastolic blood pressure {v.DiastolicBp} is outside the safe range (60-120)", AlertSeverity.Critical));
        if (v.HeartRate is > 120 or < 50)
            concerns.Add(new Concern($"Heart rate {v.HeartRate} bpm is outside the safe range (50-120)", AlertSeverity.Warning));
        if (v.TemperatureC is > 38.5m or < 35m)
            concerns.Add(new Concern($"Temperature {v.TemperatureC}°C is outside the safe range (35-38.5)", AlertSeverity.Warning));
        if (v.OxygenSaturation is < 92)
            concerns.Add(new Concern($"Oxygen saturation {v.OxygenSaturation}% is below the safe threshold (92%)", AlertSeverity.Critical));

        return concerns;
    }
}
