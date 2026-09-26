using libfintx.FinTS;
using libfintx.FinTS.Camt;
using libfintx.FinTS.Data;

namespace FinTSExplorer.Core;

public sealed record AccountUpdateResult(AccountInformation Account, List<CamtTransaction> NewTransactions, decimal? Balance);

public class AccountUpdater
{
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
        DateTime? startDate = existing.Count > 0 ? existing.Max(t => t.ValueDate) : null;

        _log(startDate is not null
            ? $"[{account.AccountIban}] Vorhandene Daten bis {startDate:d} – lade ab da neu."
            : $"[{account.AccountIban}] Keine vorhandenen Daten – lade maximal möglichen Zeitraum.");

        var result = await _operations.WaitForResultAsync(_client.Transactions_camt(new TANDialog(_operations.WaitForTanAsync), CamtVersion.Camt052, startDate));
        if (result is null)
            return null;

        LogMessages(result.Messages);

        if (FinTsOperations.HasError(result.Messages))
            return null;

        var added = TransactionStore.Save(path, result.Data);
        _log($"[{account.AccountIban}] {added.Count} neue Umsätze gespeichert.");

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
