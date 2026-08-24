// Services/MedicineLookupService.cs
// New agent, no prior C# equivalent — see agents/medicine_agent/agent.py.
// Grounded in real web results via the agent's Google Search tool rather
// than the model's own memory, since drug names/uses aren't something to
// let an LLM guess at from parametric knowledge alone.

using System.Net.Http.Json;
using System.Text.Json;
using Janani.Data;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public class MedicineLookupService(HttpClient agentServiceClient, AppDbContext db, CurrentUserService currentUser) : IBloomAgent
{
    public string AgentName => "MedicineLookup";
    public string AgentRole => "Explains what a medicine is for and why it benefits the user, grounded in web search";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record MedicineExplanation(string Explanation);

    public async Task<string> LookupAsync(string medicineName, string? dosage, CancellationToken ct = default)
    {
        var userId = await currentUser.GetUserIdAsync();
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var language = profile?.Language ?? AppLanguage.English;

        var request = new
        {
            medicineName,
            dosage,
            language = language.ToString()
        };

        using var response = await agentServiceClient.PostAsJsonAsync("/agents/medicine/lookup", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MedicineExplanation>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty explanation.");
        return result.Explanation;
    }
}
