// Services/HbncVisitSchedule.cs
// Home Based Newborn Care (HBNC) visit tracking -- India's HBNC protocol
// calls for ASHA/health-worker home visits on postpartum days 3, 7, 14,
// 21, 28, and 42, checking on both mother and newborn. Same pattern as
// VaccinationSchedule: fixed reference data plus a pure status
// computation -- due days are facts, not something an LLM should guess at.
//
// Computed on read, same as VaccinationSchedule -- no dedicated visit
// table. Completion is recorded through the same generic
// RecoveryPlanItemCompletion mechanism the recovery plan itself uses
// (UserId + ItemKey + CompletedAt), keyed by each visit's own Key below,
// rather than a new dedicated table -- "did this visit happen" needs
// nothing more structured than that.
//
// PUSH REMINDERS ARE DELIBERATELY OUT OF SCOPE HERE: this class only
// computes a status for whoever loads the recovery-plan page to see.
// This app has no server-side scheduler at all -- every existing
// "reminder" (wwwroot/janani-reminders.js) is a client-side setTimeout
// that only fires while a tab is open. A real "your HBNC visit is due"
// push notification, sent on the correct day even when nobody has the
// app open, would need a BackgroundService or a Cloud Scheduler job wired
// in as shared app infrastructure -- HBNC would not be the only feature
// that wants it, so that's a separate, larger piece of work, not
// something to fold into this feature. Named here so it doesn't get lost.

using Janani.Models;

namespace Janani.Services;

public enum HbncVisitStatus
{
    Completed,
    Overdue,
    DueNow,
    Upcoming
}

public static class HbncVisitSchedule
{
    public record ScheduledVisit(string Key, int Day);

    // Grace window before an unmarked visit counts as Overdue rather than
    // merely DueNow -- 2 days, less than the shortest gap between two
    // scheduled visits (4 days, between day 3 and day 7), so two visits
    // are never simultaneously DueNow. Same reasoning as
    // VaccinationSchedule.GraceWeeks, scaled to a days-based cadence.
    private const int GraceDays = 2;

    public static readonly List<ScheduledVisit> Schedule =
    [
        new("hbnc-day3", 3),
        new("hbnc-day7", 7),
        new("hbnc-day14", 14),
        new("hbnc-day21", 21),
        new("hbnc-day28", 28),
        new("hbnc-day42", 42),
    ];

    public record VisitWithStatus(ScheduledVisit Visit, HbncVisitStatus Status, DateTime? CompletedAt);

    // postpartumDay is UserProfile.PostpartumDay -- already resolves
    // DeliveryDate against "today" and locks in Day 0 = the day of
    // delivery itself (see UserProfile.PostpartumDay's own comment and
    // HbncVisitScheduleTests for the boundary this implies: the day-3
    // visit becomes DueNow exactly at PostpartumDay 3, not 2 or 4). Null
    // (no DeliveryDate on file) means there's nothing to schedule yet.
    public static List<VisitWithStatus> BuildStatus(int? postpartumDay, IReadOnlyDictionary<string, DateTime> completedAtByKey)
    {
        if (postpartumDay is not { } day) return [];

        return Schedule.Select(v =>
        {
            if (completedAtByKey.TryGetValue(v.Key, out var completedAt))
                return new VisitWithStatus(v, HbncVisitStatus.Completed, completedAt);

            var status = day >= v.Day + GraceDays ? HbncVisitStatus.Overdue
                : day >= v.Day ? HbncVisitStatus.DueNow
                : HbncVisitStatus.Upcoming;
            return new VisitWithStatus(v, status, null);
        }).ToList();
    }
}
