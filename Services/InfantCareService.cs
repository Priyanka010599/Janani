// Services/InfantCareService.cs
// New agent, no prior C# equivalent — see agents/infant_care_agent/agent.py.
// Same HTTP-client shape as ElderNurtureService: one call per request,
// structured JSON reply — but not stateless. The Python side keeps a real
// persisted session per infant (see main.py), so the agent remembers past
// check-ins for this infant across calls, same as Companion's memory.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Models;

namespace Janani.Services;

public class InfantCareService(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "InfantCare";
    public string AgentRole => "Daily guidance for an infant profile";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InfantDailyPlan> GenerateDailyPlanAsync(
        InfantProfile infant, string statusDescription, string? recentActivitySummary,
        AppLanguage language = AppLanguage.English, CancellationToken ct = default)
    {
        var request = new
        {
            infantId = infant.Id,
            statusDescription,
            infantName = infant.Name,
            infantAgeWeeks = infant.AgeInWeeks,
            recentActivitySummary,
            language = language.ToString()
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/infant-care/plan", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<InfantDailyPlan>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty infant daily plan.");
    }
}
