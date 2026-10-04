using System.Text.Encodings.Web;
using System.Text.Json;

namespace FinTSExplorer.Core;

// Partner, die trotz regelmaessigem Auftauchen bewusst keine Fixkosten sind (Supermaerkte, Privatpersonen,
// o.ae.) - Teilstring-Vergleich (case-insensitive) gegen PartnerName ODER Description, nur fuer die
// automatische Erkennung, nicht fuer FixedCostOverrides.
public static class FixedCostExcludes
{
    private const string FileName = "FixedCostsExcludes.json";

    public static List<string> Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, FileName);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        if (!File.Exists(path))
        {
            var defaults = new List<string>();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<string>>(json, options) ?? new List<string>();
    }
}
