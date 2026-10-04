using System.Text.Json;

namespace FinTSExplorer.Core;

// Merkt sich, wann die Fixkosten-Mail das naechste Mal faellig ist, und in welchem Abstand sie danach folgt.
public static class FixedCostReportState
{
    private const string FileName = "FixedCostsReportState.json";
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromDays(2);

    // NextSend = null (Datei fehlt bzw. noch kein Termin) -> der Aufrufer behandelt das als sofort faellig.
    // Interval im JSON als TimeSpan-Text, z.B. "2.00:00:00" fuer 2 Tage.
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
