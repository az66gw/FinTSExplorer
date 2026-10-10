using libfintx.FinTS;
using libfintx.FinTS.Camt;
using libfintx.FinTS.Data;

namespace FinTSExplorer.Core;

public sealed record AccountUpdateResult(AccountInformation Account, List<CamtTransaction> NewTransactions, decimal? Balance);

public class AccountUpdater
{
    // Die Bank gibt Daten, die aelter als 90 Tage sind, nur nach starker Kundenauthentifizierung heraus (PSD2).
    // Etwas Abstand zur Grenze, damit Uhrzeit-/Zeitzonenunterschiede keine Rolle spielen. Gilt nur noch fuer den
    // Umstieg ohne Abruf-Zeitstempel.
    private const int MaxLookbackDays = 85;

    // So viele Tage vor dem letzten Abruf beginnt die naechste Abfrage (Buchungen koennen nachtraeglich auftauchen).
    private const int OverlapDays = 7;

    private readonly FinTsClient _client;
    private readonly FinTsOperations _operations;
    private readonly string _baseDirectory;
    private readonly Action<string> _log;

    public AccountUpdater(FinTsClient client, FinTsOperations operations, string baseDirectory, Action<string> log)
    {
        _client = client;
        _operations = operations;
        _baseDirectory = baseDirectory;
        _log = log;
    }

    public async Task<List<AccountInformation>?> LoadAccountsAsync(ConnectionDetails connectionDetails)
    {
        var accountsResult = await _operations.WaitForResultAsync(_client.Accounts(new TANDialog(_operations.WaitForTanAsync)));
        if (accountsResult is null)
            return null;

        LogMessages(accountsResult.Messages);

        if (FinTsOperations.HasError(accountsResult.Messages) || accountsResult.Data.Count == 0)
        {
            _log("Keine Konten gefunden.");
            return null;
        }

        foreach (var account in accountsResult.Data)
            AccountHelper.FillMissingAccountFields(account, connectionDetails);

        return accountsResult.Data;
    }

    public async Task<AccountUpdateResult?> UpdateTransactionsAsync(AccountInformation account, ConnectionDetails connectionDetails)
    {
        AccountHelper.ApplySelectedAccount(_client, connectionDetails, account);

        var path = TransactionStore.GetFilePath(_baseDirectory, account.AccountIban);
        var existing = TransactionStore.LoadExisting(path);
        var fetchStarted = DateTime.Now;
        var lastFetch = LastFetchStore.Get(_baseDirectory, account.AccountIban);
        DateTime? startDate;

        if (lastFetch is not null)
        {
            // Ab dem letzten erfolgreichen Abruf, mit etwas Ueberlapp fuer nachtraeglich eingetragene Buchungen.
            // War der Dienst lange aus, reicht das weit zurueck - die Freigabe der Bank ist dann berechtigt.
            startDate = lastFetch.Value.Date.AddDays(-OverlapDays);
            _log($"[{account.AccountIban}] Letzter Abruf am {lastFetch:g} – lade ab {startDate:d} neu.");
        }
        else if (existing.Count > 0)
        {
            // Noch kein Abruf-Zeitstempel (z.B. erster Lauf nach dem Umstieg): ab der letzten Buchung, aber hoechstens
            // MaxLookbackDays zurueck - die Bank verlangt fuer aeltere Daten bei jedem Lauf eine Freigabe.
            startDate = existing.Max(t => t.ValueDate);
            _log($"[{account.AccountIban}] Vorhandene Daten bis {startDate:d} – lade ab da neu.");

            var earliestStart = DateTime.Today.AddDays(-MaxLookbackDays);
            if (startDate < earliestStart)
            {
                startDate = earliestStart;
                _log($"[{account.AccountIban}] Startdatum auf {startDate:d} begrenzt (maximal {MaxLookbackDays} Tage zurück).");
            }
        }
        else
        {
            startDate = null;
            _log($"[{account.AccountIban}] Keine vorhandenen Daten – lade maximal möglichen Zeitraum.");
        }

        var result = await _operations.WaitForResultAsync(_client.Transactions_camt(new TANDialog(_operations.WaitForTanAsync), CamtVersion.Camt052, startDate));
        if (result is null)
            return null;

        LogMessages(result.Messages);

        if (FinTsOperations.HasError(result.Messages))
            return null;

        var added = TransactionStore.Save(path, result.Data);
        _log($"[{account.AccountIban}] {added.Count} neue Umsätze gespeichert.");

        // Erst nach erfolgreichem Speichern merken. Der Zeitpunkt vor der Abfrage zaehlt, damit nichts zwischen Abfrage
        // und Zeitstempel durchrutscht.
        LastFetchStore.Set(_baseDirectory, account.AccountIban, fetchStarted);

        var balance = await FetchBalanceAsync(account);
        return new AccountUpdateResult(account, added, balance);
    }

    private async Task<decimal?> FetchBalanceAsync(AccountInformation account)
    {
        var result = await _operations.WaitForResultAsync(_client.Balance(new TANDialog(_operations.WaitForTanAsync)));
        if (result is null)
            return null;

        LogMessages(result.Messages);

        if (FinTsOperations.HasError(result.Messages))
        {
            _log($"[{account.AccountIban}] Kontostand konnte nicht abgerufen werden.");
            return null;
        }

        return result.Data.Balance;
    }

    public async Task<List<AccountUpdateResult>> UpdateAllAsync(List<AccountInformation> accounts, ConnectionDetails connectionDetails)
    {
        var originalAccount = _client.activeAccount;
        var results = new List<AccountUpdateResult>();

        foreach (var account in accounts)
        {
            var result = await UpdateTransactionsAsync(account, connectionDetails);
            if (result is not null)
                results.Add(result);
        }

        AccountHelper.ApplySelectedAccount(_client, connectionDetails, originalAccount ?? accounts[0]);

        return results;
    }

    private void LogMessages(IEnumerable<HBCIBankMessage> messages)
    {
        foreach (var message in messages)
            _log($"  [{message.Type}] {message.Code}: {message.Message}");
    }
}
