using System.Text.Json;

namespace FinTSExplorer.Core;

// Verhindert zu haeufigen Versand, wenn der Dienst mehrfach am Tag laeuft (siehe Schedule.json).
public static class FixedCostReportState
{
    private const string FileName = "FixedCostsReportState.json";

    private sealed record State(DateTime? LastSentAt);

    public static int DaysSinceLastSent(string baseDirectory, DateTime now)
    {
        var path = Path.Combine(baseDirectory, FileName);
        if (!File.Exists(path))
            return int.MaxValue;

        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
        if (state?.LastSentAt is null)
            return int.MaxValue;

        return (now.Date - state.LastSentAt.Value.Date).Days;
    }

    public static void MarkSent(string baseDirectory, DateTime sentAt)
    {
        var path = Path.Combine(baseDirectory, FileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(new State(sentAt), options));
    }
}
