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

    public int CurrentWeek =>
        Math.Min(40, Math.Max(1,
            (int)((DateOnly.FromDateTime(DateTime.Today).ToDateTime(TimeOnly.MinValue)
                 - PregnancyStartDate.ToDateTime(TimeOnly.MinValue)).Days / 7)));
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

// A separate connection from ConnectedCalendarAccount, deliberately — sending
// email on the caregiver's behalf (gmail.send) is a different, more
// sensitive grant than syncing calendar events, requested via its own OAuth
// consent (see EmailNotificationService) rather than folded into an existing
// connection's scope.
public class ConnectedEmailAccount
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = "Google";
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public string EncryptedRefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public bool LastSendFailed { get; set; }
    public string? LastSendError { get; set; }
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