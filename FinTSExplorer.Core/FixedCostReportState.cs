using System.Text.Json;

namespace FinTSExplorer.Core;

// Merkt sich, wann die Fixkosten-Mail das naechste Mal faellig ist, und in welchem Abstand sie danach folgt.
public static class FixedCostReportState
{
    private const string FileName = "FixedCostsReportState.json";
    private const int DefaultIntervalDays = 2;

    // NextSend = null (Datei fehlt bzw. noch kein Termin) -> der Aufrufer behandelt das als sofort faellig.
    public sealed record State(DateTime? NextSend, int IntervalDays = DefaultIntervalDays);

    public static State Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, FileName);
        if (!File.Exists(path))
            return new State(null);

        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State(null);
        return state.IntervalDays > 0 ? state : state with { IntervalDays = DefaultIntervalDays };
    }

    public static void Save(string baseDirectory, State state)
    {
        var path = Path.Combine(baseDirectory, FileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(state, options));
    }
}
