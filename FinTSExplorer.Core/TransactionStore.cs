using System.Text.Encodings.Web;
using System.Text.Json;
using libfintx.FinTS.Camt;

namespace FinTSExplorer.Core;

public static class TransactionStore
{
    public static string GetFilePath(string baseDirectory, string iban)
    {
        var directory = Path.Combine(baseDirectory, "Umsaetze");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{iban}.json");
    }

    // Kontounabhaengig, liest direkt alle Umsaetze/*.json - braucht keine laufende FinTS-Verbindung/Kontenliste.
    public static List<CamtTransaction> LoadAll(string baseDirectory)
    {
        var directory = Path.Combine(baseDirectory, "Umsaetze");
        if (!Directory.Exists(directory))
            return new List<CamtTransaction>();

        return Directory.GetFiles(directory, "*.json")
            .SelectMany(path => LoadExisting(path))
            .ToList();
    }

    private static JsonSerializerOptions Options => new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<CamtTransaction> LoadExisting(string path)
    {
        if (!File.Exists(path))
            return new List<CamtTransaction>();

        return JsonSerializer.Deserialize<List<CamtTransaction>>(File.ReadAllText(path), Options) ?? new List<CamtTransaction>();
    }

    // Gibt die tatsächlich neu hinzugekommenen Umsätze zurück (nicht nur die Anzahl),
    // damit Aufrufer (z.B. eine Update-Mail) nur den geänderten Teil anzeigen können.
    public static List<CamtTransaction> Save(string path, List<CamtStatement> statements)
    {
        var existingTransactions = LoadExisting(path);
        var existingKeys = existingTransactions.Select(DedupKey).ToHashSet();
        var newTransactions = statements.SelectMany(s => s.Transactions);

        var merged = existingTransactions
            .Concat(newTransactions)
            .GroupBy(DedupKey)
            .Select(g => g.First())
            .OrderBy(t => t.ValueDate)
            .ToList();

        File.WriteAllText(path, JsonSerializer.Serialize(merged, Options));

        return merged.Where(t => !existingKeys.Contains(DedupKey(t))).ToList();
    }

    // Camt-Umsätze haben keine durchgängig zuverlässige eindeutige ID (z.B. Kartenzahlungen ohne EndToEndId) -
    // Dedup läuft daher über eine Kombination mehrerer Felder statt einer einzelnen ID.
    private static string DedupKey(CamtTransaction transaction) =>
        $"{transaction.ValueDate:yyyyMMdd}|{transaction.InputDate:yyyyMMdd}|{transaction.Amount}|{transaction.PartnerName}|{transaction.Description}|{transaction.TypeCode}|{transaction.EndToEndId}|{transaction.ProprietaryRef}";
}
