// Services/PhcStockChecker.cs
// Same split as every other checker in this app (VitalsThresholdChecker,
// GrowthThresholdChecker, SchemeMatcher): plain code decides whether a PHC's
// stock level is concerning. No model is ever asked to judge a stock count.

using Janani.Models;

namespace Janani.Services;

public enum PhcStockStatus { Ok, Warning, Critical }

public static class PhcStockChecker
{
    // Critical: at or below the PHC's own reorder threshold -- the threshold
    // IS the "reorder now" line, set per medicine/PHC by whoever logs it.
    // Warning: within 50% headroom above it, so a supervisor sees it coming
    // before it's actually critical.
    public static PhcStockStatus Status(PhcStockItem item)
    {
        if (item.StockCount <= item.ReorderThreshold) return PhcStockStatus.Critical;
        if (item.StockCount <= item.ReorderThreshold * 1.5m) return PhcStockStatus.Warning;
        return PhcStockStatus.Ok;
    }
}

// Deterministic cross-PHC redistribution suggestions: for a medicine that's
// Critical at one PHC, look for another PHC holding real surplus of the same
// medicine and suggest moving units across. Pure function of the stock
// table -- no model involved, and no attempt at real logistics routing
// (distance, transport time, cold-chain requirements) -- v1 scope is
// "which PHCs should talk to each other," not "dispatch a vehicle."
public static class PhcRedistributionMatcher
{
    public record Recommendation(
        string MedicineName, Phc FromPhc, Phc ToPhc, int SuggestedUnits, string Reason);

    public static List<Recommendation> Recommend(IReadOnlyList<Phc> phcs, IReadOnlyList<PhcStockItem> items)
    {
        var phcById = phcs.ToDictionary(p => p.Id);
        var recommendations = new List<Recommendation>();

        foreach (var group in items.GroupBy(i => i.MedicineName))
        {
            var critical = group.Where(i => PhcStockChecker.Status(i) == PhcStockStatus.Critical).ToList();
            // Surplus: comfortably above its own reorder line, with real
            // spare capacity to give away -- not just technically "Ok".
            var surplus = group.Where(i => i.StockCount > i.ReorderThreshold * 2).ToList();
            if (critical.Count == 0 || surplus.Count == 0) continue;

            foreach (var need in critical)
            {
                if (!phcById.TryGetValue(need.PhcId, out var needPhc)) continue;
                var donor = surplus
                    .Where(s => s.PhcId != need.PhcId)
                    .OrderByDescending(s => s.StockCount - s.ReorderThreshold)
                    .FirstOrDefault();
                if (donor == null || !phcById.TryGetValue(donor.PhcId, out var donorPhc)) continue;

                var donorSpare = donor.StockCount - donor.ReorderThreshold;
                var needShortfall = need.ReorderThreshold - need.StockCount + need.ReorderThreshold;
                var units = Math.Min(donorSpare, needShortfall);
                if (units <= 0) continue;

                recommendations.Add(new Recommendation(
                    need.MedicineName, donorPhc, needPhc, units,
                    $"{needPhc.Name} ({needPhc.District}) is at {need.StockCount}/{need.ReorderThreshold} " +
                    $"(its reorder line) — {donorPhc.Name} ({donorPhc.District}) holds {donor.StockCount}, " +
                    $"{donorSpare} units above its own reorder line."));
            }
        }

        return recommendations;
    }
}
