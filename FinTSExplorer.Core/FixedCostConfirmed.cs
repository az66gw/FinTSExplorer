using System.Text.Encodings.Web;
using System.Text.Json;

namespace FinTSExplorer.Core;

// Partner, bei denen du dich verbindlich festgelegt hast: das sind Fixkosten (z.B. die Miete). Nur eine
// Markierung in der Fixkosten-Mail (Stern) - ob ein Partner ueberhaupt in der Liste auftaucht, haengt weiter
// an der automatischen Erkennung bzw. an FixedCostOverrides. Teilstring-Vergleich (case-insensitive) gegen
// PartnerName ODER Description der juengsten Buchung. Bewusst ohne Vorgabe im Quellcode.
public static class FixedCostConfirmed
{
    private const string FileName = "FixedCostsConfirmed.json";

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
