// Services/RecoveryPlanGenerator.cs
// Postpartum recovery-plan content, computed on read -- same shape as
// VaccinationSchedule: the content lives as static data in this file, the
// generator only ever selects/filters it, and only per-mother COMPLETION
// state is persisted (Models.RecoveryPlanItemCompletion).
//
// This app does not give tailored clinical instructions -- that's the
// doctor's role. GeneralItems below is the SAME content for every mother,
// organized into four phases driven off PostpartumDay; nothing here
// varies by delivery type, complications, or bereavement status except:
//   (a) DoctorPrompts -- short "ask your doctor about X" nudges (never
//       how-to instructions) filtered by DeliveryType/DeliveryComplication/
//       CareContext. This is the ONLY place those three axes affect output.
//   (b) RedFlags -- deliberately the OPPOSITE: one static list, identical
//       for every profile, never filtered by anything. See
//       RecoveryPlanGeneratorTests for the test asserting that.
//
// Every item (general, prompt, or red flag) carries a stable string Key
// (for referencing/completion-tracking -- content wording can change
// without breaking a reference) and a SourceRef. No item names a
// medication or a dose. First-pass content from well-known public
// guidelines (WHO, ACOG, NHS, RCOG, CDC), not authored or reviewed by a
// clinician for this app specifically -- worth a clinician's review before
// this is relied on in production, same caveat as the bereavement catalog.
//
// PretermBirth/NicuAdmission prompts direct questions to the "neonatal
// team" rather than "your doctor" -- for a baby in NICU, that's who
// actually answers these, not the mother's own obstetric provider.

using Janani.Models;

namespace Janani.Services;

// Day 0-7 counts as week 1; Days8to42 therefore starts day 8, keeping all
// four phases contiguous with no gap and no overlap.
public enum RecoveryPhase
{
    Days0to2,
    Days3to7,
    Days8to42,
    Day43Plus
}

public enum RecoveryCategory
{
    WoundCare,
    Pain,
    MobilityExercise,
    Bleeding,
    BladderBowel,
    Feeding,
    NutritionSleep,
    MentalHealth,
    RedFlags
}

// Which delivery type(s) a DoctorPrompt applies to. [Flags] because some
// prompts span a group (any Cesarean) while others target exactly one
// (VBAC). None = every delivery type.
[Flags]
public enum DeliveryTypeTag
{
    None = 0,
    Vaginal = 1,
    VaginalWithTearOrEpisiotomy = 2,
    AssistedVaginal = 4,
    PlannedCesarean = 8,
    EmergencyCesarean = 16,
    VBAC = 32,
    AnyVaginal = Vaginal | VaginalWithTearOrEpisiotomy | AssistedVaginal | VBAC,
    AnyCesarean = PlannedCesarean | EmergencyCesarean
}

// How a DoctorPrompt's relevance depends on CareContext. Always is the
// default; the other three exist because feeding-related prompts and one
// grief-support prompt genuinely do depend on whether a baby survived --
// see the constraint comment on CareContext itself, which this still
// respects (nothing here ever touches the other eight categories).
public enum BereavementApplicability
{
    Always,
    RequiresSurvivingInfant,
    FullLossOnly,
    BereavedOnly
}

// General postpartum information -- identical for every mother in a given
// phase. No delivery-type, complication, or CareContext field: this is
// deliberate, per the "no tailored clinical instructions" rule.
public record RecoveryPlanContentItem(string Key, RecoveryPhase Phase, RecoveryCategory Category, string Text, string SourceRef);

// A nudge to raise a topic with her provider -- never an instruction.
public record AskYourDoctorPrompt(
    string Key,
    RecoveryCategory Category,
    string Prompt,
    string SourceRef,
    DeliveryTypeTag DeliveryTypeTags = DeliveryTypeTag.None,
    DeliveryComplication RequiredComplications = DeliveryComplication.None,
    BereavementApplicability BereavementApplicability = BereavementApplicability.Always);

public record RedFlagItem(string Key, string Text, string SourceRef);

public static class RecoveryPlanGenerator
{
    public static RecoveryPhase? PhaseForDay(int? postpartumDay) => postpartumDay switch
    {
        null => null,
        <= 2 => RecoveryPhase.Days0to2,
        <= 7 => RecoveryPhase.Days3to7,
        <= 42 => RecoveryPhase.Days8to42,
        _ => RecoveryPhase.Day43Plus
    };

    public static IReadOnlyList<RecoveryPlanContentItem> GeneralContentForPhase(RecoveryPhase phase) =>
        GeneralItems.Where(i => i.Phase == phase).ToList();

    // The only function in this class that takes delivery type,
    // complications, or CareContext as input -- everything else here is
    // either phase-only (GeneralContentForPhase) or entirely unfiltered
    // (RedFlags).
    public static IReadOnlyList<AskYourDoctorPrompt> DoctorPromptsFor(
        DeliveryType? deliveryType, DeliveryComplication complications, CareContext careContext)
    {
        var deliveryTag = ToTag(deliveryType);
        return DoctorPrompts.Where(p =>
                (p.DeliveryTypeTags == DeliveryTypeTag.None || (p.DeliveryTypeTags & deliveryTag) != 0)
                && (p.RequiredComplications == DeliveryComplication.None || (p.RequiredComplications & complications) != 0)
                && MatchesBereavement(p.BereavementApplicability, careContext))
            .ToList();
    }

    private static DeliveryTypeTag ToTag(DeliveryType? type) => type switch
    {
        DeliveryType.Vaginal => DeliveryTypeTag.Vaginal,
        DeliveryType.VaginalWithTearOrEpisiotomy => DeliveryTypeTag.VaginalWithTearOrEpisiotomy,
        DeliveryType.AssistedVaginal => DeliveryTypeTag.AssistedVaginal,
        DeliveryType.PlannedCesarean => DeliveryTypeTag.PlannedCesarean,
        DeliveryType.EmergencyCesarean => DeliveryTypeTag.EmergencyCesarean,
        DeliveryType.VBAC => DeliveryTypeTag.VBAC,
        _ => DeliveryTypeTag.None
    };

    private static bool MatchesBereavement(BereavementApplicability applicability, CareContext ctx) => applicability switch
    {
        BereavementApplicability.Always => true,
        BereavementApplicability.RequiresSurvivingInfant => ctx.HasAnySurvivingInfant,
        BereavementApplicability.FullLossOnly => ctx.IsBereaved && !ctx.HasAnySurvivingInfant,
        BereavementApplicability.BereavedOnly => ctx.IsBereaved,
        _ => true
    };

    private const string Who = "WHO: Recommendations on Maternal and Newborn Care for a Positive Postnatal Experience (2022)";
    private const string Acog = "ACOG Committee Opinion 736: Optimizing Postpartum Care";
    private const string Cdc = "CDC: Urgent Maternal Warning Signs";
    private const string Nhs = "NHS: Recovering from a Caesarean Birth / Physical Health After Birth";
    private const string Rcog = "RCOG Patient Information: Perineal Tears and Episiotomies";
    private const string BereavementSource = "General perinatal bereavement care guidance (e.g. NHS, Postpartum Support International) -- discuss specifics with your provider";
    private const string WhoPreterm = "WHO: Recommendations for Care of the Preterm or Low-Birth-Weight Infant (2022)";

    // ── General content: 7 categories x 4 phases, identical for everyone.
    // Feeding and RedFlags deliberately have no entries here -- Feeding is
    // represented entirely via DoctorPrompts (whether normal feeding
    // support or milk-suppression-after-loss is relevant depends on
    // CareContext, so it can't be one-size-fits-all text); RedFlags is its
    // own separate, unfiltered list below.
    public static readonly List<RecoveryPlanContentItem> GeneralItems =
    [
        // WoundCare
        new("woundcare-days0to2", RecoveryPhase.Days0to2, RecoveryCategory.WoundCare,
            "Whether you had stitches, a tear, or a Cesarean incision, keep the area clean and dry, and change pads or dressings as advised. Check the wound daily for increasing redness, swelling, or a bad smell.",
            Who),
        new("woundcare-days3to7", RecoveryPhase.Days3to7, RecoveryCategory.WoundCare,
            "Wound discomfort should be gradually easing by now, not getting worse. Keep watching for signs of infection.",
            Who),
        new("woundcare-days8to42", RecoveryPhase.Days8to42, RecoveryCategory.WoundCare,
            "Most wounds are fully closed and much less tender by this point, though some scars stay sensitive for longer. Keep watching for new redness, discharge, or the wound opening.",
            Acog),
        new("woundcare-day43plus", RecoveryPhase.Day43Plus, RecoveryCategory.WoundCare,
            "By 6 weeks most wounds have healed well; your provider will check this at your postpartum visit. Scar tissue can stay slightly numb or tight for months -- that's normal, but mention it if it concerns you.",
            Acog),

        // Pain
        new("pain-days0to2", RecoveryPhase.Days0to2, RecoveryCategory.Pain,
            "Some pain -- from the wound, from afterpains as your uterus contracts, or general soreness -- is expected in the first days. Ask your provider what pain relief is appropriate for you; don't wait until pain is severe to ask for help.",
            Acog),
        new("pain-days3to7", RecoveryPhase.Days3to7, RecoveryCategory.Pain,
            "Pain should be trending down, not up, by the end of the first week. A sudden increase, or pain that stops responding to what's been helping, is worth calling your provider about.",
            Acog),
        new("pain-days8to42", RecoveryPhase.Days8to42, RecoveryCategory.Pain,
            "Lingering soreness with movement is common through this window, but sharp or worsening pain isn't -- mention it at your postpartum visit or sooner if it's severe.",
            Acog),
        new("pain-day43plus", RecoveryPhase.Day43Plus, RecoveryCategory.Pain,
            "Most postpartum pain has resolved by 6 weeks. Ongoing pain is worth raising with your provider rather than assuming you have to live with it.",
            Acog),

        // MobilityExercise
        new("mobility-days0to2", RecoveryPhase.Days0to2, RecoveryCategory.MobilityExercise,
            "Gentle movement as soon as you feel able helps circulation and recovery, but rest matters just as much -- let pain and fatigue set the pace, not a schedule.",
            Who),
        new("mobility-days3to7", RecoveryPhase.Days3to7, RecoveryCategory.MobilityExercise,
            "Short, gentle walks can gradually increase. Avoid anything that strains your abdomen or pelvic floor until you've been cleared.",
            Acog),
        new("mobility-days8to42", RecoveryPhase.Days8to42, RecoveryCategory.MobilityExercise,
            "Gentle pelvic floor exercises are usually safe to start around now, but check with your provider first.",
            Acog),
        new("mobility-day43plus", RecoveryPhase.Day43Plus, RecoveryCategory.MobilityExercise,
            "Most providers clear a return to regular exercise, including more vigorous activity, around the 6-week postpartum visit -- confirm with your own provider before resuming anything strenuous.",
            Acog),

        // Bleeding
        new("bleeding-days0to2", RecoveryPhase.Days0to2, RecoveryCategory.Bleeding,
            "Heavy bleeding (lochia) with clots is normal in the first couple of days, similar to a heavy period or more. Soaking through more than one pad an hour, for more than one hour, is a red flag -- see the red flags list.",
            Cdc),
        new("bleeding-days3to7", RecoveryPhase.Days3to7, RecoveryCategory.Bleeding,
            "Bleeding should be gradually lightening and changing from bright red to pink or brown. A sudden return to heavy, bright red bleeding after it had slowed down is a warning sign -- contact your provider.",
            Cdc),
        new("bleeding-days8to42", RecoveryPhase.Days8to42, RecoveryCategory.Bleeding,
            "Light spotting or brownish discharge can continue for several weeks. A return to heavy bleeding, or bleeding with a bad smell, is still worth calling about at any point in this window.",
            Acog),
        new("bleeding-day43plus", RecoveryPhase.Day43Plus, RecoveryCategory.Bleeding,
            "Bleeding has usually stopped entirely by 6 weeks, though your first period afterward can sometimes arrive around now and be heavier than usual.",
            Acog),

        // BladderBowel
        new("bladderbowel-days0to2", RecoveryPhase.Days0to2, RecoveryCategory.BladderBowel,
            "Passing urine may sting at first -- pouring warm water over the area while you go can help. A first bowel movement can feel intimidating; stool softeners (as your provider suggests) and not straining can ease this.",
            Nhs),
        new("bladderbowel-days3to7", RecoveryPhase.Days3to7, RecoveryCategory.BladderBowel,
            "You should be passing urine normally by now without pain. Ongoing difficulty, burning, or being unable to fully empty your bladder needs medical attention.",
            Acog),
        new("bladderbowel-days8to42", RecoveryPhase.Days8to42, RecoveryCategory.BladderBowel,
            "Some leaking of urine when coughing or laughing is common and often improves with pelvic floor exercises -- mention it at your postpartum visit rather than assuming it's permanent.",
            Acog),
        new("bladderbowel-day43plus", RecoveryPhase.Day43Plus, RecoveryCategory.BladderBowel,
            "Bladder and bowel function have usually returned to normal by 6 weeks. Ongoing incontinence, constipation, or pain is worth raising with your provider -- help is available.",
            Acog),

        // NutritionSleep
        new("nutritionsleep-days0to2", RecoveryPhase.Days0to2, RecoveryCategory.NutritionSleep,
            "Eat when you can, keep water nearby, and accept help with meals if it's offered. Sleep will be fragmented -- resting when you can, even in short stretches, matters more than a full night right now.",
            Acog),
        new("nutritionsleep-days3to7", RecoveryPhase.Days3to7, RecoveryCategory.NutritionSleep,
            "Iron-rich foods support recovery from blood loss at delivery. Sleep deprivation is exhausting but expected this early -- ask for help overnight if anyone can give it.",
            Acog),
        new("nutritionsleep-days8to42", RecoveryPhase.Days8to42, RecoveryCategory.NutritionSleep,
            "Continue eating regularly even when busy or tired -- it's easy to skip meals in this stretch. If exhaustion feels extreme rather than ordinary new-parent tired, mention it to your provider.",
            Acog),
        new("nutritionsleep-day43plus", RecoveryPhase.Day43Plus, RecoveryCategory.NutritionSleep,
            "Nutrition needs shift again if breastfeeding continues. Persistent, unrelenting exhaustion is worth a conversation with your provider, not something to push through.",
            Acog),

        // MentalHealth
        new("mentalhealth-days0to2", RecoveryPhase.Days0to2, RecoveryCategory.MentalHealth,
            "The 'baby blues' -- tearfulness, mood swings, feeling overwhelmed -- are very common in the first days and usually pass on their own within about two weeks.",
            Acog),
        new("mentalhealth-days3to7", RecoveryPhase.Days3to7, RecoveryCategory.MentalHealth,
            "If low mood, anxiety, or feeling overwhelmed is getting worse rather than better past the first week, that's a signal to reach out to your provider rather than wait it out.",
            Acog),
        new("mentalhealth-days8to42", RecoveryPhase.Days8to42, RecoveryCategory.MentalHealth,
            "Your provider will likely screen for postpartum depression and anxiety around your postpartum visit -- answer honestly; these are common and treatable, not a reflection of you as a parent.",
            Acog),
        new("mentalhealth-day43plus", RecoveryPhase.Day43Plus, RecoveryCategory.MentalHealth,
            "If sadness, anxiety, intrusive thoughts, or feeling disconnected haven't improved by 6 weeks, please tell your provider -- postpartum depression and anxiety are common and very treatable.",
            Acog),
    ];

    // ── DoctorPrompts: the only content filtered by delivery type,
    // complications, or CareContext. Every one is a topic to RAISE, never
    // an instruction on what to do about it.
    public static readonly List<AskYourDoctorPrompt> DoctorPrompts =
    [
        new("ask-cesarean-incision", RecoveryCategory.WoundCare,
            "Ask your doctor about caring for your Cesarean incision and when it's safe to bathe, lift, or drive.",
            Nhs, DeliveryTypeTags: DeliveryTypeTag.AnyCesarean),
        new("ask-cesarean-activity", RecoveryCategory.MobilityExercise,
            "Ask your doctor about restrictions on lifting, stairs, and driving after a Cesarean birth.",
            Nhs, DeliveryTypeTags: DeliveryTypeTag.AnyCesarean),
        new("ask-perineal-tear", RecoveryCategory.WoundCare,
            "Ask your doctor about caring for your tear or episiotomy and when to expect it to feel better.",
            Rcog, DeliveryTypeTags: DeliveryTypeTag.VaginalWithTearOrEpisiotomy),
        new("ask-third-fourth-degree-tear", RecoveryCategory.WoundCare,
            "Ask your doctor about the extra follow-up a third- or fourth-degree tear usually needs.",
            Rcog, RequiredComplications: DeliveryComplication.ThirdOrFourthDegreeTear),
        new("ask-vbac-signs", RecoveryCategory.RedFlags,
            "Ask your doctor what specific signs they'd want you to call about after a VBAC.",
            Acog, DeliveryTypeTags: DeliveryTypeTag.VBAC),
        new("ask-pph-followup", RecoveryCategory.Bleeding,
            "Ask your doctor about any follow-up blood tests or monitoring needed after a postpartum hemorrhage.",
            Acog, RequiredComplications: DeliveryComplication.PostpartumHemorrhage),
        new("ask-retained-placenta", RecoveryCategory.Bleeding,
            "Ask your doctor whether any extra bleeding follow-up is needed given the retained placental tissue noted at delivery.",
            Acog, RequiredComplications: DeliveryComplication.RetainedPlacenta),
        new("ask-preeclampsia-followup", RecoveryCategory.RedFlags,
            "Ask your doctor about blood pressure monitoring after delivery, since a blood-pressure concern was noted.",
            Cdc, RequiredComplications: DeliveryComplication.Preeclampsia),
        new("ask-blood-clot-followup", RecoveryCategory.RedFlags,
            "Ask your doctor about any follow-up needed for the blood clot concern noted around your delivery.",
            Acog, RequiredComplications: DeliveryComplication.BloodClot),
        new("ask-infection-followup", RecoveryCategory.WoundCare,
            "Ask your doctor about completing any prescribed treatment and what follow-up is needed after the infection noted around your delivery.",
            Who, RequiredComplications: DeliveryComplication.Infection),
        new("ask-anemia-followup", RecoveryCategory.NutritionSleep,
            "Ask your doctor about iron supplementation and any follow-up blood tests for the anemia noted around your delivery.",
            Who, RequiredComplications: DeliveryComplication.Anemia),
        new("ask-gestational-diabetes-followup", RecoveryCategory.NutritionSleep,
            "Ask your doctor about postpartum glucose testing, since gestational diabetes was part of your pregnancy.",
            Acog, RequiredComplications: DeliveryComplication.GestationalDiabetes),
        new("ask-multiple-birth-feeding", RecoveryCategory.Feeding,
            "Ask your doctor or a lactation consultant about feeding approaches for twins or more.",
            Who, RequiredComplications: DeliveryComplication.MultipleBirth, BereavementApplicability: BereavementApplicability.RequiresSurvivingInfant),
        new("ask-feeding-routine", RecoveryCategory.Feeding,
            "Ask your doctor or a lactation consultant about establishing a feeding routine and getting support if it's difficult.",
            Who, BereavementApplicability: BereavementApplicability.RequiresSurvivingInfant),
        new("ask-milk-after-loss", RecoveryCategory.Feeding,
            "Ask your doctor about options for easing or suppressing milk that may come in, even without a baby to feed.",
            BereavementSource, BereavementApplicability: BereavementApplicability.FullLossOnly),
        new("ask-grief-support", RecoveryCategory.MentalHealth,
            "Ask your doctor about grief support alongside your physical recovery, including a referral to a perinatal loss counsellor.",
            BereavementSource, BereavementApplicability: BereavementApplicability.BereavedOnly),
        new("ask-nicu-expressing-milk", RecoveryCategory.Feeding,
            "Ask the neonatal team about expressing and storing milk for your baby while they're in the NICU.",
            WhoPreterm, RequiredComplications: DeliveryComplication.PretermBirth | DeliveryComplication.NicuAdmission,
            BereavementApplicability: BereavementApplicability.RequiresSurvivingInfant),
        new("ask-nicu-kangaroo-care", RecoveryCategory.Feeding,
            "Ask the neonatal team when and how you can start kangaroo (skin-to-skin) care with your baby.",
            WhoPreterm, RequiredComplications: DeliveryComplication.PretermBirth | DeliveryComplication.NicuAdmission,
            BereavementApplicability: BereavementApplicability.RequiresSurvivingInfant),
        new("ask-nicu-discharge-criteria", RecoveryCategory.Feeding,
            "Ask the neonatal team what criteria your baby needs to meet before they can be discharged home.",
            WhoPreterm, RequiredComplications: DeliveryComplication.PretermBirth | DeliveryComplication.NicuAdmission,
            BereavementApplicability: BereavementApplicability.RequiresSurvivingInfant),
        new("ask-blood-transfusion-iron", RecoveryCategory.NutritionSleep,
            "Ask your doctor about iron supplementation after your blood transfusion.",
            Acog, RequiredComplications: DeliveryComplication.BloodTransfusion),
        new("ask-blood-transfusion-hemoglobin-followup", RecoveryCategory.Bleeding,
            "Ask your doctor about a follow-up hemoglobin or blood count check after your transfusion.",
            Acog, RequiredComplications: DeliveryComplication.BloodTransfusion),
    ];

    // ── RedFlags: one static list, identical for every profile, never
    // filtered by phase, delivery type, complications, or CareContext.
    public static IReadOnlyList<RedFlagItem> RedFlags => RedFlagsList;

    private static readonly List<RedFlagItem> RedFlagsList =
    [
        new("redflag-heavy-bleeding", "Heavy bleeding -- soaking through a pad in an hour or less, or passing clots larger than a golf ball.", Cdc),
        new("redflag-fever", "A fever.", Cdc),
        new("redflag-severe-headache", "A severe headache, especially with vision changes.", Cdc),
        new("redflag-chest-pain-breathing", "Chest pain or trouble breathing.", Cdc),
        new("redflag-leg-swelling", "A leg that is swollen, red, and painful.", Cdc),
        new("redflag-wound-infection", "A wound (incision or tear) that is increasingly red, swollen, warm, or draining pus.", Cdc),
        new("redflag-abdominal-pain", "Severe or worsening abdominal pain that feels different from normal cramping.", Cdc),
        new("redflag-self-harm", "Thoughts of harming yourself or your baby.", Cdc),
    ];
}
