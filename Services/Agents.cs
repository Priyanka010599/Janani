// Services/Agents.cs
// Specialized agents. All three now delegate to the Python ADK agent
// service (agents/) over HTTP instead of calling a ChatClient directly.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Models;

namespace Janani.Services;

// ── 3. Meal Agent — gentle meal suggestions by feeling ──────────────────────
// Ported to ADK: see agents/meal_agent/agent.py for the prompt and model
// call. This class is now a thin HTTP client to that service.
public class MealAgent(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "Meals";
    public string AgentRole => "Gentle pregnancy-safe meal suggestions";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MealSuggestion> SuggestMealsAsync(string feeling, AgentContext ctx, CancellationToken ct = default)
    {
        var request = new
        {
            feeling,
            userName = ctx.UserName,
            pregnancyWeek = ctx.PregnancyWeek,
            language = ctx.Language.ToString(),
            workingWomanMode = ctx.WorkingWomanMode,
            lastMood = ctx.LastMood
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/meal/suggest", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MealSuggestion>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty meal suggestion.");
    }
}

public record MealSuggestion(string Opening, string GentleNote, List<MealItem> Meals)
{
    public MealSuggestion() : this("", "", []) { }
}
public record MealItem(string Name, string Description, string WhyItHelps)
{
    public MealItem() : this("", "", "") { }
}

// ── 4. Partner Agent — weekly message for partner ───────────────────────────
// Ported to ADK: see agents/partner_agent/agent.py for the prompt and model
// call. This class is now a thin HTTP client to that service.
public class PartnerAgent(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "Partner";
    public string AgentRole => "Gentle partner communication";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record PartnerMessageResponse(string Message);

    public async Task<string> GeneratePartnerMessageAsync(
        string? additionalContext, AgentContext ctx, CancellationToken ct = default)
    {
        var request = new
        {
            additionalContext,
            userName = ctx.UserName,
            pregnancyWeek = ctx.PregnancyWeek,
            language = ctx.Language.ToString(),
            workingWomanMode = ctx.WorkingWomanMode,
            lastMood = ctx.LastMood
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/partner/message", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PartnerMessageResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty partner message.");
        return result.Message;
    }
}

// ── 5. Birth Plan Agent — multi-step guided birth planning ──────────────────
// The wizard state machine and static acknowledgments below are unchanged —
// only the final document generation is ported to ADK (agents/birth_plan_agent).
public class BirthPlanAgent(HttpClient agentServiceClient) : IBloomAgent
{
    public string AgentName => "BirthPlan";
    public string AgentRole => "Multi-step guided birth plan builder";

    private static readonly string[] Steps =
    [
        "Where would you like to give birth — hospital, birthing centre, or at home? What matters most to you about the environment?",
        "Who would you like with you during labour? This could be a partner, family member, doula, or just medical staff — whatever feels right for you.",
        "How do you feel about pain management? For example, epidural, gas and air, water birth, breathing techniques — or a combination.",
        "Are there any specific things you would like to avoid, or things that are especially important to you during labour?",
        "What are your wishes for immediately after the birth — skin-to-skin contact, delayed cord clamping, who cuts the cord, first feed?",
        "Is there anything else you want your care team to know about you — cultural wishes, fears, previous experiences, or anything at all?"
    ];

    private static readonly string[] AcknowledgmentTemplates =
    [
        "Thank you for sharing that — it really helps your care team understand you. 💗",
        "That's noted with care. 🌸",
        "Got it — thank you for taking the time to think that through.",
        "That means a lot to know, thank you. 💗",
        "Thank you for trusting me with that.",
        "Noted, gently and completely. 🌷",
    ];

    private readonly List<(string question, string answer)> _answers = [];
    private int _currentStep;
    private int _ackIndex;

    public string CurrentQuestion => _currentStep < Steps.Length ? Steps[_currentStep] : "";
    public bool IsComplete => _currentStep >= Steps.Length;
    public int TotalSteps => Steps.Length;
    public int CurrentStepNumber => _currentStep + 1;

    public async IAsyncEnumerable<string> ProcessAnswerAsync(
        string answer, AgentContext ctx,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_currentStep < Steps.Length)
        {
            _answers.Add((Steps[_currentStep], answer));
            _currentStep++;
        }

        if (!IsComplete)
        {
            // Warm, templated acknowledgment — no LLM call needed since the next
            // question is already known statically from Steps, and this was the
            // single biggest source of avoidable wait time in the wizard.
            var ack = AcknowledgmentTemplates[_ackIndex++ % AcknowledgmentTemplates.Length];
            yield return $"{ack}\n\n{CurrentQuestion}";
        }
        else
        {
            // All steps done — generate the full birth plan document via the
            // agent service (agents/birth_plan_agent), streamed the same way
            // CompanionService streams its replies.
            var allAnswers = string.Join("\n\n", _answers.Select(a => $"Q: {a.question}\nA: {a.answer}"));

            var request = new
            {
                answersText = allAnswers,
                userName = ctx.UserName,
                pregnancyWeek = ctx.PregnancyWeek,
                language = ctx.Language.ToString(),
                workingWomanMode = ctx.WorkingWomanMode,
                lastMood = ctx.LastMood
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/agents/birth-plan/generate")
            {
                Content = JsonContent.Create(request)
            };

            using var response = await agentServiceClient.SendAsync(
                httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var buffer = new char[256];
            int charsRead;
            while ((charsRead = await reader.ReadAsync(buffer, ct)) > 0)
            {
                yield return new string(buffer, 0, charsRead);
            }
        }
    }

    public void Reset()
    {
        _currentStep = 0;
        _answers.Clear();
        _ackIndex = 0;
    }
}
