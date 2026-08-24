// Services/RecipeService.cs
// "Recipes" module backed by a real BigQuery table (janani_insights.
// newmom_recipes) — curated first-food, finger-food, postpartum-recovery,
// and lactation-support recipes, each carrying a source/attribution note.
// Same reasoning as BookRecommendationService: an LLM never invents a
// recipe for a newborn or a postpartum body, it only ever displays what a
// curated, queryable dataset already decided. Degrades to an empty list if
// BigQuery is unavailable — recipes are a nice-to-have, never something
// that should break a page.

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Google.Cloud.BigQuery.V2;

namespace Janani.Services;

public record Recipe(string Category, string Title, string Stage, string Ingredients, string Instructions, string? Source);

public class RecipeService
{
    private readonly HttpClient _agentServiceClient;
    private readonly BigQueryClient? _client;
    private readonly string _projectId;
    private readonly ILogger<RecipeService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record RecipeAnswer(string Answer);

    public static readonly (string Key, string Label)[] Categories =
    [
        ("first-foods", "First Foods"),
        ("finger-foods", "Finger Foods"),
        ("postpartum-recovery", "Postpartum Recovery"),
        ("lactation-support", "Lactation Support"),
    ];

    public RecipeService(HttpClient agentServiceClient, ILogger<RecipeService> logger)
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
            _logger.LogWarning("BigQuery client unavailable ({Message}) — recipes will be empty.", ex.Message);
            _client = null;
        }
    }

    // Only English/Hindi/Telugu have translated rows so far (see
    // newmom_recipes_i18n load) — any other profile language falls back to
    // English rather than showing an empty page. Extending to more
    // languages is just adding more rows, no schema/code change needed.
    private static readonly HashSet<string> TranslatedLanguages = new() { "English", "Hindi", "Telugu" };

    public async Task<List<Recipe>> GetRecipesAsync(string? category = null, string language = "English", CancellationToken ct = default)
    {
        if (_client == null) return [];

        var effectiveLanguage = TranslatedLanguages.Contains(language) ? language : "English";

        try
        {
            var sql = category == null
                ? """
                  SELECT category, title, stage, ingredients, instructions, source
                  FROM `janani_insights.newmom_recipes`
                  WHERE language = @language
                  ORDER BY category, title
                  """
                : """
                  SELECT category, title, stage, ingredients, instructions, source
                  FROM `janani_insights.newmom_recipes`
                  WHERE language = @language AND category = @category
                  ORDER BY title
                  """;
            var parameters = new List<BigQueryParameter> { new("language", BigQueryDbType.String, effectiveLanguage) };
            if (category != null) parameters.Add(new BigQueryParameter("category", BigQueryDbType.String, category));
            var result = await _client.ExecuteQueryAsync(sql, parameters, cancellationToken: ct);

            return result.Select(row => new Recipe(
                Category: (string)row["category"],
                Title: (string)row["title"],
                Stage: (string)row["stage"],
                Ingredients: (string)row["ingredients"],
                Instructions: (string)row["instructions"],
                Source: row["source"] as string
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recipe query failed for category {Category}", category);
            return [];
        }
    }

    // Conversational "ask for a recipe" — the same idea as Companion chat,
    // but the agent is only ever allowed to pick from the real catalog below,
    // never invent a recipe of its own. C# owns the data access (same
    // division of labor as CaregiverCoordinationService's care summary);
    // the agent only ever sees the already-assembled catalog text.
    public async Task<string> AskAsync(string question, string language, CancellationToken ct = default)
    {
        var allRecipes = await GetRecipesAsync(category: null, language, ct);
        var catalog = BuildCatalogText(allRecipes);

        var request = new { question, recipeCatalog = catalog, language };
        using var response = await _agentServiceClient.PostAsJsonAsync("/agents/recipe/ask", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RecipeAnswer>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Agent service returned an empty answer.");
        return result.Answer;
    }

    private static string BuildCatalogText(List<Recipe> recipes)
    {
        if (recipes.Count == 0) return "No recipes available.";

        var sb = new StringBuilder();
        foreach (var r in recipes)
        {
            sb.AppendLine($"- {r.Title} [{r.Category}, {r.Stage}]");
            sb.AppendLine($"  Ingredients: {r.Ingredients}");
            sb.AppendLine($"  Instructions: {r.Instructions}");
            if (!string.IsNullOrWhiteSpace(r.Source))
                sb.AppendLine($"  Source: {r.Source}");
        }
        return sb.ToString();
    }
}
