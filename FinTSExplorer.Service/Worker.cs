using FinTSExplorer.Core;
using libfintx.FinTS;
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
                await RunOnceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Aktualisieren der Kontoumsätze");
            }

            // Wird jeden Zyklus neu geladen, damit Änderungen am Zeitplan ohne Dienst-Neustart greifen.
            var schedule = ScheduleConfig.Load(baseDirectory, message => _logger.LogInformation("{Message}", message));
            var nextRun = ScheduleConfig.GetNextRun(schedule, DateTime.Now);

            _logger.LogInformation("Nächster Lauf um {NextRun}", nextRun);
            await Task.Delay(nextRun - DateTime.Now, stoppingToken);
        }
    }

    private async Task RunOnceAsync()
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

        await SendUpdateMailAsync(baseDirectory, displayNames, updateResults);
    }

    private async Task SendUpdateMailAsync(string baseDirectory, Dictionary<string, string> displayNames, List<AccountUpdateResult> updateResults)
    {
        var mailContext = MailConfig.Load(baseDirectory, message => _logger.LogInformation("{Message}", message));
        if (mailContext is null)
            return;

        var changed = updateResults.Where(r => r.NewTransactions.Count > 0).ToList();
        if (changed.Count == 0)
            return;

        var totalCount = changed.Sum(c => c.NewTransactions.Count);
        var subject = $"FinTSExplorer: {totalCount} neue{(totalCount == 1 ? "r" : "")} Umsatz{(totalCount == 1 ? "" : "ätze")}";
        var body = BuildMailBody(displayNames, changed);

        var sender = new GmxMailSender(_logger);
        await sender.SendAsync(mailContext, subject, body);
    }

    private static string BuildMailBody(Dictionary<string, string> displayNames, List<AccountUpdateResult> changed)
    {
        var body = new StringBuilder();

        foreach (var result in changed)
        {
            body.AppendLine($"{AccountDisplayNames.GetDisplayName(displayNames, result.Account)} ({result.Account.AccountIban}):");

            foreach (var transaction in result.NewTransactions.OrderBy(t => t.ValueDate))
                body.AppendLine($"  {transaction.ValueDate:d}  {transaction.Amount,10:0.00} EUR  {transaction.PartnerName}  {transaction.Description}");

            body.AppendLine();
        }

        return body.ToString();
    }
}
