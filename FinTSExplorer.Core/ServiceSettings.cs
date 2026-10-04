using System.Text.Encodings.Web;
using System.Text.Json;

namespace FinTSExplorer.Core;

// Allgemeine Einstellungen des Dienstes (gitignorte Datei neben der exe, wird bei Fehlen leer angelegt).
public sealed class ServiceSettings
{
    private const string FileName = "ServiceSettings.json";

    // Empfaenger aller Mails des Dienstes (Umsatz-Update und Fixkosten). Bewusst ohne Vorgabe im Quellcode.
    public string? RecipientAddress { get; set; }

    // IBAN des Gehaltskontos fuer die Deckungspruefung in der Update-Mail. Leer = keine Pruefung.
    // Bewusst ohne Vorgabe im Quellcode.
    public string? SalaryAccountIban { get; set; }

    // Wie weit das Gehaltskonto ins Minus darf (positiver Betrag, 0 = kein Dispo).
    public decimal OverdraftLimit { get; set; }

    // Uhrzeit, zu der die Fixkosten-Mail nach Ablauf des Intervalls verschickt wird (JSON: "08:30:00").
    public TimeOnly SendTime { get; set; } = new(8, 30);

    public static ServiceSettings Load(string baseDirectory)
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
            var defaults = new ServiceSettings();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }

        return JsonSerializer.Deserialize<ServiceSettings>(File.ReadAllText(path), options) ?? new ServiceSettings();
    }
}
