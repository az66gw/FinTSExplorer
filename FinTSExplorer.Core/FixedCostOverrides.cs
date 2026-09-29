using System.Text.Encodings.Web;
using System.Text.Json;

namespace FinTSExplorer.Core;

// Fuer Abos, die der automatischen Erkennung entgehen: PartnerName fehlt bei Auslandskartenzahlungen oft
// (Haendlername steht nur im freien Description-Text), oder die Zahlung ist zu selten (z.B. jaehrlich) fuer
// die Monats-Schwelle in FixedCostAnalyzer. Match wird als Teilstring in PartnerName ODER Description gesucht.
public sealed record FixedCostOverride(string Match, string Label, int IntervalMonths);

public static class FixedCostOverrides
{
    private const string FileName = "FixedCostsOverrides.json";

    public static List<FixedCostOverride> Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, FileName);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        if (!File.Exists(path))
        {
            var defaults = new List<FixedCostOverride>
            {
                new("WOLFRAMALPHA", "WolframAlpha Pro", 12),
                new("ANTHROPIC", "Anthropic Claude", 1),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<FixedCostOverride>>(json, options) ?? new List<FixedCostOverride>();
    }
}
