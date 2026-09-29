using libfintx.FinTS.Camt;

namespace FinTSExplorer.Core;

public sealed record FixedCostForecastEntry(string Label, int ExpectedDay, decimal AverageAmount, string Basis);

public static class FixedCostAnalyzer
{
    // Automatisch erkannte Partner brauchen mindestens so viele unterschiedliche Monate,
    // um als "wiederkehrend" zu gelten - Override-Eintraege (siehe FixedCostOverrides) sind davon ausgenommen.
    private const int MinDistinctMonths = 3;

    public static List<FixedCostForecastEntry> Analyze(List<CamtTransaction> transactions, List<FixedCostOverride> overrides)
    {
        var entries = new List<FixedCostForecastEntry>();
        entries.AddRange(AnalyzeAutomatic(transactions));
        entries.AddRange(AnalyzeOverrides(transactions, overrides));

        return entries.OrderBy(e => e.ExpectedDay).ToList();
    }

    private static IEnumerable<FixedCostForecastEntry> AnalyzeAutomatic(List<CamtTransaction> transactions)
    {
        var outgoing = transactions.Where(t => t.Amount < 0 && !string.IsNullOrWhiteSpace(t.PartnerName));

        foreach (var group in outgoing.GroupBy(t => t.PartnerName!.Trim()))
        {
            var distinctMonths = group.Select(t => new DateTime(t.ValueDate.Year, t.ValueDate.Month, 1)).Distinct().Count();
            if (distinctMonths < MinDistinctMonths)
                continue;

            yield return new FixedCostForecastEntry(
                group.Key,
                MedianDay(group.Select(t => t.ValueDate.Day)),
                Math.Round(group.Average(t => t.Amount), 2),
                $"{distinctMonths} Monate");
        }
    }

    private static IEnumerable<FixedCostForecastEntry> AnalyzeOverrides(List<CamtTransaction> transactions, List<FixedCostOverride> overrides)
    {
        foreach (var over in overrides)
        {
            var matches = transactions
                .Where(t => t.Amount < 0)
                .Where(t => (t.PartnerName?.Contains(over.Match, StringComparison.OrdinalIgnoreCase) ?? false)
                            || (t.Description?.Contains(over.Match, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            if (matches.Count == 0)
                continue;

            var last = matches.MaxBy(t => t.ValueDate)!;
            var nextExpected = last.ValueDate.AddMonths(over.IntervalMonths);

            yield return new FixedCostForecastEntry(
                over.Label,
                nextExpected.Day,
                Math.Round(matches.Average(t => t.Amount), 2),
                $"Override, alle {over.IntervalMonths} Monat(e), nächste erwartet am {nextExpected:d}");
        }
    }

    private static int MedianDay(IEnumerable<int> days)
    {
        var sorted = days.OrderBy(d => d).ToList();
        return sorted[sorted.Count / 2];
    }
}
