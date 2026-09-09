// Services/EpdsContent.cs
// The Edinburgh Postnatal Depression Scale item text/response options —
// static display content, same "computed on read" spirit as
// RecoveryPlanGenerator, kept separate from EpdsScreeningChecker (which
// only ever sees the already-scored 0-3 integers, never this text).
//
// ============================================================================
// LICENSING — READ BEFORE ENABLING THIS SCREEN FOR REAL USERS.
// ============================================================================
// Source: Cox JL, Holden JM, Sagovsky R (1987) "Detection of postnatal
// depression: development of the 10-item Edinburgh Postnatal Depression
// Scale", British Journal of Psychiatry 150:782-786.
//
// The English text below was verified against a COPE (Centre of Perinatal
// Excellence, Australia) reproduction of the instrument that itself
// carries the citation above and the note "Reproduced with permission."
//
// The Royal College of Psychiatrists' own stated terms for the EPDS
// (confirmed via web search, not independently re-verified with RCPsych
// directly): the scale "may be photocopied by individual researchers or
// clinicians for their own use without seeking permission from the
// publishers, provided the scale is copied in full and all copies
// acknowledge the source." Written permission from the Royal College of
// Psychiatrists is required "for copying and distribution to others or
// for republication (in print, online or by any other medium)."
//
// A production app serving this content to end users is republication by
// an online medium under that definition. THIS CODEBASE DOES NOT CURRENTLY
// HAVE THAT WRITTEN PERMISSION. Do not enable this screening flow for real
// users until it has been obtained and this comment updated to record it
// (who granted it, when, any conditions attached).
// ============================================================================

using Janani.Models;

namespace Janani.Services;

// Number = the published item number (1-10), used for AdministeredAt-time
// scoring (EpdsScreening.ItemN) and for citing "item 10" in the checker.
// DescendingScore = true means the FIRST displayed option is the most
// severe (3 points) and the LAST is least severe (0 points) — items 3, 5,
// 6, 7, 8, 9, 10 in the published scale are presented this way. Items 1,
// 2, 4 are the opposite: first option = 0 points, last = 3 points. Getting
// this backwards for even one item would silently corrupt real depression
// screening scores, so it's captured explicitly per item rather than left
// to be inferred from option text at render time — see
// EpdsContent.ScoreForSelection and EpdsContentTests for the mapping this
// protects.
public record EpdsItem(int Number, string Statement, string[] Options, bool DescendingScore);

public static class EpdsContent
{
    // Verified verbatim against the COPE.org.au reproduction described
    // above. Options are listed in the same order as the published form.
    public static readonly List<EpdsItem> EnglishItems =
    [
        new(1, "I have been able to laugh and see the funny side of things",
            ["As much as I always could", "Not quite so much now", "Definitely not so much now", "Not at all"],
            DescendingScore: false),
        new(2, "I have looked forward with enjoyment to things",
            ["As much as I ever did", "Rather less than I used to", "Definitely less than I used to", "Hardly at all"],
            DescendingScore: false),
        new(3, "I have blamed myself unnecessarily when things went wrong",
            ["Yes, most of the time", "Yes, some of the time", "Not very often", "No, never"],
            DescendingScore: true),
        new(4, "I have been anxious or worried for no good reason",
            ["No, not at all", "Hardly ever", "Yes, sometimes", "Yes, very often"],
            DescendingScore: false),
        new(5, "I have felt scared or panicky for no very good reason",
            ["Yes, quite a lot", "Yes, sometimes", "No, not much", "No, not at all"],
            DescendingScore: true),
        new(6, "Things have been getting on top of me",
            ["Yes, most of the time I haven't been able to cope at all", "Yes, sometimes I haven't been coping as well as usual",
             "No, most of the time I have coped quite well", "No, I have been coping as well as ever"],
            DescendingScore: true),
        new(7, "I have been so unhappy that I have had difficulty sleeping",
            ["Yes, most of the time", "Yes, sometimes", "Not very often", "No, not at all"],
            DescendingScore: true),
        new(8, "I have felt sad or miserable",
            ["Yes, most of the time", "Yes, quite often", "Not very often", "No, not at all"],
            DescendingScore: true),
        new(9, "I have been so unhappy that I have been crying",
            ["Yes, most of the time", "Yes, quite often", "Only occasionally", "No, never"],
            DescendingScore: true),
        new(10, "The thought of harming myself has occurred to me",
            ["Yes, quite often", "Sometimes", "Hardly ever", "Never"],
            DescendingScore: true),
    ];

    public static int ScoreForSelection(EpdsItem item, int selectedOptionIndex) =>
        item.DescendingScore ? 3 - selectedOptionIndex : selectedOptionIndex;

    // Deliberately NOT a lookup that falls back to English (or to any
    // machine/in-house translation) for an unlisted language — per
    // instruction, an unavailable language must be reported as
    // unavailable, not silently substituted.
    //
    // Hindi: a validated translation exists (Joshi et al. 2020, Int J
    // Womens Health — Hindi-version validation for antenatal depression,
    // cutoff 9/10 against a Hindi PHQ-9 gold standard) but its exact
    // item-by-item text was not retrieved/verified in this session — it
    // sits behind the journal's full text, not reproducible from search
    // results alone. Add it here once the actual validated wording has
    // been sourced from that publication (or RCPsych's own translations
    // list) and its own licensing confirmed.
    //
    // Telugu: no published, validated EPDS Telugu translation could be
    // confirmed to exist in this session's research (Hindi and Marathi
    // validations turned up; Telugu did not). Do not assume one exists —
    // verify independently before adding it here.
    public static readonly HashSet<AppLanguage> AvailableLanguages = [AppLanguage.English];

    public static bool IsLanguageAvailable(AppLanguage language) => AvailableLanguages.Contains(language);
}
