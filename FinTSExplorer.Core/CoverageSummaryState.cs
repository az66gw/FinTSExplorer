using System.Text.Json;

namespace FinTSExplorer.Core;

// Merkt sich, wann die kompakte Deckungs-Uebersicht das naechste Mal faellig ist, und in welchem Abstand sie danach
// folgt. Die Uhrzeit steckt in NextSend selbst (z.B. Samstag 05:00); nach dem Versand wird nur das Datum um Interval
// weitergeschoben, die Uhrzeit bleibt.
public static class CoverageSummaryState
{
    private const string FileName = "CoverageSummaryState.json";
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromDays(7);

    // NextSend = null (Datei fehlt bzw. noch kein Termin) -> der Aufrufer behandelt das als sofort faellig.
    // Interval im JSON als TimeSpan-Text, z.B. "7.00:00:00" fuer 7 Tage.
    public sealed record State(DateTime? NextSend, TimeSpan Interval);

    public static State Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, FileName);
        if (!File.Exists(path))
            return new State(null, DefaultInterval);

        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State(null, DefaultInterval);
        return state.Interval > TimeSpan.Zero ? state : state with { Interval = DefaultInterval };
    }

    public static void Save(string baseDirectory, State state)
    {
        var path = Path.Combine(baseDirectory, FileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(state, options));
    }
}
