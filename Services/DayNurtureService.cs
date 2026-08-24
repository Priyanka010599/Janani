// Services/DayNurtureService.cs
// The core differentiating feature: mood + week of pregnancy + context
// → a warm, personalized daily plan generated via structured output.
//
// This is the thing no other pregnancy app does. The structured output
// schema ensures consistent UI rendering regardless of what the model says.
//
// Ported to ADK: see agents/day_nurture_agent/agent.py for the prompt and
// model call. This class resolves the mood description + cache (UI-perf
// concerns, not agent behavior) and delegates generation over HTTP.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Models;

namespace Janani.Services;

public class DayNurtureService(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "DayNurture";
    public string AgentRole => "Personalized mood and daily nurture planner";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<MoodType, string> MoodDescriptions = new()
    {
        [MoodType.Exhausted]    = "deeply tired and low on energy",
        [MoodType.Anxious]      = "worried and anxious about pregnancy or the future",
        [MoodType.Nauseous]     = "nauseous and physically uncomfortable",
        [MoodType.Emotional]    = "emotionally sensitive and a bit overwhelmed",
        [MoodType.Happy]        = "happy and content",
        [MoodType.Energetic]    = "energetic and motivated",
        [MoodType.Overwhelmed]  = "overwhelmed with everything to do",
        [MoodType.Peaceful]     = "calm and at peace"
    };

    // Repeat check-ins with the same mood/week/note in the same part of the day
    // produce the same prompt — cache per-circuit so a repeat "check in again"
    // doesn't pay for a fresh ~90s+ generation.
    private readonly Dictionary<(MoodType, int, bool, string, string), DailyPlan> _cache = [];

    public async Task<DailyPlan> GenerateDailyPlanAsync(
        MoodType mood, AgentContext ctx, string? additionalNote = null,
        CancellationToken ct = default)
    {
        var timeOfDay = DateTime.Now.Hour < 12 ? "morning" : DateTime.Now.Hour < 17 ? "afternoon" : "evening";
        var cacheKey = (mood, ctx.PregnancyWeek, ctx.WorkingWomanMode, additionalNote ?? "", timeOfDay);
        if (_cache.TryGetValue(cacheKey, out var cachedPlan))
            return cachedPlan;

        var moodDescription = MoodDescriptions.GetValueOrDefault(mood, mood.ToString().ToLower());

        var request = new
        {
            moodDescription,
            timeOfDay,
            additionalNote,
            userName = ctx.UserName,
            pregnancyWeek = ctx.PregnancyWeek,
            language = ctx.Language.ToString(),
            workingWomanMode = ctx.WorkingWomanMode,
            lastMood = ctx.LastMood
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/day-nurture/plan", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var plan = await response.Content.ReadFromJsonAsync<DailyPlan>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty daily plan.");

        _cache[cacheKey] = plan;
        return plan;
    }
}