using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class HbncVisitScheduleTests
{
    private static readonly Dictionary<string, DateTime> NoneCompleted = new();

    [Fact]
    public void NullPostpartumDay_ReturnsEmpty()
    {
        // No DeliveryDate on file yet -- nothing to schedule.
        var statuses = HbncVisitSchedule.BuildStatus(postpartumDay: null, NoneCompleted);
        Assert.Empty(statuses);
    }

    [Fact]
    public void BuildStatus_ReturnsOneEntryPerScheduledVisit()
    {
        var statuses = HbncVisitSchedule.BuildStatus(postpartumDay: 0, NoneCompleted);
        Assert.Equal(HbncVisitSchedule.Schedule.Count, statuses.Count);
        Assert.Equal(6, statuses.Count);
    }

    // Day-zero convention locked in step 1 (UserProfile.PostpartumDay:
    // "Day 0 = the day of delivery itself"): on the day of delivery, the
    // day-3 visit is still 3 full days away, not already due. If Day 0
    // were ever miscounted as "day 1 already elapsed", this would flip to
    // DueNow and every visit boundary below would be off by one.
    [Fact]
    public void DeliveryDay_AllVisitsAreUpcoming_NoneDueYet()
    {
        var statuses = HbncVisitSchedule.BuildStatus(postpartumDay: 0, NoneCompleted);
        Assert.All(statuses, s => Assert.Equal(HbncVisitStatus.Upcoming, s.Status));
    }

    [Theory]
    [InlineData(2, HbncVisitStatus.Upcoming)]  // one day before due
    [InlineData(3, HbncVisitStatus.DueNow)]    // exactly due -- the day-zero boundary
    [InlineData(4, HbncVisitStatus.DueNow)]    // within the 2-day grace
    [InlineData(5, HbncVisitStatus.Overdue)]   // grace exhausted
    public void Day3Visit_DueOverdueBoundary(int postpartumDay, HbncVisitStatus expected)
    {
        var statuses = HbncVisitSchedule.BuildStatus(postpartumDay, NoneCompleted);
        var day3 = statuses.First(s => s.Visit.Key == "hbnc-day3");
        Assert.Equal(expected, day3.Status);
    }

    [Theory]
    [InlineData(41, HbncVisitStatus.Upcoming)]
    [InlineData(42, HbncVisitStatus.DueNow)]
    [InlineData(43, HbncVisitStatus.DueNow)]
    [InlineData(44, HbncVisitStatus.Overdue)]
    public void Day42Visit_DueOverdueBoundary(int postpartumDay, HbncVisitStatus expected)
    {
        var statuses = HbncVisitSchedule.BuildStatus(postpartumDay, NoneCompleted);
        var day42 = statuses.First(s => s.Visit.Key == "hbnc-day42");
        Assert.Equal(expected, day42.Status);
    }

    [Fact]
    public void NoTwoVisits_AreSimultaneouslyDueNow()
    {
        // The 2-day grace is deliberately shorter than every gap between
        // consecutive scheduled days (shortest gap: day3 to day7 = 4
        // days), so exactly one visit is ever DueNow at a time.
        foreach (var day in Enumerable.Range(0, 60))
        {
            var dueNowCount = HbncVisitSchedule.BuildStatus(day, NoneCompleted)
                .Count(s => s.Status == HbncVisitStatus.DueNow);
            Assert.True(dueNowCount <= 1, $"Day {day} had {dueNowCount} visits DueNow simultaneously");
        }
    }

    [Fact]
    public void CompletedVisit_IsReportedAsCompleted_RegardlessOfDay()
    {
        var completedAt = DateTime.UtcNow.AddDays(-10);
        var completed = new Dictionary<string, DateTime> { ["hbnc-day3"] = completedAt };

        // Even well past the point it would otherwise be Overdue, a
        // recorded completion overrides the day-based computation entirely.
        var statuses = HbncVisitSchedule.BuildStatus(postpartumDay: 60, completed);

        var day3 = statuses.First(s => s.Visit.Key == "hbnc-day3");
        Assert.Equal(HbncVisitStatus.Completed, day3.Status);
        Assert.Equal(completedAt, day3.CompletedAt);
    }

    [Fact]
    public void AllScheduledDays_MatchThePublishedHbncProtocol()
    {
        Assert.Equal([3, 7, 14, 21, 28, 42], HbncVisitSchedule.Schedule.Select(v => v.Day));
    }
}
