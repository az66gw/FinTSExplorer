using System.Text.Encodings.Web;
using System.Text.Json;
using libfintx.FinTS;
using libfintx.FinTS.Data;

namespace FinTSExplorer.Core;

public static class AccountDisplayNames
{
    private const string FileName = "AccountNames.json";

    public static Dictionary<string, string> Load(string baseDirectory)
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
            var defaults = new Dictionary<string, string>
            {
                ["2115301"] = "SpardaGiro",
                ["1842087"] = "SpardaGiro",
                ["1602115301"] = "SpardaCash",
                ["3002115301"] = "SpardaMastercard",
                ["5002115301"] = "Geschäftsguthaben",
            };
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options) ?? new Dictionary<string, string>();
    }

    public static string GetDisplayName(Dictionary<string, string> displayNames, AccountInformation account) =>
        displayNames.TryGetValue(account.AccountNumber, out var name) ? name : account.AccountType;
}
