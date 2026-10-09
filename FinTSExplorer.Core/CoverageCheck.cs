using libfintx.FinTS.Camt;

namespace FinTSExplorer.Core;

public sealed record CoverageItem(string Label, int Day, decimal Amount, bool PastDue);

// Fixkosten eines kommenden Monats. NonMonthlyItems = Posten, die nicht jeden Monat anfallen (Quartal, Halbjahr,
// Jahr) und deshalb erklaeren, warum ein Monat teurer ist als der andere.
public sealed record CoverageOutlookMonth(DateTime Month, decimal Total, List<CoverageItem> NonMonthlyItems);

// Shortfall = groesster Fehlbetrag (positiv), den man vorher aufs Konto legen muesste; 0 = reicht.
// FirstShortfallItem = der Posten, ab dem das Geld erstmals nicht mehr reicht.
// Paid = in diesem Monat schon abgebuchte Fixkosten (steckt im Kontostand), Total = noch ausstehende.
public sealed record CoverageResult(
    DateTime Month,
    decimal Balance,
    List<CoverageItem> Items,
    decimal Paid,
    decimal Total,
    decimal EndBalance,
    decimal Shortfall,
    CoverageItem? FirstShortfallItem,
    List<CoverageOutlookMonth> Outlook);

// Reicht der aktuelle Kontostand fuer die Fixkosten, die im laufenden Monat noch abgebucht werden?
// Das Gehalt wird bewusst nicht gegengerechnet (vorsichtige Sicht).
public static class CoverageCheck
{
    private const int OutlookMonths = 3;

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
        var entries = FixedCostAnalyzer.Analyze(accountTransactions, overrides, excludes, confirmed, today, month)
            .Where(e => !e.PossiblyEnded)
            .ToList();

        var items = entries
            .Where(e => IsDueIn(e, month))
            .Select(e => new CoverageItem(e.Label, e.ExpectedDay, e.ExpectedAmount, PastDue: e.ExpectedDay < today.Day))
            .OrderBy(i => i.Day)
            .ToList();

        var paid = entries.Where(e => MonthsSinceLastBooking(e, month) == 0).Sum(e => e.ExpectedAmount);

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

        var outlook = Enumerable.Range(1, OutlookMonths)
            .Select(offset => BuildOutlookMonth(entries, month.AddMonths(offset)))
            .ToList();

        var shortfall = Math.Max(0m, -overdraftLimit - lowest);
        return new CoverageResult(month, balance, items, paid, items.Sum(i => i.Amount), running, shortfall, firstShortfall, outlook);
    }

    private static CoverageOutlookMonth BuildOutlookMonth(List<FixedCostForecastEntry> entries, DateTime month)
    {
        var due = entries.Where(e => IsDueIn(e, month)).ToList();
        var nonMonthly = due
            .Where(e => e.CycleMonths > 1)
            .Select(e => new CoverageItem(e.Label, e.ExpectedDay, e.ExpectedAmount, PastDue: false))
            .ToList();

        return new CoverageOutlookMonth(month, due.Sum(e => e.ExpectedAmount), nonMonthly);
    }

    private static int MonthsSinceLastBooking(FixedCostForecastEntry entry, DateTime month) =>
        (month.Year * 12 + month.Month) - (entry.LastBooking.Year * 12 + entry.LastBooking.Month);

    // Faellig, wenn die letzte Buchung ein Vielfaches des Zyklus vor diesem Monat liegt. Liegt sie schon in diesem
    // Monat, ist der Posten abgebucht und steckt im Kontostand.
    private static bool IsDueIn(FixedCostForecastEntry entry, DateTime month)
    {
        var monthsSinceLast = MonthsSinceLastBooking(entry, month);
        return monthsSinceLast > 0 && entry.CycleMonths > 0 && monthsSinceLast % entry.CycleMonths == 0;
    }
}
