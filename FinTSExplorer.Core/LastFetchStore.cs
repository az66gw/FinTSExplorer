using System.Text.Json;

namespace FinTSExplorer.Core;

// Merkt sich pro Konto, wann die Umsaetze zuletzt erfolgreich abgerufen wurden (LastFetch.json). Daraus ergibt sich
// der Startpunkt der naechsten Abfrage - unabhaengig davon, wann die letzte Buchung war.
public static class LastFetchStore
{
    private const string FileName = "LastFetch.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static DateTime? Get(string baseDirectory, string iban)
    {
        var all = Load(baseDirectory);
        return all.TryGetValue(iban, out var timestamp) ? timestamp : null;
    }

    public static void Set(string baseDirectory, string iban, DateTime timestamp)
    {
        var all = Load(baseDirectory);
        all[iban] = timestamp;
        File.WriteAllText(Path.Combine(baseDirectory, FileName), JsonSerializer.Serialize(all, Options));
    }

    private static Dictionary<string, DateTime> Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, FileName);
        if (!File.Exists(path))
            return new Dictionary<string, DateTime>();

        return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? new Dictionary<string, DateTime>();
    }
}
