using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class VaccinationScheduleTests
{
    [Fact]
    public void Newborn_BirthDoseVaccines_AreDueNow_NotOverdue()
    {
        // Grace period means a newborn isn't immediately "overdue" the
        // moment they're born, even though birth-dose vaccines are due at 0 weeks.
        var statuses = VaccinationSchedule.BuildStatus(ageInWeeks: 0, given: []);

        var birthDoses = statuses.Where(s => s.Vaccine.DueAtWeeks == 0).ToList();
        Assert.All(birthDoses, s => Assert.Equal(VaccinationStatus.DueNow, s.Status));
    }

    [Fact]
    public void OlderInfant_PastGracePeriod_EarlyVaccinesAreOverdue()
    {
        // 20 weeks old: the 6/10/14-week vaccines are all well past their
        // due date + 2-week grace, so should be Overdue if never given.
        var statuses = VaccinationSchedule.BuildStatus(ageInWeeks: 20, given: []);

        var sixWeekDose = statuses.First(s => s.Vaccine.Name == "OPV-1");
        Assert.Equal(VaccinationStatus.Overdue, sixWeekDose.Status);
    }

    [Fact]
    public void YoungInfant_FutureVaccines_AreUpcoming()
    {
        var statuses = VaccinationSchedule.BuildStatus(ageInWeeks: 2, given: []);

        var sixWeekDose = statuses.First(s => s.Vaccine.Name == "OPV-1");
        Assert.Equal(VaccinationStatus.Upcoming, sixWeekDose.Status);
    }

    [Fact]
    public void GivenVaccine_IsReportedAsGiven_RegardlessOfAge()
    {
        var givenAt = DateTime.UtcNow.AddDays(-30);
        var given = new List<VaccinationRecord>
        {
            new() { VaccineName = "BCG", GivenAt = givenAt }
        };

        // Even at an age where BCG would otherwise be overdue, a recorded
        // dose should override the age-based computation entirely.
        var statuses = VaccinationSchedule.BuildStatus(ageInWeeks: 30, given);

        var bcg = statuses.First(s => s.Vaccine.Name == "BCG");
        Assert.Equal(VaccinationStatus.Given, bcg.Status);
        Assert.Equal(givenAt, bcg.GivenAt);
    }

    [Fact]
    public void DueNowWindow_RespectsTheTwoWeekGrace()
    {
        // Exactly at the due week: DueNow, not yet Overdue.
        var atDue = VaccinationSchedule.BuildStatus(ageInWeeks: 6, given: []);
        Assert.Equal(VaccinationStatus.DueNow, atDue.First(s => s.Vaccine.Name == "OPV-1").Status);

        // One week past due, still inside the 2-week grace: still DueNow.
        var withinGrace = VaccinationSchedule.BuildStatus(ageInWeeks: 7, given: []);
        Assert.Equal(VaccinationStatus.DueNow, withinGrace.First(s => s.Vaccine.Name == "OPV-1").Status);

        // Past the grace window: Overdue.
        var pastGrace = VaccinationSchedule.BuildStatus(ageInWeeks: 9, given: []);
        Assert.Equal(VaccinationStatus.Overdue, pastGrace.First(s => s.Vaccine.Name == "OPV-1").Status);
    }

    [Fact]
    public void BuildStatus_ReturnsOneEntryPerScheduledVaccine()
    {
        var statuses = VaccinationSchedule.BuildStatus(ageInWeeks: 0, given: []);
        Assert.Equal(VaccinationSchedule.Schedule.Count, statuses.Count);
    }
}
