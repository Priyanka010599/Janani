// Services/BookRecommendationService.cs
// "Recommended reading" backed by a real BigQuery table (janani_insights.
// recommended_reading) — a curated set of real, well-known published books
// across pregnancy, infant care, and elder/dementia caregiving, each with
// its actual title/author and a representative public rating. Ranked by
// real rating data rather than an LLM guessing titles, and queried via
// BigQuery rather than embedded in code so it can grow into a live-synced
// table (e.g. from a books API) without changing how the app reads it.
// Degrades to an empty list if BigQuery is unavailable — recommendations
// are a nice-to-have, never something that should break a page.

using Google.Cloud.BigQuery.V2;

namespace Janani.Services;

public record BookRecommendation(string Title, string Authors, double AverageRating, int RatingsCount, string? Description, string? PublishedDate);

public class BookRecommendationService
{
    private readonly BigQueryClient? _client;
    private readonly string _projectId;
    private readonly ILogger<BookRecommendationService> _logger;

    public BookRecommendationService(ILogger<BookRecommendationService> logger)
    {
        _logger = logger;
        _projectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "janani-505411";

        try
        {
            _client = BigQueryClient.Create(_projectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("BigQuery client unavailable ({Message}) — recommended reading will be empty.", ex.Message);
            _client = null;
        }
    }

    public async Task<List<BookRecommendation>> GetRecommendationsAsync(string topic, int limit = 3, CancellationToken ct = default)
    {
        if (_client == null) return [];

        try
        {
            var sql = """
                SELECT title, authors, average_rating, ratings_count, description, published_date
                FROM `janani_insights.recommended_reading`
                WHERE topic = @topic
                ORDER BY average_rating DESC, ratings_count DESC
                LIMIT @limit
                """;
            var parameters = new[]
            {
                new BigQueryParameter("topic", BigQueryDbType.String, topic),
                new BigQueryParameter("limit", BigQueryDbType.Int64, limit),
            };
            var result = await _client.ExecuteQueryAsync(sql, parameters, cancellationToken: ct);

            return result.Select(row => new BookRecommendation(
                Title: (string)row["title"],
                Authors: (string)row["authors"],
                AverageRating: (double)row["average_rating"],
                RatingsCount: (int)(long)row["ratings_count"],
                Description: row["description"] as string,
                PublishedDate: row["published_date"] as string
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recommended reading query failed for topic {Topic}", topic);
            return [];
        }
    }
}
