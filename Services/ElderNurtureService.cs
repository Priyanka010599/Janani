// Services/ElderNurtureService.cs
// New agent, no prior C# equivalent — see agents/elder_nurture_agent/agent.py
// for the prompt and model call. One call per request, structured JSON
// reply — but not stateless. The Python side keeps a real persisted session
// per elder (see main.py), so the agent remembers past check-ins for this
// elder across calls, same as Companion's memory.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Models;

namespace Janani.Services;

public class ElderNurtureService(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "ElderNurture";
    public string AgentRole => "Daily caregiving guidance for an elder profile";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ElderDailyPlan> GenerateDailyPlanAsync(
        ElderProfile elder, string moodDescription, string? recentVitalsSummary,
        string? additionalNote = null, AppLanguage language = AppLanguage.English, CancellationToken ct = default)
    {
        var request = new
        {
            elderId = elder.Id,
            moodDescription,
            elderName = elder.Name,
            elderAge = elder.Age,
            elderRelation = elder.Relation,
            recentVitalsSummary,
            additionalNote,
            language = language.ToString()
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/elder-nurture/plan", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ElderDailyPlan>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty elder daily plan.");
    }
}
