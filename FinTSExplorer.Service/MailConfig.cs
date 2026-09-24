using System.Text.Json;

namespace FinTSExplorer.Service;

public static class MailConfig
{
    private const string FileName = "MailContext.json";

    public static MailContext? Load(string baseDirectory, Action<string> log)
    {
        var path = Path.Combine(baseDirectory, FileName);

        if (!File.Exists(path))
        {
            log($"Keine Mail-Konfiguration gefunden ({path}) - Update-Mail wird übersprungen.");
            return null;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var context = JsonSerializer.Deserialize<MailContext>(File.ReadAllText(path), options);

        if (context is null || string.IsNullOrWhiteSpace(context.SenderAddress) || string.IsNullOrWhiteSpace(context.SenderPassword))
        {
            log($"Mail-Konfiguration unvollständig ({path}) - Update-Mail wird übersprungen.");
            return null;
        }

        return context;
    }
}
