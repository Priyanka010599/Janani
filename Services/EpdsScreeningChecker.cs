// Services/EpdsScreeningChecker.cs
// Pure, deterministic scoring of the Edinburgh Postnatal Depression Scale —
// same discipline as PostpartumRecoveryChecker/PregnancyVitalsThresholdChecker:
// no I/O, no LLM call. This class takes no BirthOutcome/CareContext input
// at all — that's what makes "no different threshold for a bereaved
// profile" true structurally, not just by policy: there's nothing here to
// branch on even if someone wanted to.
//
// Total-score cutoffs — SOURCED, not invented:
//   >= 13: the original UK threshold from Cox JL, Holden JM, Sagovsky R
//          (1987) "Detection of postnatal depression: development of the
//          10-item Edinburgh Postnatal Depression Scale", Br J Psychiatry
//          150:782-786 — sensitivity 86%, specificity 78% in that sample.
//   >= 10: the conventional "possible depression, worth a closer look"
//          tier used alongside the 13 cutoff in general clinical practice.
// Indian validation studies have used LOWER thresholds than the UK ≥13:
//   - Postpartum populations in India: EPDS >= 12 used as the screening
//     threshold in multiple studies (e.g. Mumbai tertiary-care cohort).
//   - Joshi et al. (2020), Hindi-version validation for ANTENATAL
//     depression (Int J Womens Health): optimal cutoff 9/10 against a
//     Hindi PHQ-9 gold standard (sensitivity 65.4%, specificity 79.7%).
// This checker keeps the UK 10/13 tiers as the default because that's
// what's actually validated end-to-end (both cutoff AND item wording) for
// the English instrument used here — see EpdsContent.cs for the item-text
// licensing/translation-availability situation. Swapping in a
// lower Indian-validated cutoff without also using that study's own
// validated (and correctly licensed) translation would be internally
// inconsistent — flag this constant to a clinician before changing it
// rather than mixing cutoffs across studies.

using Janani.Models;

namespace Janani.Services;

public static class EpdsScreeningChecker
{
    public static List<Concern> Check(EpdsScreening screening)
    {
        var concerns = new List<Concern>();

        // Item 10 (thoughts of self-harm) escalates on ANY nonzero answer,
        // independent of the total score. This is a DELIBERATE, more
        // conservative departure from the published scale's own scoring —
        // the standard EPDS treats item 10 as just one more 0-3 item
        // folded into the total, with no special standalone rule. Clinical
        // practice guidance (and this app's own judgment) treats ANY
        // nonzero answer on the self-harm item as needing immediate
        // follow-up regardless of the total score, because a low total
        // score can mask a real, isolated self-harm signal. Do not
        // "correct" this back to matching the published scale — the
        // deviation is intentional, checked first and unconditionally.
        if (screening.Item10 > 0)
            concerns.Add(new Concern(
                "A response on the self-harm question needs immediate attention, regardless of the total score",
                AlertSeverity.Critical));

        var total = screening.TotalScore;
        if (total >= 13)
            concerns.Add(new Concern($"EPDS score {total}/30 suggests a higher likelihood of depression — needs prompt medical attention", AlertSeverity.Critical));
        else if (total >= 10)
            concerns.Add(new Concern($"EPDS score {total}/30 suggests possible depression — worth telling your doctor", AlertSeverity.Warning));

        return concerns;
    }
}
