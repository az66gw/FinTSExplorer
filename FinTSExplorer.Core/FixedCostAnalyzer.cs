using libfintx.FinTS.Camt;

namespace FinTSExplorer.Core;

public sealed record FixedCostForecastEntry(string Label, int ExpectedDay, decimal ExpectedAmount, string Basis, string? Description, bool Confirmed, DateTime LastBooking, bool PossiblyEnded, int CycleMonths);

public static class FixedCostAnalyzer
{
    // Automatisch erkannte Partner brauchen mindestens so viele unterschiedliche Monate,
    // um als "wiederkehrend" zu gelten - Override-Eintraege (siehe FixedCostOverrides) sind davon ausgenommen.
    private const int MinDistinctMonths = 3;

    // forecastMonth = erster Tag des Monats, fuer den der erwartete Buchungstag geschaetzt wird.
    public static List<FixedCostForecastEntry> Analyze(List<CamtTransaction> transactions, List<FixedCostOverride> overrides, List<string> excludes, List<string> confirmed, DateTime today, DateTime forecastMonth)
    {
        var entries = new List<FixedCostForecastEntry>();
        entries.AddRange(AnalyzeAutomatic(transactions, excludes, confirmed, today, forecastMonth));
        entries.AddRange(AnalyzeOverrides(transactions, overrides, today, forecastMonth));

        return entries.OrderBy(e => e.ExpectedDay).ToList();
    }

    // Ein Posten gilt als vermutlich beendet, wenn die letzte Buchung laenger als zwei volle Zyklen zurueckliegt.
    // Er bleibt trotzdem in der Liste (nur gekennzeichnet): ein Ausbleiben soll auffallen, nicht verschwinden.
    private static bool IsPossiblyEnded(DateTime lastBooking, int cycleMonths, DateTime today) =>
        (today.Year * 12 + today.Month) - (lastBooking.Year * 12 + lastBooking.Month) > 2 * cycleMonths;

    // Fuer den Praefix-Merge unten: kuerzere Schluessel erst ab dieser Laenge als moegliche abgeschnittene
    // Variante eines laengeren akzeptieren, um zufaellige Treffer bei kurzen Namen zu vermeiden.
    private const int MinPrefixMatchLength = 20;

    private static List<FixedCostForecastEntry> AnalyzeAutomatic(List<CamtTransaction> transactions, List<string> excludes, List<string> confirmed, DateTime today, DateTime forecastMonth)
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
            var (expectedDay, dayText) = EstimateDay(groupTransactions, forecastMonth);

            results.Add(new FixedCostForecastEntry(
                newest.PartnerName!.Trim(),
                expectedDay,
                newest.Amount,
                $"{distinctMonths} Monate, {RhythmText(typicalGap)}, {dayText}",
                newest.Description,
                confirmed.Any(c => (newest.PartnerName?.Contains(c, StringComparison.OrdinalIgnoreCase) ?? false)
                                   || (newest.Description?.Contains(c, StringComparison.OrdinalIgnoreCase) ?? false)),
                newest.ValueDate,
                IsPossiblyEnded(newest.ValueDate, typicalGap, today),
                typicalGap));
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

    private static IEnumerable<FixedCostForecastEntry> AnalyzeOverrides(List<CamtTransaction> transactions, List<FixedCostOverride> overrides, DateTime today, DateTime forecastMonth)
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
                Math.Min(last.ValueDate.Day, DateTime.DaysInMonth(forecastMonth.Year, forecastMonth.Month)),
                last.Amount,
                $"Override, alle {over.IntervalMonths} Monat(e), nächste erwartet am {nextExpected:d}",
                last.Description,
                Confirmed: true, // Wer einen Override anlegt, hat sich damit bereits als Fixkosten festgelegt.
                last.ValueDate,
                IsPossiblyEnded(last.ValueDate, over.IntervalMonths, today),
                over.IntervalMonths);
        }
    }

    // Nur die juengsten Buchungen zaehlen: Verschiebt ein Einzieher seinen Tag (z.B. von 1. auf 4.-7.), soll die
    // alte Phase die Schaetzung nicht dauerhaft verzerren.
    private const int RecentBookingsForDay = 12;

    // Schaetzt den Buchungstag im Prognosemonat. Zwei Sichten werden verglichen:
    //  - Kalendertag ("immer am 15.", bei Wochenende verschiebt die Bank),
    //  - n-ter Bankarbeitstag des Monats ("immer am 1. Bankarbeitstag", das Datum schwankt je nach Wochenende).
    // Genommen wird die Sicht mit der kleineren Streuung; bei Gleichstand der Kalendertag.
    private static (int Day, string Text) EstimateDay(List<CamtTransaction> bookings, DateTime forecastMonth)
    {
        var recent = bookings.OrderByDescending(t => t.ValueDate).Take(RecentBookingsForDay).ToList();
        var calendarDays = recent.Select(t => t.ValueDate.Day).ToList();
        var bankDays = recent.Select(t => BankCalendar.BankDayIndex(t.ValueDate)).ToList();

        var calendarSpread = Spread(calendarDays);
        var bankSpread = Spread(bankDays);

        if (bankSpread < calendarSpread)
        {
            var index = Median(bankDays);
            var date = BankCalendar.DateOfBankDay(forecastMonth.Year, forecastMonth.Month, index);
            return (date.Day, $"meist {index}. Bankarbeitstag{ScatterNote(bankSpread)}");
        }

        var day = Math.Min(Median(calendarDays), DateTime.DaysInMonth(forecastMonth.Year, forecastMonth.Month));
        return (day, $"meist am {day}.{ScatterNote(calendarSpread)}");
    }

    private static string ScatterNote(int spread) => spread >= 2 ? " (streut)" : "";

    private static int Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    // Robuste Streuung: Median der Abweichungen vom Median (Ausreisser wie ein einzelner 16. fallen kaum ins Gewicht).
    private static int Spread(List<int> values)
    {
        var median = Median(values);
        return Median(values.Select(v => Math.Abs(v - median)).ToList());
    }
}
