using System.Text.Json;

namespace FinTSExplorer.Core;

// Merkt sich, wann die Fixkosten-Mail das naechste Mal faellig ist.
public static class FixedCostReportState
{
    private const string FileName = "FixedCostsReportState.json";

    private sealed record State(DateTime? NextSend);

    // null = Datei fehlt bzw. noch kein Termin gespeichert -> der Aufrufer behandelt das als sofort faellig.
    public static DateTime? LoadNextSend(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, FileName);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<State>(File.ReadAllText(path))?.NextSend;
    }

    public static void SaveNextSend(string baseDirectory, DateTime nextSend)
    {
        var path = Path.Combine(baseDirectory, FileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(new State(nextSend), options));
    }
}
