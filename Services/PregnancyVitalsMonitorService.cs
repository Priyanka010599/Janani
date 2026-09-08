// Services/PregnancyVitalsMonitorService.cs
// Pregnancy-side counterpart to HealthMonitorService (elder) — see
// agents/pregnancy_vitals_agent/agent.py. Only ever called after
// PregnancyVitalsThresholdChecker has already found a concern; this class
// doesn't decide whether to call itself, the caller does.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Models;

namespace Janani.Services;

public class PregnancyVitalsMonitorService(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "PregnancyVitalsMonitor";
    public string AgentRole => "Explains flagged pregnancy vitals concerns to the user";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record HealthAlertExplanation(string Explanation, string SuggestedAction);

    public async Task<(string Explanation, string SuggestedAction)> ExplainAsync(
        string userName, int pregnancyWeek, List<Concern> concerns,
        AlertSeverity severity, AppLanguage language = AppLanguage.English, CancellationToken ct = default)
    {
        var request = new
        {
            userName,
            pregnancyWeek,
            concerns = concerns.Select(c => c.Description).ToList(),
            severity = severity.ToString(),
            language = language.ToString()
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/pregnancy-vitals/explain", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<HealthAlertExplanation>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty pregnancy vitals explanation.");
        return (result.Explanation, result.SuggestedAction);
    }
}
