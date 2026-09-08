// Services/PostpartumRecoveryMonitorService.cs
// Postpartum-side counterpart to PregnancyVitalsMonitorService — see
// agents/postpartum_recovery_agent/agent.py. Only ever called after
// PostpartumRecoveryChecker or EpdsScreeningChecker has already found a
// concern; this class doesn't decide whether to call itself, the caller does.
//
// Deviates from PregnancyVitalsMonitorService's signature in one way: it
// takes plain concern description strings rather than a strongly-typed
// List<Concern>, because this one service explains concerns from TWO
// different checkers (PostpartumRecoveryChecker and EpdsScreeningChecker)
// at once and there's no single call site that has both lists on hand
// together — each screening path only ever produces one or the other.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Models;

namespace Janani.Services;

public class PostpartumRecoveryMonitorService(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "PostpartumRecoveryMonitor";
    public string AgentRole => "Explains flagged postpartum recovery and EPDS concerns to the user";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record PostpartumAlertExplanation(string Explanation, string SuggestedAction);

    public async Task<(string Explanation, string SuggestedAction)> ExplainAsync(
        string userName, IEnumerable<string> concernDescriptions,
        AlertSeverity severity, AppLanguage language = AppLanguage.English, CancellationToken ct = default)
    {
        var request = new
        {
            userName,
            concerns = concernDescriptions.ToList(),
            severity = severity.ToString(),
            language = language.ToString()
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/postpartum-recovery/explain", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PostpartumAlertExplanation>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty postpartum recovery explanation.");
        return (result.Explanation, result.SuggestedAction);
    }
}
