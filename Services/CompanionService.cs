// Services/CompanionService.cs
// The AI Companion: answers pregnancy questions grounded in the knowledge base,
// maintains conversation history across a session, and always defers to
// healthcare providers for medical decisions.
//
// Ported to ADK: see agents/companion_agent/agent.py for the prompt, RAG
// knowledge base, and model call. Conversation history now lives in a
// persistent session on the agent service (Cloud SQL Postgres in prod, see
// agents/common/session_store.py) instead of an in-memory List<ChatMessage>
// here — it survives circuit reconnects and works across devices, which the
// old per-circuit in-memory history couldn't.

using System.Net.Http.Json;
using Janani.Models;

namespace Janani.Services;

public class CompanionService(HttpClient agentServiceClient, CurrentUserService currentUser) : IBloomAgent
{
    public string AgentName => "Companion";
    public string AgentRole => "Conversational RAG pregnancy assistant (Janani)";

    public async IAsyncEnumerable<string> AskAsync(
        string question, AgentContext ctx,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();

        var request = new
        {
            userId,
            question,
            userName = ctx.UserName,
            pregnancyWeek = ctx.PregnancyWeek,
            language = ctx.Language.ToString(),
            workingWomanMode = ctx.WorkingWomanMode,
            lastMood = ctx.LastMood
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/agents/companion/chat")
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

    public async Task ClearHistoryAsync()
    {
        var userId = await currentUser.GetUserIdAsync();
        using var response = await agentServiceClient.DeleteAsync($"/agents/companion/session?userId={userId}");
        response.EnsureSuccessStatusCode();
    }
}
