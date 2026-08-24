// Services/ElderTutorService.cs
// "Elder Mode" AI tutor — backed by a real BigQuery table (janani_insights.
// elder_guides) covering phone/app basics (video calling, sending photos,
// SOS, text size, what Janani/AI even is). Same reasoning as
// MedicineLookupService/RecipeService: an elderly, possibly-confused user
// asking "how do I video call my daughter" and getting WRONG steps back is
// actively harmful, not just an inaccuracy — so the agent is only ever
// allowed to pick from this real, curated catalog, never invent UI steps
// of its own.

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Google.Cloud.BigQuery.V2;

namespace Janani.Services;

public record ElderGuide(string Category, string Title, string Steps, string? Source);

public class ElderTutorService
{
    private readonly HttpClient _agentServiceClient;
    private readonly BigQueryClient? _client;
    private readonly string _projectId;
    private readonly ILogger<ElderTutorService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record TutorAnswer(string Answer);

    // Same fallback rule as RecipeService — only English/Hindi/Telugu have
    // translated rows so far; any other profile language falls back to
    // English rather than showing an empty catalog.
    private static readonly HashSet<string> TranslatedLanguages = new() { "English", "Hindi", "Telugu" };

    public ElderTutorService(HttpClient agentServiceClient, ILogger<ElderTutorService> logger)
    {
        _agentServiceClient = agentServiceClient;
        _logger = logger;
        _projectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "janani-505411";

        try
        {
            _client = BigQueryClient.Create(_projectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("BigQuery client unavailable ({Message}) — elder guides will be empty.", ex.Message);
            _client = null;
        }
    }

    public async Task<List<ElderGuide>> GetGuidesAsync(string language = "English", CancellationToken ct = default)
    {
        if (_client == null) return [];

        var effectiveLanguage = TranslatedLanguages.Contains(language) ? language : "English";

        try
        {
            var sql = """
                SELECT category, title, steps, source
                FROM `janani_insights.elder_guides`
                WHERE language = @language
                ORDER BY category, title
                """;
            var parameters = new[] { new BigQueryParameter("language", BigQueryDbType.String, effectiveLanguage) };
            var result = await _client.ExecuteQueryAsync(sql, parameters, cancellationToken: ct);

            return result.Select(row => new ElderGuide(
                Category: (string)row["category"],
                Title: (string)row["title"],
                Steps: (string)row["steps"],
                Source: row["source"] as string
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Elder guide query failed");
            return [];
        }
    }

    // Conversational "ask the tutor" — same shape as RecipeService.AskAsync:
    // C# owns data access, the agent only ever sees the already-assembled
    // catalog text and picks from it, never invents its own steps.
    public async Task<string> AskAsync(string question, string language, CancellationToken ct = default)
    {
        var allGuides = await GetGuidesAsync(language, ct);
        var catalog = BuildCatalogText(allGuides);

        var request = new { question, guideCatalog = catalog, language };
        using var response = await _agentServiceClient.PostAsJsonAsync("/agents/elder-tutor/ask", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TutorAnswer>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty answer.");
        return result.Answer;
    }

    private static string BuildCatalogText(List<ElderGuide> guides)
    {
        if (guides.Count == 0) return "No guides available.";

        var sb = new StringBuilder();
        foreach (var g in guides)
        {
            sb.AppendLine($"- {g.Title} [{g.Category}]");
            sb.AppendLine($"  Steps: {g.Steps}");
            if (!string.IsNullOrWhiteSpace(g.Source))
                sb.AppendLine($"  Source: {g.Source}");
        }
        return sb.ToString();
    }
}
