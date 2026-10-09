using FinTSExplorer.Core;
using libfintx.FinTS;
using System.Globalization;
using System.Text;

namespace FinTSExplorer.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseDirectory = AppContext.BaseDirectory;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Aktualisieren der Kontoumsätze");
            }

            // Wird jeden Zyklus neu geladen, damit Änderungen am Zeitplan ohne Dienst-Neustart greifen.
            var schedule = ScheduleConfig.Load(baseDirectory, message => _logger.LogInformation("{Message}", message));
            var nextRun = ScheduleConfig.GetNextRun(schedule, DateTime.Now);

            _logger.LogInformation("Nächster Lauf um {NextRun}", nextRun);

            // In kurzen Schritten warten statt am Stück, damit zwischendurch geprueft werden kann, ob die
            // Fixkosten-Mail faellig ist. Laeuft im selben Strang wie das Umsatz-Update, also ohne parallelen
            // Zugriff auf die Umsaetze-Dateien.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SendFixedCostsReportIfDueAsync(baseDirectory, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler bei der Fixkosten-Prognose");
                }

                var remaining = nextRun - DateTime.Now;
                if (remaining <= TimeSpan.Zero)
                    break;

                await Task.Delay(remaining < CheckInterval ? remaining : CheckInterval, stoppingToken);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var baseDirectory = AppContext.BaseDirectory;

        var connectionDetails = FinTsConfig.Load(baseDirectory, message => _logger.LogInformation("{Message}", message));
        if (connectionDetails is null)
        {
            _logger.LogWarning("Keine gültige Konfiguration - überspringe diesen Lauf.");
            return;
        }

        var client = new FinTsClient(connectionDetails);

        await using var approvalNotifier = new PipeApprovalNotifier(_logger);
        var operations = new FinTsOperations(approvalNotifier, message => _logger.LogInformation("{Message}", message));
        // TypedTanPrompt bleibt ungesetzt: im unbeaufsichtigten Dienstbetrieb gibt es niemanden,
        // der eine getippte TAN eingeben könnte - nur Freigabe über anderen Kanal (Push) wird unterstützt.

        var syncResult = await client.Synchronization();
        if (!syncResult.IsSuccess)
            foreach (var message in syncResult.Messages)
                _logger.LogInformation("[Sync] [{Type}] {Code}: {Message}", message.Type, message.Code, message.Message);

        var updater = new AccountUpdater(client, operations, baseDirectory, message => _logger.LogInformation("{Message}", message));

        var accounts = await updater.LoadAccountsAsync(connectionDetails);
        if (accounts is null)
        {
            _logger.LogWarning("Keine Konten geladen - überspringe diesen Lauf.");
            return;
        }

        var displayNames = AccountDisplayNames.Load(baseDirectory);
        var updateResults = await updater.UpdateAllAsync(accounts, connectionDetails);

        await SendUpdateMailAsync(baseDirectory, displayNames, updateResults, stoppingToken);
    }

    private async Task SendUpdateMailAsync(string baseDirectory, Dictionary<string, string> displayNames, List<AccountUpdateResult> updateResults, CancellationToken stoppingToken)
    {
        var mailContext = MailConfig.Load(baseDirectory, message => _logger.LogInformation("{Message}", message));
        if (mailContext is null)
            return;

        var changed = updateResults.Where(r => r.NewTransactions.Count > 0).ToList();
        if (changed.Count == 0)
            return;

        var settings = ServiceSettings.Load(baseDirectory);
        var recipientAddress = settings.RecipientAddress;
        if (string.IsNullOrWhiteSpace(recipientAddress))
        {
            _logger.LogWarning("Keine RecipientAddress in ServiceSettings.json - Update-Mail wird übersprungen.");
            return;
        }

        var totalCount = changed.Sum(c => c.NewTransactions.Count);
        var subject = $"FinTSExplorer: {totalCount} neue{(totalCount == 1 ? "r" : "")} Umsatz{(totalCount == 1 ? "" : "ätze")}";
        var body = BuildMailBody(displayNames, changed);

        var coverage = TryCalculateCoverage(baseDirectory, settings, changed);
        if (coverage is not null)
        {
            if (coverage.Shortfall > 0)
            {
                subject = "ACHTUNG Deckung Gehaltskonto - " + subject;
                body = BuildCoverageWarning(coverage) + Environment.NewLine + Environment.NewLine + body;
            }

            body += Environment.NewLine + BuildCoverageSection(coverage);
        }

        var sender = new GmxMailSender(_logger);
        await sender.SendAsync(mailContext, recipientAddress, subject, body, stoppingToken);

        // Zusaetzlich die knappe Fassung nur mit der Deckung und der 3-Monats-Planung, direkt hinterher.
        if (coverage is not null)
        {
            var (summarySubject, summaryBody) = BuildCoverageSummaryMail(coverage);
            await sender.SendAsync(mailContext, recipientAddress, summarySubject, summaryBody, stoppingToken);
        }
    }

    // Deckungspruefung nur, wenn auf dem Gehaltskonto neue Buchungen dazugekommen sind. Ein Fehler hier darf
    // die eigentliche Update-Mail nie verhindern.
    private CoverageResult? TryCalculateCoverage(string baseDirectory, ServiceSettings settings, List<AccountUpdateResult> changed)
    {
        if (string.IsNullOrWhiteSpace(settings.SalaryAccountIban))
            return null;

        try
        {
            var salaryIban = settings.SalaryAccountIban.Replace(" ", "");
            var salaryResult = changed.FirstOrDefault(r =>
                string.Equals(r.Account.AccountIban?.Replace(" ", ""), salaryIban, StringComparison.OrdinalIgnoreCase));

            if (salaryResult?.Balance is null)
                return null;

            var transactions = TransactionStore.LoadExisting(TransactionStore.GetFilePath(baseDirectory, salaryResult.Account.AccountIban));
            var overrides = FixedCostOverrides.Load(baseDirectory);
            var excludes = FixedCostExcludes.Load(baseDirectory);
            var confirmed = FixedCostConfirmed.Load(baseDirectory);

            return CoverageCheck.Calculate(salaryResult.Balance.Value, settings.OverdraftLimit, transactions, overrides, excludes, confirmed, DateTime.Now);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deckungspruefung fuer das Gehaltskonto fehlgeschlagen - wird in der Mail ausgelassen.");
            return null;
        }
    }

    private static string BuildCoverageWarning(CoverageResult coverage)
    {
        var first = coverage.FirstShortfallItem!;
        var shortfall = coverage.Shortfall.ToString("0.00", CultureInfo.InvariantCulture);
        return $"ACHTUNG: Ab Tag {first.Day} ({first.Label}) reicht das Geld auf dem Gehaltskonto voraussichtlich nicht mehr."
            + Environment.NewLine
            + $"Es fehlen bis zu {shortfall} EUR - bitte vorher umbuchen.";
    }

    private static (string Subject, string Body) BuildCoverageSummaryMail(CoverageResult coverage)
    {
        static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        static string Cost(decimal value) => Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);

        var german = new CultureInfo("de-DE");
        var monthName = coverage.Month.ToString("MMMM", german);
        var open = -coverage.Total;
        var paid = -coverage.Paid;

        var subject = $"FinTSExplorer: Fixkosten {monthName}, Deckung Gehaltskonto";
        var body = new StringBuilder();

        if (coverage.Shortfall > 0)
        {
            subject = "ACHTUNG " + subject;
            body.AppendLine(BuildCoverageWarning(coverage));
            body.AppendLine();
        }

        body.AppendLine($"Fixkosten {monthName} (Gehaltskonto):  {Cost(paid + open),10} EUR");
        body.AppendLine($"  davon schon abgebucht:            {Cost(paid),10} EUR");
        body.AppendLine($"  noch offen:                       {Cost(open),10} EUR");
        body.AppendLine();
        body.AppendLine($"Kontostand heute:                   {Amount(coverage.Balance),10} EUR");
        body.AppendLine($"Voraussichtlich am Monatsende:      {Amount(coverage.EndBalance),10} EUR   -> {(coverage.Shortfall > 0 ? "ZU WENIG" : "OK")}");
        body.AppendLine();
        body.AppendLine("Planung der naechsten Monate (Fixkosten Gehaltskonto):");

        foreach (var month in coverage.Outlook)
        {
            var extras = month.NonMonthlyItems.Count == 0
                ? ""
                : "   (inkl. nicht monatlich: " + string.Join(", ", month.NonMonthlyItems.Select(i => $"{i.Label} {Cost(i.Amount)}")) + ")";

            body.AppendLine($"  {month.Month.ToString("MMMM yyyy", german),-15} {Cost(month.Total),10} EUR{extras}");
        }

        body.AppendLine();
        body.AppendLine("Nicht enthalten: Gehalt, Bargeld, Kartenzahlungen und die Mastercard-Abrechnung.");
        return (subject, body.ToString());
    }

    private static string BuildCoverageSection(CoverageResult coverage)
    {
        static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        var text = new StringBuilder();
        text.AppendLine("--- Deckung Gehaltskonto bis Monatsende ---");
        text.AppendLine($"Kontostand heute: {Money(coverage.Balance),10} EUR");

        if (coverage.Items.Count == 0)
        {
            text.AppendLine("Keine Fixkosten mehr fällig in diesem Monat.");
        }
        else
        {
            text.AppendLine("Noch ausstehend:");
            foreach (var item in coverage.Items)
            {
                var note = item.PastDue ? "   (Tag schon vorbei, noch nicht gebucht)" : "";
                text.AppendLine($"  Tag {item.Day,2}  {item.Label,-45} {Money(item.Amount),9}{note}");
            }

            text.AppendLine($"Summe ausstehend: {Money(coverage.Total),10} EUR");
            text.AppendLine($"Voraussichtlich am Monatsende: {Money(coverage.EndBalance),10} EUR   -> {(coverage.Shortfall > 0 ? "ZU WENIG" : "OK")}");
        }

        text.AppendLine("Nicht enthalten: Gehalt, Bargeld, Kartenzahlungen und die Mastercard-Abrechnung.");
        return text.ToString();
    }

    private static string BuildMailBody(Dictionary<string, string> displayNames, List<AccountUpdateResult> changed)
    {
        var body = new StringBuilder();

        foreach (var result in changed)
        {
            body.AppendLine($"{AccountDisplayNames.GetDisplayName(displayNames, result.Account)} ({result.Account.AccountIban}):");

            if (result.Balance is not null)
                body.AppendLine($"  Kontostand: {result.Balance.Value.ToString("0.00", CultureInfo.InvariantCulture)} EUR");

            foreach (var transaction in result.NewTransactions.OrderBy(t => t.ValueDate))
            {
                var valueDate = transaction.ValueDate.ToString("d", CultureInfo.InvariantCulture);
                var amount = transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture);
                body.AppendLine($"  {valueDate}  {amount,10} EUR  {transaction.PartnerName}");
                body.AppendLine($"    {transaction.Description}");
            }

            body.AppendLine();
        }

        return body.ToString();
    }

    // Wie oft zwischen zwei Umsatz-Updates geprueft wird, ob die Fixkosten-Mail faellig ist.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    // Nach einem fehlgeschlagenen Versand nicht jede Minute neu versuchen (sonst SMTP-Login-Flut).
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(30);

    // Laeuft unabhaengig vom regulaeren Umsatz-Update - sagt die Fixkosten des Folgemonats grob voraus
    // (Betrag + ungefaehrer Tag), auf Basis der gespeicherten Historie. Der naechste Termin (NextSend) und der
    // Abstand zwischen zwei Mails (Interval) stehen in FixedCostsReportState.json; fehlt NextSend, ist die Mail
    // sofort faellig. Empfaenger und Versand-Uhrzeit (SendTime) stehen in ServiceSettings.json.
    private async Task SendFixedCostsReportIfDueAsync(string baseDirectory, CancellationToken stoppingToken)
    {
        var now = DateTime.Now;
        var state = FixedCostReportState.Load(baseDirectory);
        if (state.NextSend is not null && now < state.NextSend)
            return;

        var mailContext = MailConfig.Load(baseDirectory, message => _logger.LogInformation("{Message}", message));
        if (mailContext is null)
        {
            FixedCostReportState.Save(baseDirectory, state with { NextSend = now + RetryAfterFailure });
            return;
        }

        var settings = ServiceSettings.Load(baseDirectory);
        var recipientAddress = settings.RecipientAddress;
        if (string.IsNullOrWhiteSpace(recipientAddress))
        {
            _logger.LogWarning("Keine RecipientAddress in ServiceSettings.json - Fixkosten-Mail wird übersprungen.");
            FixedCostReportState.Save(baseDirectory, state with { NextSend = now + RetryAfterFailure });
            return;
        }

        var transactions = TransactionStore.LoadAll(baseDirectory);
        var overrides = FixedCostOverrides.Load(baseDirectory);
        var excludes = FixedCostExcludes.Load(baseDirectory);
        var confirmed = FixedCostConfirmed.Load(baseDirectory);
        var forecast = FixedCostAnalyzer.Analyze(transactions, overrides, excludes, confirmed, now, new DateTime(now.Year, now.Month, 1).AddMonths(1));

        var subject = $"FinTSExplorer: Fixkosten-Prognose {now.AddMonths(1):MMMM yyyy}";
        var body = BuildFixedCostsMailBody(forecast);

        var sender = new GmxMailSender(_logger);
        var sent = await sender.SendAsync(mailContext, recipientAddress, subject, body, stoppingToken);

        var next = sent ? now.Date + state.Interval + settings.SendTime.ToTimeSpan() : now + RetryAfterFailure;
        FixedCostReportState.Save(baseDirectory, state with { NextSend = next });
        _logger.LogInformation("Nächste Fixkosten-Mail: {NextSend}", next);
    }

    private static string BuildFixedCostsMailBody(List<FixedCostForecastEntry> forecast)
    {
        // Oben, was noch Aufmerksamkeit braucht (nicht bestaetigt oder evtl. beendet), unten die klaren Faelle.
        var toCheck = forecast.Where(e => !e.Confirmed || e.PossiblyEnded).OrderBy(e => e.ExpectedDay).ToList();
        var clear = forecast.Where(e => e.Confirmed && !e.PossiblyEnded).OrderBy(e => e.ExpectedDay).ToList();

        var body = new StringBuilder();
        body.AppendLine("Voraussichtliche Fixkosten fuer den kommenden Monat (je Gruppe nach Tag im Monat sortiert).");
        body.AppendLine("* = von dir als Fixkosten bestaetigt (FixedCostsConfirmed.json bzw. Override)");
        body.AppendLine("! = letzte Buchung liegt mehr als zwei Zyklen zurueck, Posten evtl. beendet");

        AppendGroup(body, "ZU PRUEFEN (nicht bestaetigt oder evtl. beendet)", toCheck);
        AppendGroup(body, "BESTAETIGT", clear);

        return body.ToString();
    }

    private static void AppendGroup(StringBuilder body, string title, List<FixedCostForecastEntry> entries)
    {
        if (entries.Count == 0)
            return;

        body.AppendLine();
        body.AppendLine($"==== {title} ====");
        body.AppendLine();

        foreach (var entry in entries)
        {
            var amount = entry.ExpectedAmount.ToString("0.00", CultureInfo.InvariantCulture);
            var mark = entry.Confirmed ? "*" : " ";
            body.AppendLine($"Tag {entry.ExpectedDay,2}: {mark} {entry.Label,-55} {amount,10} EUR");
            body.AppendLine($"          ({entry.Basis})");

            if (entry.PossiblyEnded)
                body.AppendLine($"          ! evtl. beendet - letzte Buchung am {entry.LastBooking.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

            if (!string.IsNullOrWhiteSpace(entry.Description))
                body.AppendLine($"          {entry.Description}");
        }
    }
}
