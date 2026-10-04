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

        var recipientAddress = FixedCostReportState.Load(baseDirectory).RecipientAddress;
        if (string.IsNullOrWhiteSpace(recipientAddress))
        {
            _logger.LogWarning("Keine RecipientAddress in FixedCostsReportState.json - Update-Mail wird übersprungen.");
            return;
        }

        var totalCount = changed.Sum(c => c.NewTransactions.Count);
        var subject = $"FinTSExplorer: {totalCount} neue{(totalCount == 1 ? "r" : "")} Umsatz{(totalCount == 1 ? "" : "ätze")}";
        var body = BuildMailBody(displayNames, changed);

        var sender = new GmxMailSender(_logger);
        await sender.SendAsync(mailContext, recipientAddress, subject, body, stoppingToken);
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

    // Uhrzeit des Fixkosten-Versands. Der Abstand zwischen zwei Mails (Interval) steht in
    // FixedCostsReportState.json.
    private static readonly TimeOnly SendTime = new(8, 30);

    // Wie oft zwischen zwei Umsatz-Updates geprueft wird, ob die Fixkosten-Mail faellig ist.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    // Nach einem fehlgeschlagenen Versand nicht jede Minute neu versuchen (sonst SMTP-Login-Flut).
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(30);

    // Laeuft unabhaengig vom regulaeren Umsatz-Update - sagt die Fixkosten des Folgemonats grob voraus
    // (Betrag + ungefaehrer Tag), auf Basis der gespeicherten Historie. Der naechste Termin steht in
    // FixedCostsReportState.json (NextSend); fehlt er, ist die Mail sofort faellig.
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

        if (string.IsNullOrWhiteSpace(state.RecipientAddress))
        {
            _logger.LogWarning("Keine RecipientAddress in FixedCostsReportState.json - Fixkosten-Mail wird übersprungen.");
            FixedCostReportState.Save(baseDirectory, state with { NextSend = now + RetryAfterFailure });
            return;
        }

        var transactions = TransactionStore.LoadAll(baseDirectory);
        var overrides = FixedCostOverrides.Load(baseDirectory);
        var excludes = FixedCostExcludes.Load(baseDirectory);
        var forecast = FixedCostAnalyzer.Analyze(transactions, overrides, excludes);

        var subject = $"FinTSExplorer: Fixkosten-Prognose {now.AddMonths(1):MMMM yyyy}";
        var body = BuildFixedCostsMailBody(forecast);

        var sender = new GmxMailSender(_logger);
        var sent = await sender.SendAsync(mailContext, state.RecipientAddress, subject, body, stoppingToken);

        var next = sent ? now.Date + state.Interval + SendTime.ToTimeSpan() : now + RetryAfterFailure;
        FixedCostReportState.Save(baseDirectory, state with { NextSend = next });
        _logger.LogInformation("Nächste Fixkosten-Mail: {NextSend}", next);
    }

    private static string BuildFixedCostsMailBody(List<FixedCostForecastEntry> forecast)
    {
        var body = new StringBuilder();
        body.AppendLine("Voraussichtliche Fixkosten fuer den kommenden Monat (sortiert nach Tag im Monat):");
        body.AppendLine();

        foreach (var entry in forecast)
        {
            var amount = entry.ExpectedAmount.ToString("0.00", CultureInfo.InvariantCulture);
            body.AppendLine($"Tag {entry.ExpectedDay,2}:  {entry.Label,-55} {amount,10} EUR");
            body.AppendLine($"          ({entry.Basis})");

            if (!string.IsNullOrWhiteSpace(entry.Description))
                body.AppendLine($"          {entry.Description}");
        }

        return body.ToString();
    }
}
