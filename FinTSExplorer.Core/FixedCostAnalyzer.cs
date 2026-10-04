using libfintx.FinTS.Camt;

namespace FinTSExplorer.Core;

public sealed record FixedCostForecastEntry(string Label, int ExpectedDay, decimal ExpectedAmount, string Basis, string? Description, bool Confirmed, DateTime LastBooking, bool PossiblyEnded);

public static class FixedCostAnalyzer
{
    // Automatisch erkannte Partner brauchen mindestens so viele unterschiedliche Monate,
    // um als "wiederkehrend" zu gelten - Override-Eintraege (siehe FixedCostOverrides) sind davon ausgenommen.
    private const int MinDistinctMonths = 3;

    public static List<FixedCostForecastEntry> Analyze(List<CamtTransaction> transactions, List<FixedCostOverride> overrides, List<string> excludes, List<string> confirmed, DateTime today)
    {
        var entries = new List<FixedCostForecastEntry>();
        entries.AddRange(AnalyzeAutomatic(transactions, excludes, confirmed, today));
        entries.AddRange(AnalyzeOverrides(transactions, overrides, today));

        return entries.OrderBy(e => e.ExpectedDay).ToList();
    }

    // Ein Posten gilt als vermutlich beendet, wenn die letzte Buchung laenger als zwei volle Zyklen zurueckliegt.
    // Er bleibt trotzdem in der Liste (nur gekennzeichnet): ein Ausbleiben soll auffallen, nicht verschwinden.
    private static bool IsPossiblyEnded(DateTime lastBooking, int cycleMonths, DateTime today) =>
        (today.Year * 12 + today.Month) - (lastBooking.Year * 12 + lastBooking.Month) > 2 * cycleMonths;

    // Fuer den Praefix-Merge unten: kuerzere Schluessel erst ab dieser Laenge als moegliche abgeschnittene
    // Variante eines laengeren akzeptieren, um zufaellige Treffer bei kurzen Namen zu vermeiden.
    private const int MinPrefixMatchLength = 20;

    private static List<FixedCostForecastEntry> AnalyzeAutomatic(List<CamtTransaction> transactions, List<string> excludes, List<string> confirmed, DateTime today)
    {
        var outgoing = transactions
            .Where(t => t.Amount < 0 && !string.IsNullOrWhiteSpace(t.PartnerName))
            .Where(t => !excludes.Any(exclude =>
                t.PartnerName!.Contains(exclude, StringComparison.OrdinalIgnoreCase)
                || (t.Description?.Contains(exclude, StringComparison.OrdinalIgnoreCase) ?? false)));

        // Manche Altbuchungen der Bank (vor einer SEPA-Formatumstellung, TypeCode 828/"Summenbeleg") haben durch
        // ein Legacy-DTA-Zeilenformat zufaellige Leerzeichen mitten im Namen, z.B. "congstar - eine Marke der T
        // elekom D eutschland GmbH" statt "... Telekom Deutschland GmbH". Ohne Leerzeichen vergleichen, damit
        // solche Varianten mit der sauberen Schreibweise zusammenfallen.
        var groups = outgoing
            .GroupBy(t => NormalizeKey(t.PartnerName!))
            .Select(g => (Key: g.Key, Transactions: g.ToList()))
            .OrderByDescending(g => g.Key.Length)
            .ToList();

        // Bei manchen dieser Altbuchungen ist der Name zusaetzlich auf eine feste Feldlaenge abgeschnitten,
        // wodurch die eingefuegten Leerzeichen echte Zeichen am Ende verdraengen (z.B. "...eingetrageneGe"
        // statt "...eingetrageneGeno") - reines Entfernen der Leerzeichen fuehrt solche verkuerzten Varianten
        // dann nicht zusammen. Deshalb zusaetzlich per Praefix der laengeren, saubereren Variante zuordnen.
        var merged = new List<(string Key, List<CamtTransaction> Transactions)>();
        foreach (var group in groups)
        {
            var target = merged.FirstOrDefault(m =>
                group.Key.Length >= MinPrefixMatchLength && m.Key.StartsWith(group.Key, StringComparison.Ordinal));

            if (target.Transactions is not null)
                target.Transactions.AddRange(group.Transactions);
            else
                merged.Add((group.Key, group.Transactions));
        }

        var results = new List<FixedCostForecastEntry>();
        foreach (var (_, groupTransactions) in merged)
        {
            var months = groupTransactions
                .Select(t => t.ValueDate.Year * 12 + t.ValueDate.Month)
                .Distinct()
                .OrderBy(m => m)
                .ToList();
            var distinctMonths = months.Count;
            if (distinctMonths < MinDistinctMonths)
                continue;

            // Neuere Buchungen haben eher die saubere Schreibweise (siehe Kommentar oben) - als Anzeigename nehmen.
            var newest = groupTransactions.OrderByDescending(t => t.ValueDate).First();
            var typicalGap = TypicalGap(months);

            results.Add(new FixedCostForecastEntry(
                newest.PartnerName!.Trim(),
                MedianDay(groupTransactions.Select(t => t.ValueDate.Day)),
                newest.Amount,
                $"{distinctMonths} Monate, {RhythmText(typicalGap)}",
                newest.Description,
                confirmed.Any(c => (newest.PartnerName?.Contains(c, StringComparison.OrdinalIgnoreCase) ?? false)
                                   || (newest.Description?.Contains(c, StringComparison.OrdinalIgnoreCase) ?? false)),
                newest.ValueDate,
                IsPossiblyEnded(newest.ValueDate, typicalGap, today)));
        }

        return results;
    }

    // Typischer Abstand zwischen zwei Monaten mit Buchung (Median), z.B. 3 = quartalsweise. Vorsicht: bei
    // sehr lueckenhaften Partnern (z.B. Supermaerkte) ist das nur eine grobe Naeherung.
    private static int TypicalGap(List<int> sortedMonths)
    {
        var gaps = sortedMonths.Zip(sortedMonths.Skip(1), (a, b) => b - a).OrderBy(g => g).ToList();
        return gaps[gaps.Count / 2];
    }

    private static string RhythmText(int typicalGap) => typicalGap switch
    {
        1 => "monatlich",
        3 => "quartalsweise",
        6 => "halbjährlich",
        12 => "jährlich",
        _ => $"alle {typicalGap} Monate",
    };

    private static string NormalizeKey(string partnerName) =>
        new(partnerName.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static IEnumerable<FixedCostForecastEntry> AnalyzeOverrides(List<CamtTransaction> transactions, List<FixedCostOverride> overrides, DateTime today)
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
                last.Amount,
                $"Override, alle {over.IntervalMonths} Monat(e), nächste erwartet am {nextExpected:d}",
                last.Description,
                Confirmed: true, // Wer einen Override anlegt, hat sich damit bereits als Fixkosten festgelegt.
                last.ValueDate,
                IsPossiblyEnded(last.ValueDate, over.IntervalMonths, today));
        }
    }

    private static int MedianDay(IEnumerable<int> days)
    {
        var sorted = days.OrderBy(d => d).ToList();
        return sorted[sorted.Count / 2];
    }
}
