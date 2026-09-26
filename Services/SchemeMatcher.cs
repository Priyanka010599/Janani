// Services/SchemeMatcher.cs
// Deterministic "which government schemes probably apply" check -- same discipline as
// every other checker in this app: plain code decides, no model is asked whether someone
// is eligible for anything. A caregiver is told a scheme is Likely / Possible / Unlikely
// from a handful of facts she gives us, with the reasons spelled out as keys the page
// translates, so the verdict itself never depends on wording or on an LLM.
//
// SOURCED, not invented -- only rules confirmed against the issuing departments' own pages
// (checked 2026-09):
//   PMMVY 2.0 (Mission Shakti, from 1 Apr 2022) -- Ministry of Women & Child Development FAQs
//     (spniwcd.wcd.gov.in/pradhan-mantri-matru-vandana-yojna/faqs):
//       * Rs 5,000 for the first living child, in two instalments (Rs 3,000 after ANC,
//         Rs 2,000 after birth registration and 14 weeks of immunisation).
//       * Rs 6,000 for a second child ONLY if that child is a girl, in one instalment.
//       * Pregnant women and lactating mothers, age band 18y 7m - 55y.
//       * Central/state government and PSU employees are excluded.
//       * Only the first two live births are covered.
//   JSY -- National Health Mission (nhm.gov.in, Janani Suraksha Yojana):
//       * Low Performing States get the wider rule: every woman delivering in a public or
//         accredited facility, regardless of age or parity. Listed: UP, Uttarakhand, Bihar,
//         Jharkhand, MP, Chhattisgarh, Assam, Rajasthan, Odisha, J&K.
//       * Elsewhere: BPL / SC / ST women aged 19+, for the first two live births.
//       * Low Performing States cash: Rs 1,400 rural, Rs 1,000 urban (mother's share).
//   JSSK / UIP -- free at public facilities, see caregiver_agent/scheme_navigator.py.
//
// DELIBERATELY NOT ENCODED: the cash amount for High Performing States. It wasn't
// confirmable from an official page during this check, so the result says "confirm the
// amount locally" instead of guessing a number. Rules also change and vary by state --
// SchemeMatcher.AsOf is shown to the user for exactly that reason, and every result
// carries the "confirm with your ASHA / Anganwadi worker" reminder.

namespace Janani.Services;

public enum Scheme { Pmmvy, Jsy, Jssk, Uip }

public enum MatchStatus { Likely, Possible, Unlikely }

// Everything here is a fact the caregiver states, or is prefilled from her own profile.
// Nullable means "she hasn't said" -- an unknown never silently counts as a yes or a no.
public record SchemeInput(
    string? State,
    bool? IsRural,
    int? Age,
    bool Expecting,
    bool Lactating,
    bool HasInfantUnderOne,
    int PriorLiveBirths,
    bool? DisadvantagedCategory,   // BPL / SC / ST household
    bool? GovtOrPsuEmployee);

// Reasons are translation keys, not sentences -- the page owns the wording.
public record SchemeMatch(Scheme Scheme, MatchStatus Status, IReadOnlyList<string> Reasons, int? AmountRupees);

public static class SchemeMatcher
{
    public const string AsOf = "September 2026";

    public static readonly IReadOnlySet<string> JsyLowPerformingStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Uttar Pradesh", "Uttarakhand", "Bihar", "Jharkhand", "Madhya Pradesh",
        "Chhattisgarh", "Assam", "Rajasthan", "Odisha", "Jammu and Kashmir"
    };

    public static List<SchemeMatch> Match(SchemeInput i) =>
    [
        MatchPmmvy(i),
        MatchJsy(i),
        MatchJssk(i),
        MatchUip(i)
    ];

    private static SchemeMatch MatchPmmvy(SchemeInput i)
    {
        var reasons = new List<string>();
        if (!i.Expecting && !i.Lactating)
            return new SchemeMatch(Scheme.Pmmvy, MatchStatus.Unlikely, ["NotPregnantOrLactating"], null);

        var unlikely = false;
        var possible = false;
        int? amount = null;

        switch (i.Age)
        {
            case null: possible = true; reasons.Add("ConfirmAge"); break;
            case < 18: unlikely = true; reasons.Add("PmmvyAgeBelowMin"); break;
            case > 55: unlikely = true; reasons.Add("PmmvyAgeAboveMax"); break;
            case 18: possible = true; reasons.Add("PmmvyAgeBorderline"); break;
        }

        switch (i.GovtOrPsuEmployee)
        {
            case true: unlikely = true; reasons.Add("PmmvyGovtEmployee"); break;
            case null: possible = true; reasons.Add("ConfirmEmployment"); break;
        }

        switch (i.PriorLiveBirths)
        {
            case 0: amount = 5000; reasons.Add("PmmvyFirstChild"); break;
            case 1: amount = 6000; possible = true; reasons.Add("PmmvySecondChildGirlOnly"); break;
            default: unlikely = true; reasons.Add("PmmvyThirdOrLater"); break;
        }

        // Sources describe PMMVY's target group differently (disadvantaged sections vs. all
        // eligible women), so a "no" or "not sure" is a prompt to confirm, never a rejection.
        if (i.DisadvantagedCategory != true)
        {
            possible = true;
            reasons.Add("ConfirmCategory");
        }

        // Once excluded, a line about what the benefit would have been is just noise.
        if (unlikely) reasons.RemoveAll(r => r is "PmmvyFirstChild" or "PmmvySecondChildGirlOnly");

        return new SchemeMatch(Scheme.Pmmvy, Resolve(unlikely, possible), reasons, unlikely ? null : amount);
    }

    private static SchemeMatch MatchJsy(SchemeInput i)
    {
        if (!i.Expecting && !i.Lactating)
            return new SchemeMatch(Scheme.Jsy, MatchStatus.Unlikely, ["NotPregnantOrLactating"], null);

        if (string.IsNullOrWhiteSpace(i.State))
            return new SchemeMatch(Scheme.Jsy, MatchStatus.Possible, ["ConfirmState"], null);

        var reasons = new List<string>();

        if (JsyLowPerformingStates.Contains(i.State))
        {
            reasons.Add("JsyLowPerformingState");
            int? amount = i.IsRural switch { true => 1400, false => 1000, _ => null };
            var possible = false;
            if (amount == null) { possible = true; reasons.Add("ConfirmRuralUrban"); }
            return new SchemeMatch(Scheme.Jsy, possible ? MatchStatus.Possible : MatchStatus.Likely, reasons, amount);
        }

        // Every other state: BPL/SC/ST, 19+, first two live births.
        var unlikely = false;
        var maybe = false;
        reasons.Add("JsyOtherState");

        if (i.PriorLiveBirths >= 2) { unlikely = true; reasons.Add("JsyParityLimit"); }

        switch (i.Age)
        {
            case null: maybe = true; reasons.Add("ConfirmAge"); break;
            case < 19: unlikely = true; reasons.Add("JsyAgeBelowMin"); break;
        }

        switch (i.DisadvantagedCategory)
        {
            case false: unlikely = true; reasons.Add("JsyCategoryRequired"); break;
            case null: maybe = true; reasons.Add("ConfirmCategory"); break;
        }

        reasons.Add("JsyConfirmAmount");
        return new SchemeMatch(Scheme.Jsy, Resolve(unlikely, maybe), reasons, null);
    }

    // No eligibility test beyond "delivering in / born at a public facility" -- a plain yes for
    // anyone pregnant, recently delivered, or with a newborn, and nothing to be unsure about.
    private static SchemeMatch MatchJssk(SchemeInput i) =>
        i.Expecting || i.Lactating || i.HasInfantUnderOne
            ? new SchemeMatch(Scheme.Jssk, MatchStatus.Likely, ["JsskPublicFacility"], null)
            : new SchemeMatch(Scheme.Jssk, MatchStatus.Unlikely, ["NoPregnancyOrInfant"], null);

    private static SchemeMatch MatchUip(SchemeInput i) =>
        i.HasInfantUnderOne
            ? new SchemeMatch(Scheme.Uip, MatchStatus.Likely, ["UipFree"], null)
            : new SchemeMatch(Scheme.Uip, MatchStatus.Unlikely, ["NoInfant"], null);

    private static MatchStatus Resolve(bool unlikely, bool possible) =>
        unlikely ? MatchStatus.Unlikely : possible ? MatchStatus.Possible : MatchStatus.Likely;
}
