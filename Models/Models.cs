// Models/Models.cs
using Janani.Services;

namespace Janani.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty; // public — shown in the nav bar
    public string Email { get; set; } = string.Empty;     // private — collected but never displayed
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly PregnancyStartDate { get; set; }
    public bool WorkingWomanMode { get; set; } = true;

    // Not everyone using Janani is pregnant — a caregiver (of any gender)
    // may only ever use the elder/infant-care side. Defaults true so
    // existing accounts keep seeing what they already see; toggle in
    // Profile.razor at any time, this isn't a one-time signup choice.
    public bool TracksPregnancy { get; set; } = true;

    // Same "flexible, not a fixed signup category" reasoning as
    // TracksPregnancy — these default false (unlike TracksPregnancy)
    // because most accounts aren't caring for an elder or infant, and
    // showing those nav items unconditionally to everyone was clutter.
    // Set at registration (Register.razor) and editable anytime (Profile.razor).
    public bool CaresForElder { get; set; }
    public bool CaresForInfant { get; set; }

    // Every agent call across the app used to hardcode AppLanguage.English
    // regardless of this enum's 6 supported languages actually existing —
    // this is the one persisted preference that drives all of them now.
    public AppLanguage Language { get; set; } = AppLanguage.English;

    // Who a Critical pregnancy vitals alert auto-emails — see
    // EmailNotificationService and Vitals.razor. Same pattern as
    // ElderProfile.DoctorName/DoctorEmail.
    public string? DoctorName { get; set; }
    public string? DoctorEmail { get; set; }

    // Shared secret for pairing a pregnancy vitals edge device to THIS
    // account specifically — unlike elder/infant vitals ingest (keyed by a
    // dependent profile id + one global token), pregnancy vitals are keyed
    // directly by UserId, the real login-capable account, so a single global
    // token would let anyone holding it inject fake Critical alerts at any
    // user by guessing an id. Null until the user generates one in Settings.
    public string? DevicePairingToken { get; set; }

    public int CurrentWeek =>
        Math.Min(40, Math.Max(1,
            (int)((DateOnly.FromDateTime(DateTime.Today).ToDateTime(TimeOnly.MinValue)
                 - PregnancyStartDate.ToDateTime(TimeOnly.MinValue)).Days / 7)));

    // ── Postpartum Recovery Guide ──────────────────────────────────────
    // Null until she has actually delivered — capturing these doesn't
    // switch the app into any "postpartum mode" (CurrentWeek above is
    // untouched); they're just facts the recovery-plan generator (a later
    // step) will read once they exist.
    public DateOnly? DeliveryDate { get; set; }
    public DeliveryType? DeliveryType { get; set; }
    public DeliveryComplication DeliveryComplications { get; set; } = DeliveryComplication.None;
    public MaternalFeedingMethod? FeedingMethod { get; set; }

    // Day 0 = the day of delivery itself (matches HBNC's own day-0/3/7/14/
    // 21/28/42 visit numbering — see the HBNC reminder step). Uses
    // DateTime.Today, the same clock source as CurrentWeek above, so the
    // two stay consistent with each other. Null when there's no
    // DeliveryDate yet, when DeliveryDate hasn't happened yet (it's after
    // today), or when it predates PregnancyStartDate (an impossible
    // entry) — never clamped at the top end, so a long-recovered mother's
    // day count (200+) still reads correctly. Get-only, no setter, so EF
    // Core's convention leaves it unmapped — same as CurrentWeek, and
    // confirmed by inspecting the generated schema (see PostpartumDayTests
    // and the Program.cs startup verification).
    public int? PostpartumDay
    {
        get
        {
            if (DeliveryDate is not { } deliveryDate) return null;
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (deliveryDate > today || deliveryDate < PregnancyStartDate) return null;
            return (today.ToDateTime(TimeOnly.MinValue) - deliveryDate.ToDateTime(TimeOnly.MinValue)).Days;
        }
    }

    // ── Bereavement ──────────────────────────────────────────────────────
    // Deliberately separate from DeliveryType: a stillbirth or neonatal
    // death can follow ANY delivery type (an emergency C-section still
    // needs C-section wound care), so the two axes must stay independent.
    // Null (LiveBirth-equivalent) for every account that hasn't recorded an
    // outcome — including caregiver-only accounts, which never set this at
    // all — so nothing here changes existing behavior by default.
    public BirthOutcome? BirthOutcome { get; set; }

    // The baby's name, if named — offering this matters in perinatal
    // bereavement care specifically. BabyDate is deliberately distinct from
    // DeliveryDate: for a neonatal death the baby may have lived days after
    // birth, so BabyDate holds the date of death while DeliveryDate stays
    // the birth date; for stillbirth/late miscarriage the two dates usually
    // coincide and BabyDate is simply left matching or unset.
    public string? BabyName { get; set; }
    public DateOnly? BabyDate { get; set; }

    // Resolves BirthOutcome (+ DeliveryType, where relevant) into the flags
    // every other layer reads — routing/nav, VaccinationSchedule,
    // PushNotificationService, the companion agent — so none of them
    // re-derive "is this bereaved" from the raw enum themselves. Computed,
    // not stored: always current, and confirmed unmapped by EF the same
    // way as CurrentWeek/PostpartumDay above.
    //
    // PartialLossMultiple is deliberately NOT folded into a single global
    // "bereaved implies no infant content" rule — a surviving twin still
    // needs the infant side. HasAnySurvivingInfant is the one flag that
    // stays true for it, and every gate keyed on it (nav, routing, the
    // vaccination schedule, per-child push notifications) therefore stays
    // per-child rather than shutting off the whole account.
    public CareContext CareContext => new(BirthOutcome);
}

public enum BirthOutcome
{
    LiveBirth,
    Stillbirth,
    NeonatalDeath,
    LateMiscarriage,
    PartialLossMultiple
}

// CONSTRAINT FOR THE RECOVERY-PLAN GENERATOR: none of the flags below ever
// suppress the physical recovery plan itself -- wound care, pain, mobility,
// bleeding, bladder/bowel, mental health, and red-flag content must render
// in full for a bereaved profile exactly as for any other delivery type or
// complication set. A bereaved mother still had a body that went through a
// birth; that recovery doesn't stop because the baby didn't survive. The
// ONLY things these flags gate are infant-specific and feeding-specific
// content (infant nav/routes, the vaccination schedule, push notifications
// about a baby, and which flavor of feeding/lactation guidance applies).
// If a future flag here would hide anything from the physical plan's own
// categories, that is a bug -- add a new, narrowly-scoped flag instead of
// widening one of these.
public class CareContext(BirthOutcome? outcome)
{
    public BirthOutcome? Outcome { get; } = outcome;

    // Any recorded outcome other than a live birth counts as bereaved —
    // including PartialLossMultiple, which still lost a baby even though
    // another survived.
    public bool IsBereaved => Outcome is not null and not BirthOutcome.LiveBirth;

    // False only for a full loss (Stillbirth/NeonatalDeath/LateMiscarriage).
    // Null (no outcome recorded), LiveBirth, and PartialLossMultiple all
    // read true — the first two obviously have a living infant, and
    // PartialLossMultiple's surviving twin means infant content still
    // applies, just to that one real InfantProfile row (there's never a
    // row created for the baby who didn't survive, so nothing further
    // needs a per-child "deceased" flag on InfantProfile itself).
    public bool HasAnySurvivingInfant => Outcome is not (BirthOutcome.Stillbirth
        or BirthOutcome.NeonatalDeath or BirthOutcome.LateMiscarriage);

    // Drives nav visibility, route access, and the VaccinationSchedule
    // suppression guard — all three read this one flag rather than the
    // raw outcome.
    public bool SuppressInfantContent => !HasAnySurvivingInfant;

    // These two are deliberately separate, not one "suppress lactation"
    // flag — a single flag named that way would also hide the milk-
    // suppression guidance itself, which is exactly the content a
    // full-loss mother needs. Both share the same trigger (full loss:
    // Stillbirth/NeonatalDeath/LateMiscarriage) but point opposite ways:
    // hide "how to breastfeed" (there's no baby to feed), show "how to
    // manage/suppress milk coming in with no baby" instead. Both are
    // false for PartialLossMultiple — the surviving twin still feeds
    // normally, with no separate suppression-guidance need of its own.
    public bool SuppressBreastfeedingGuidance => IsBereaved && !HasAnySurvivingInfant;
    public bool ShowLactationSuppressionGuidance => IsBereaved && !HasAnySurvivingInfant;
}

// How the baby was born. Distinct from DeliveryComplication (below) —
// a VBAC or an emergency Cesarean isn't itself a complication, and a
// complication (e.g. postpartum hemorrhage) can happen alongside any of
// these. The recovery-plan generator (later step) keys its phased content
// off both axes together.
public enum DeliveryType
{
    Vaginal,
    VaginalWithTearOrEpisiotomy,
    AssistedVaginal, // forceps or vacuum
    PlannedCesarean,
    EmergencyCesarean,
    VBAC
}

// [Flags] so more than one can apply at once (e.g. Preeclampsia + Infection).
// Deliberately a fixed, named vocabulary rather than free text — the
// recovery-plan generator needs to match on these deterministically, the
// same reason PregnancyVitalsThresholdChecker works off fixed thresholds
// rather than an LLM's read of a symptom description. Values are explicit
// powers of two (not bit-shift expressions) so each flag's numeric value
// is visible at a glance and appending a new one can't silently miscount.
[Flags]
public enum DeliveryComplication
{
    None = 0,
    PostpartumHemorrhage = 1,
    Preeclampsia = 2,
    Infection = 4,
    RetainedPlacenta = 8,
    ThirdOrFourthDegreeTear = 16,
    GestationalDiabetes = 32,
    Anemia = 64,
    BloodClot = 128,
    BloodTransfusion = 256,
    PretermBirth = 512,
    NicuAdmission = 1024,
    MultipleBirth = 2048
}

public enum MaternalFeedingMethod
{
    Breastfeeding,
    Formula,
    Mixed,
    ExclusivePumping
}

// Records that a mother has marked a recovery-plan item done/acknowledged.
// Keyed by the item's stable string Key rather than a foreign key, because
// the content itself is computed on read (Services/RecoveryPlanGenerator.cs),
// not stored in a table -- the same reason VaccinationRecord below keys on
// VaccineName (a string) rather than a foreign key into a "scheduled
// vaccines" table that doesn't exist either.
public class RecoveryPlanItemCompletion
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ItemKey { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

public enum MoodType
{
    Exhausted, Anxious, Nauseous, Emotional, Happy, Energetic, Overwhelmed, Peaceful
}

public class MoodCheckIn
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public MoodType Mood { get; set; }
    public string? AdditionalNote { get; set; }
    public string? GeneratedPlan { get; set; }
}

public enum BleedingLevel
{
    None,
    Light,
    Moderate,
    Heavy,
    SoakingPadHourly
}

public enum WoundStatus
{
    HealingWell,
    MildRednessOrSwelling,
    SignificantConcern
}

// Postpartum-side daily check-in -- UserId-keyed like PregnancyVitalsReading
// (no separate shareable profile for the mother herself, UserProfile is
// it). Wound status is one categorical value regardless of delivery type;
// WHICH question wording is shown (incision vs perineum) is a UI concern
// (see PostpartumCheckInQuestions in Services/PostpartumRecoveryChecker.cs)
// -- the severity evaluation doesn't need to know which was asked. Mood
// reuses MoodType (a casual daily tag, same as MoodCheckIn) -- it's
// informational context for the agent, not itself something
// PostpartumRecoveryChecker raises a concern from; that's what the
// separate, validated EpdsScreening below is for.
public class PostpartumCheckIn
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public int PainLevel { get; set; } // 0-10
    public BleedingLevel Bleeding { get; set; }
    public decimal? TemperatureC { get; set; }
    public WoundStatus Wound { get; set; }
    public MoodType Mood { get; set; }
    public string? Notes { get; set; }
}

// Created when PostpartumRecoveryChecker or EpdsScreeningChecker finds a
// concern -- same shape as PregnancyAlert, with two nullable source FKs
// since two different checkers feed this one alert type (exactly one is
// ever set per row).
public class PostpartumAlert
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? PostpartumCheckInId { get; set; }
    public int? EpdsScreeningId { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public bool Acknowledged { get; set; }
}

// The Edinburgh Postnatal Depression Scale (EPDS) -- Cox, Holden &
// Sagovsky, 1987. Deliberately separate from MoodCheckIn: MoodCheckIn is a
// casual one-tap daily mood tag with no clinical scoring; this is the full
// 10-item validated instrument, administered on ScheduledDay 10 and 42.
// Each item scores 0-3 per the published scale; Item10 (self-harm
// thoughts) escalates on ANY nonzero answer regardless of TotalScore --
// see EpdsScreeningChecker, which takes no BirthOutcome/CareContext input
// at all, so the same threshold applies to every profile, bereaved or not.
//
// IMPORTANT: item wording and response-option scoring keys are
// reconstructed here from well-established public knowledge of the
// instrument, not transcribed from a verified primary source in this
// session -- verify against the official published scale before this
// feeds a real screening UI. EPDS is free to reproduce for non-commercial
// clinical use provided the source is credited and the whole scale is
// used, per its standard usage terms.
public class EpdsScreening
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime AdministeredAt { get; set; } = DateTime.UtcNow;
    public int ScheduledDay { get; set; } // 10 or 42
    public int Item1 { get; set; }
    public int Item2 { get; set; }
    public int Item3 { get; set; }
    public int Item4 { get; set; }
    public int Item5 { get; set; }
    public int Item6 { get; set; }
    public int Item7 { get; set; }
    public int Item8 { get; set; }
    public int Item9 { get; set; }
    public int Item10 { get; set; } // self-harm thoughts -- see EpdsScreeningChecker

    public int TotalScore =>
        Item1 + Item2 + Item3 + Item4 + Item5 + Item6 + Item7 + Item8 + Item9 + Item10;
}

public class JournalEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int WeekOfPregnancy { get; set; }
    public string? MoodEmoji { get; set; }
}

public class JournalPhoto
{
    public int Id { get; set; }
    public int JournalEntryId { get; set; }
    public string ImageData { get; set; } = string.Empty; // base64-encoded image bytes
    public string ContentType { get; set; } = "image/jpeg";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppointmentEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? DoctorName { get; set; }
    public string? Location { get; set; }
    public string? QuestionsToAsk { get; set; }
    public string? NotesAfterVisit { get; set; }
    public bool Completed { get; set; }
    public string? GoogleEventId { get; set; }
}

public class ConnectedCalendarAccount
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = "Google";
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public string EncryptedRefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string GoogleCalendarId { get; set; } = "primary";
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public bool SyncEnabled { get; set; } = true;
    public bool LastSyncFailed { get; set; }
    public string? LastSyncError { get; set; }
}

public class WaterIntakeEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public int GlassesCount { get; set; }
}
public class KickSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public int KickCount { get; set; }
    public int DurationMinutes { get; set; }
}

// Pregnancy-side counterpart to VitalsReading (elder) — same rule: a
// deterministic checker (PregnancyVitalsThresholdChecker) decides whether
// this is concerning, an LLM only ever explains an already-flagged concern.
// Keyed by UserId directly since a pregnancy "profile" is just UserProfile,
// not a separate shareable profile like ElderProfile/InfantProfile — see
// InfantAlert's comment below for why this is its own type rather than
// widening VitalsReading/Alert with a nullable dual-subject FK.
public class PregnancyVitalsReading
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public int? SystolicBp { get; set; }
    public int? DiastolicBp { get; set; }
    public int? HeartRate { get; set; }
    public decimal? TemperatureC { get; set; }
    public int? OxygenSaturation { get; set; }
    public string? Notes { get; set; }

    // Null/empty = manually entered. Set to a device name when synced from
    // a connected device (currently only simulated — see Vitals.razor's
    // SimulateDeviceSyncAsync — pending a real vendor API integration).
    public string? Source { get; set; }

    // Same idempotency purpose as VitalsReading.DeviceReadingId (elder) —
    // see that field's comment.
    public string? DeviceReadingId { get; set; }
}

// Created when PregnancyVitalsThresholdChecker finds a reading outside a
// safe range. Same shape as Alert/InfantAlert, just UserId-keyed.
public class PregnancyAlert
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? PregnancyVitalsReadingId { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public bool Acknowledged { get; set; }
}

public class Medicine
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Dosage { get; set; }
    public string ScheduleTimes { get; set; } = string.Empty; // comma-separated "HH:mm" values, e.g. "08:00,20:00"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<TimeOnly> GetScheduleTimes() =>
        ScheduleTimes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => TimeOnly.TryParse(t, out var parsed) ? parsed : (TimeOnly?)null)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .OrderBy(t => t)
            .ToList();
}

public class MedicineDose
{
    public int Id { get; set; }
    public int MedicineId { get; set; }
    public DateOnly Date { get; set; }
    public string ScheduledTime { get; set; } = string.Empty; // "HH:mm"
    public bool Taken { get; set; }
    public DateTime? TakenAt { get; set; }
}

public class EmergencyContact
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty; // e.g. Doctor, Partner, Mother, Friend
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsDoctor { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Structured output from the AI mood planning engine
public class DailyPlan
{
    public string Acknowledgment { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = [];
    public string CalmingRecommendation { get; set; } = string.Empty;
    public string Affirmation { get; set; } = string.Empty;
    public string WeekNote { get; set; } = string.Empty;
}

// ── Elder care — new for the multi-agent build-out, no prior C# equivalent ──
// Owned directly by the caregiver's UserId, same pattern as EmergencyContact/
// Medicine — the elder isn't a login-capable User themselves. Multi-caregiver
// sharing of one elder profile (via a CareRelationship join) is a possible
// later step, not built now — no current feature needs it yet.
public class ElderProfile
{
    public int Id { get; set; }
    public int UserId { get; set; } // owning caregiver
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Relation { get; set; } = string.Empty; // e.g. Mother, Father, Grandmother
    public string? MedicalNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Who a Critical alert auto-emails — see EmailNotificationService and
    // ElderCare.razor's SaveVitalsAsync. Deliberately per-elder, not a single
    // household-wide contact: different dependents can have different doctors.
    public string? DoctorName { get; set; }
    public string? DoctorEmail { get; set; }
}

// Manually entered for now (per the current build phase — device/wearable
// integration is a later step). HealthMonitorAgent will read this table to
// evaluate alert thresholds once that agent exists.
public class VitalsReading
{
    public int Id { get; set; }
    public int ElderProfileId { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public int? SystolicBp { get; set; }
    public int? DiastolicBp { get; set; }
    public int? HeartRate { get; set; }
    public decimal? TemperatureC { get; set; }
    public int? OxygenSaturation { get; set; }
    public string? Notes { get; set; }

    // Null/empty = manually entered. Set to a device name when synced from
    // a connected device (currently only simulated — see ElderCare.razor's
    // SimulateDeviceSyncAsync — pending a real vendor API integration).
    public string? Source { get; set; }

    // Client-generated idempotency key for device-uploaded readings (e.g. the
    // edge simulator's/a real ESP32's reading_id) — a retried upload of the
    // same reading must never produce a second row. Null for manually entered
    // readings, which have no such key. Enforced by a partial unique index
    // (see Program.cs's VitalsReadings retrofit) since most rows have none.
    public string? DeviceReadingId { get; set; }
}

public enum ElderMoodType
{
    Cheerful, Calm, Tired, Anxious, Confused, InPain, Restless, Content
}

public class ElderCheckIn
{
    public int Id { get; set; }
    public int ElderProfileId { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public ElderMoodType Mood { get; set; }
    public string? AdditionalNote { get; set; }
}

// Structured output from ElderNurtureAgent — same shape as DailyPlan
// deliberately, but a distinct type since it's a distinct agent/contract.
public class ElderDailyPlan
{
    public string Acknowledgment { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = [];
    public string CalmingRecommendation { get; set; } = string.Empty;
    public string CaregiverNote { get; set; } = string.Empty;
}

public enum AlertSeverity
{
    Warning, Critical
}

// Created when VitalsThresholdChecker (Services/VitalsThresholdChecker.cs)
// finds a reading outside a safe range — Message comes from HealthMonitorAgent,
// which only explains an already-flagged concern in plain language; it never
// makes the trigger decision itself. Shown in-app only for now — no push
// notification yet, that needs a Firebase project/VAPID keys to be set up first.
public class Alert
{
    public int Id { get; set; }
    public int ElderProfileId { get; set; }
    public int? VitalsReadingId { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public bool Acknowledged { get; set; }
}

// ── Infant care — new for the multi-agent build-out, no prior C# equivalent ──
// Same ownership pattern as ElderProfile: owned by the parent/caregiver's
// UserId, the infant isn't a login-capable User.
public class InfantProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Who a Critical alert auto-emails — see EmailNotificationService and
    // InfantCare.razor's SaveGrowthAsync.
    public string? DoctorName { get; set; }
    public string? DoctorEmail { get; set; }

    public int AgeInWeeks =>
        Math.Max(0, (DateOnly.FromDateTime(DateTime.Today).ToDateTime(TimeOnly.MinValue)
            - DateOfBirth.ToDateTime(TimeOnly.MinValue)).Days / 7);
}

public enum FeedingType
{
    Breastfeeding, Formula, Solid
}

public class FeedingLog
{
    public int Id { get; set; }
    public int InfantProfileId { get; set; }
    public DateTime FedAt { get; set; } = DateTime.UtcNow;
    public FeedingType Type { get; set; }
    public string? Notes { get; set; }
}

public class SleepLog
{
    public int Id { get; set; }
    public int InfantProfileId { get; set; }
    public DateTime SleepStart { get; set; }
    public DateTime? SleepEnd { get; set; }
    public string? Notes { get; set; }
}

// Manually entered, same as VitalsReading — device integration is a later step.
public class GrowthEntry
{
    public int Id { get; set; }
    public int InfantProfileId { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public decimal? WeightKg { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? HeadCircumferenceCm { get; set; }

    // Null/empty = manually entered. Set to a device name when synced from
    // a connected device — see VitalsReading.Source for the same pattern.
    public string? Source { get; set; }
}

// Structured output from InfantCareAgent — same shape family as
// DailyPlan/ElderDailyPlan, MilestoneNote plays WeekNote's role.
public class InfantDailyPlan
{
    public string Acknowledgment { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = [];
    public string MilestoneNote { get; set; } = string.Empty;
    public string CaregiverNote { get; set; } = string.Empty;
}

// One row per vaccine actually given. The schedule itself (which vaccines,
// due at what age) is fixed reference data — see Services/VaccinationSchedule.cs
// — not stored per-infant; only the given/not-given fact needs a row.
public class VaccinationRecord
{
    public int Id { get; set; }
    public int InfantProfileId { get; set; }
    public string VaccineName { get; set; } = string.Empty;
    public DateTime GivenAt { get; set; } = DateTime.UtcNow;
}

// Infant-side counterpart to Alert (elder vitals) — a separate table rather
// than widening Alert to a nullable dual-subject FK, since Alert already
// has live data and SQLite can't cheaply relax a NOT NULL column in place.
// Created by GrowthThresholdChecker (Services/GrowthThresholdChecker.cs);
// same rule as the elder side: the checker decides, an LLM only explains.
public class InfantAlert
{
    public int Id { get; set; }
    public int InfantProfileId { get; set; }
    public int? GrowthEntryId { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public bool Acknowledged { get; set; }
}

// A device's FCM registration token. One row per browser/device the user
// has enabled push on; a device that re-registers (token refreshed, or a
// different account signs in on the same browser) updates its existing row
// rather than duplicating, keyed on the token itself since FCM tokens are
// unique per device+app+sender.
public class PushSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Grants a second caregiver access to an elder/infant profile they don't
// own. Exactly one of ElderProfileId/InfantProfileId is set per row — kept
// as two nullable FKs on one table rather than two separate tables since
// every consumer (CareAccessService) needs identical logic for both, and a
// caregiver's shared access list is naturally one combined thing to show.
// The owning ElderProfile.UserId/InfantProfile.UserId is unchanged and
// still the "primary" caregiver; this is purely additive access.
public class SharedCareAccess
{
    public int Id { get; set; }
    public int? ElderProfileId { get; set; }
    public int? InfantProfileId { get; set; }
    public int UserId { get; set; } // the caregiver granted access
    public int GrantedByUserId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}