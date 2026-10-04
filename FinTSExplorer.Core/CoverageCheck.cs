using libfintx.FinTS.Camt;

namespace FinTSExplorer.Core;

public sealed record CoverageItem(string Label, int Day, decimal Amount, bool PastDue);

// Shortfall = groesster Fehlbetrag (positiv), den man vorher aufs Konto legen muesste; 0 = reicht.
// FirstShortfallItem = der Posten, ab dem das Geld erstmals nicht mehr reicht.
public sealed record CoverageResult(
    decimal Balance,
    List<CoverageItem> Items,
    decimal Total,
    decimal EndBalance,
    decimal Shortfall,
    CoverageItem? FirstShortfallItem);

// Reicht der aktuelle Kontostand fuer die Fixkosten, die im laufenden Monat noch abgebucht werden?
// Das Gehalt wird bewusst nicht gegengerechnet (vorsichtige Sicht).
public static class CoverageCheck
{
    public static CoverageResult Calculate(
        decimal balance,
        decimal overdraftLimit,
        List<CamtTransaction> accountTransactions,
        List<FixedCostOverride> overrides,
        List<string> excludes,
        List<string> confirmed,
        DateTime today)
    {
        var month = new DateTime(today.Year, today.Month, 1);
        var entries = FixedCostAnalyzer.Analyze(accountTransactions, overrides, excludes, confirmed, today, month);

        var items = entries
            .Where(e => !e.PossiblyEnded && IsDueIn(e, month))
            .Select(e => new CoverageItem(e.Label, e.ExpectedDay, e.ExpectedAmount, PastDue: e.ExpectedDay < today.Day))
            .OrderBy(i => i.Day)
            .ToList();

        var running = balance;
        CoverageItem? firstShortfall = null;
        var lowest = balance;

        foreach (var item in items)
        {
            running += item.Amount;
            lowest = Math.Min(lowest, running);

            if (firstShortfall is null && running < -overdraftLimit)
                firstShortfall = item;
        }

        var shortfall = Math.Max(0m, -overdraftLimit - lowest);
        return new CoverageResult(balance, items, items.Sum(i => i.Amount), running, shortfall, firstShortfall);
    }

    // Faellig, wenn die letzte Buchung ein Vielfaches des Zyklus vor diesem Monat liegt. Liegt sie schon in diesem
    // Monat, ist der Posten abgebucht und steckt im Kontostand.
    private static bool IsDueIn(FixedCostForecastEntry entry, DateTime month)
    {
        var monthsSinceLast = (month.Year * 12 + month.Month) - (entry.LastBooking.Year * 12 + entry.LastBooking.Month);
        return monthsSinceLast > 0 && entry.CycleMonths > 0 && monthsSinceLast % entry.CycleMonths == 0;
    }
}
