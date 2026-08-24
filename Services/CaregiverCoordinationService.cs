// Services/CaregiverCoordinationService.cs
// New agent, no prior C# equivalent — see agents/caregiver_agent/agent.py.
// Builds the "who they care for" summary from the DB (plain data work, same
// division of labor as everywhere else: C# owns data access, the agent
// only ever sees the already-assembled text) and forwards the question.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Data;
using Janani.Models;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public class CaregiverCoordinationService(HttpClient agentServiceClient, AppDbContext db, CurrentUserService currentUser, CareAccessService careAccess) : IBloomAgent
{
    public string AgentName => "CaregiverCoordination";
    public string AgentRole => "Answers a caregiver's questions across everyone they care for";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record CaregiverAnswer(string Answer);

    public async Task<string> AskAsync(string caregiverName, string question, CancellationToken ct = default)
    {
        var careSummary = await BuildCareSummaryAsync(ct);
        var language = await GetLanguageAsync(ct);

        var request = new
        {
            question,
            caregiverName,
            careSummary,
            language = language.ToString()
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/caregiver/ask", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CaregiverAnswer>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty answer.");
        return result.Answer;
    }

    private async Task<AppLanguage> GetLanguageAsync(CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync();
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return profile?.Language ?? AppLanguage.English;
    }

    private async Task<string> BuildCareSummaryAsync(CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync();
        var accessibleElderIds = await careAccess.GetAccessibleElderIdsAsync(userId, ct);
        var accessibleInfantIds = await careAccess.GetAccessibleInfantIdsAsync(userId, ct);
        var elders = await db.ElderProfiles.AsNoTracking()
            .Where(e => accessibleElderIds.Contains(e.Id))
            .ToListAsync(ct);
        var infants = await db.InfantProfiles.AsNoTracking()
            .Where(i => accessibleInfantIds.Contains(i.Id))
            .ToListAsync(ct);

        if (elders.Count == 0 && infants.Count == 0) return "No one added yet.";

        var lines = new List<string>();
        foreach (var elder in elders)
        {
            var recentVitals = await db.VitalsReadings.AsNoTracking()
                .Where(v => v.ElderProfileId == elder.Id)
                .OrderByDescending(v => v.RecordedAt)
                .FirstOrDefaultAsync(ct);
            var openAlerts = await db.Alerts.AsNoTracking()
                .Where(a => a.ElderProfileId == elder.Id && !a.Acknowledged)
                .CountAsync(ct);

            var line = $"- {elder.Name} ({elder.Relation}, age {elder.Age})";
            if (recentVitals != null)
                line += $", last vitals recorded {recentVitals.RecordedAt:yyyy-MM-dd}";
            if (openAlerts > 0)
                line += $", {openAlerts} unacknowledged alert(s)";
            if (!string.IsNullOrWhiteSpace(elder.MedicalNotes))
                line += $". Notes: {elder.MedicalNotes}";
            lines.Add(line);
        }

        foreach (var infant in infants)
        {
            var given = await db.VaccinationRecords.AsNoTracking()
                .Where(v => v.InfantProfileId == infant.Id)
                .ToListAsync(ct);
            var statuses = VaccinationSchedule.BuildStatus(infant.AgeInWeeks, given);
            var overdue = statuses.Where(s => s.Status == VaccinationStatus.Overdue).Select(s => s.Vaccine.Name).ToList();
            var due = statuses.Where(s => s.Status == VaccinationStatus.DueNow).Select(s => s.Vaccine.Name).ToList();
            var openGrowthAlerts = await db.InfantAlerts.AsNoTracking()
                .Where(a => a.InfantProfileId == infant.Id && !a.Acknowledged)
                .CountAsync(ct);

            var line = $"- {infant.Name} (infant, {infant.AgeInWeeks} weeks old)";
            if (overdue.Count > 0)
                line += $", OVERDUE vaccines: {string.Join(", ", overdue)}";
            if (due.Count > 0)
                line += $", due now: {string.Join(", ", due)}";
            if (overdue.Count == 0 && due.Count == 0)
                line += ", vaccinations up to date";
            if (openGrowthAlerts > 0)
                line += $", {openGrowthAlerts} unacknowledged growth concern(s)";
            lines.Add(line);
        }

        return string.Join("\n", lines);
    }
}
