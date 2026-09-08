// Services/HealthMonitorService.cs
// New agent, no prior C# equivalent — see agents/health_monitor_agent/agent.py.
// Only ever called after VitalsThresholdChecker has already found a concern;
// this class doesn't decide whether to call itself, the caller does.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Models;

namespace Janani.Services;

public class HealthMonitorService(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "HealthMonitor";
    public string AgentRole => "Explains flagged vitals concerns to the caregiver";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record HealthAlertExplanation(string Explanation, string SuggestedAction);

    public async Task<(string Explanation, string SuggestedAction)> ExplainAsync(
        ElderProfile elder, List<Concern> concerns,
        AlertSeverity severity, AppLanguage language = AppLanguage.English, CancellationToken ct = default)
    {
        var request = new
        {
            elderName = elder.Name,
            elderAge = elder.Age,
            elderRelation = elder.Relation,
            concerns = concerns.Select(c => c.Description).ToList(),
            severity = severity.ToString(),
            language = language.ToString()
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/health-monitor/explain", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<HealthAlertExplanation>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty health alert explanation.");
        return (result.Explanation, result.SuggestedAction);
    }
}
