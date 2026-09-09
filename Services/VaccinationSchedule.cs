// Services/VaccinationSchedule.cs
// Fixed reference data — India's Universal Immunization Programme schedule
// (same government-scheme context as agents/caregiver_agent/scheme_navigator.py's
// JSY/JSSK content). Due ages are facts, not something an LLM should be
// asked to recall or compute — same deterministic-vs-LLM split as
// VitalsThresholdChecker: this class decides what's due/overdue, an agent
// never does.

using Janani.Models;

namespace Janani.Services;

public enum VaccinationStatus
{
    Given, Overdue, DueNow, Upcoming
}

public class VaccinationSchedule
{
    public record ScheduledVaccine(string Name, int DueAtWeeks, string AgeLabel);

    // Grace window before something manually-tracked (no reminders yet) counts
    // as "overdue" rather than merely "due now" — avoids flagging a vaccine
    // as overdue the day after it becomes due.
    private const int GraceWeeks = 2;

    public static readonly List<ScheduledVaccine> Schedule =
    [
        new("BCG", 0, "at birth"),
        new("OPV — birth dose", 0, "at birth"),
        new("Hepatitis B — birth dose", 0, "at birth"),
        new("OPV-1", 6, "6 weeks"),
        new("Pentavalent-1", 6, "6 weeks"),
        new("Rotavirus-1", 6, "6 weeks"),
        new("PCV-1", 6, "6 weeks"),
        new("fIPV-1", 6, "6 weeks"),
        new("OPV-2", 10, "10 weeks"),
        new("Pentavalent-2", 10, "10 weeks"),
        new("Rotavirus-2", 10, "10 weeks"),
        new("OPV-3", 14, "14 weeks"),
        new("Pentavalent-3", 14, "14 weeks"),
        new("Rotavirus-3", 14, "14 weeks"),
        new("PCV-2", 14, "14 weeks"),
        new("fIPV-2", 14, "14 weeks"),
        new("Measles-Rubella (MR-1)", 39, "9 months"),
        new("PCV booster", 39, "9-12 months"),
        new("Vitamin A — 1st dose", 39, "9 months"),
        new("DPT booster-1", 68, "16-24 months"),
        new("OPV booster", 68, "16-24 months"),
        new("MR-2", 68, "16-24 months"),
    ];

    public record VaccineWithStatus(ScheduledVaccine Vaccine, VaccinationStatus Status, DateTime? GivenAt);

    // suppressed is a second, independent line of defense for a bereaved
    // profile's infant content (see UserProfile.CareContext) — the primary
    // one is structural: no InfantProfile row is ever created for a baby
    // who didn't survive, so every caller here naturally has nothing to
    // iterate over already. This parameter means that guarantee doesn't
    // depend solely on that data-model assumption never changing.
    public static List<VaccineWithStatus> BuildStatus(int ageInWeeks, IReadOnlyCollection<VaccinationRecord> given, bool suppressed = false)
    {
        if (suppressed) return [];

        var givenByName = given.ToDictionary(g => g.VaccineName, g => g.GivenAt);

        return Schedule.Select(v =>
        {
            if (givenByName.TryGetValue(v.Name, out var givenAt))
                return new VaccineWithStatus(v, VaccinationStatus.Given, givenAt);

            var status = ageInWeeks >= v.DueAtWeeks + GraceWeeks ? VaccinationStatus.Overdue
                : ageInWeeks >= v.DueAtWeeks ? VaccinationStatus.DueNow
                : VaccinationStatus.Upcoming;
            return new VaccineWithStatus(v, status, null);
        }).ToList();
    }
}
