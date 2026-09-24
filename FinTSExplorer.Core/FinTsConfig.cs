using System.Text.Json;
using libfintx.FinTS.Data;

namespace FinTSExplorer.Core;

public static class FinTsConfig
{
    public const int DefaultBankCode = 50090500;
    public const string DefaultUrl = "https://fints2.atruvia.de/cgi-bin/hbciservlet";
    private const string ConfigFileName = "FinTSConfig.json";

    public static ConnectionDetails? Load(string baseDirectory, Action<string> log)
    {
        var configPath = Path.Combine(baseDirectory, ConfigFileName);

        if (!File.Exists(configPath))
        {
            CreateTemplate(configPath);
            log($"Es wurde eine leere Konfigurationsdatei angelegt: {configPath}");
            log("Bitte Zugangsdaten eintragen und erneut starten.");
            return null;
        }

        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var connectionDetails = JsonSerializer.Deserialize<ConnectionDetails>(json, options);

        if (connectionDetails is null || string.IsNullOrWhiteSpace(connectionDetails.UserId) || string.IsNullOrWhiteSpace(connectionDetails.Pin))
        {
            log($"Konfigurationsdatei '{configPath}' ist unvollständig (UserId/Pin fehlen).");
            return null;
        }

        return connectionDetails;
    }

    private static void CreateTemplate(string configPath)
    {
        var template = new ConnectionDetails
        {
            Url = DefaultUrl,
            Blz = DefaultBankCode,
            UserId = string.Empty,
            Bic = string.Empty,
            Pin = string.Empty,
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(configPath, JsonSerializer.Serialize(template, options));
    }
}
