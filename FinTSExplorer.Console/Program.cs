using FinTSExplorer.Core;
using libfintx.FinTS;
using libfintx.FinTS.Camt;
using libfintx.FinTS.Data;
using System.Text;

namespace FinTSExplorer
{
  internal static class Program
  {
    private static async Task Main()
    {
      Console.OutputEncoding = Encoding.UTF8;

      Console.WriteLine("FinTS/HBCI Explorer");
      Console.WriteLine("====================");
      Console.WriteLine();

      var baseDirectory = AppContext.BaseDirectory;

      var connectionDetails = FinTsConfig.Load(baseDirectory, Console.WriteLine);
      if (connectionDetails is null)
        return;

      var client = new FinTsClient(connectionDetails);

      var approvalNotifier = new ConsoleApprovalNotifier();
      var operations = new FinTsOperations(approvalNotifier, Console.WriteLine)
      {
        TypedTanPrompt = async prompt =>
        {
          Console.Write(prompt);
          return await Task.FromResult(Console.ReadLine() ?? string.Empty);
        },
      };

      var syncResult = await client.Synchronization();
      if (!syncResult.IsSuccess)
        PrintBankMessages(syncResult.Messages);

      var updater = new AccountUpdater(client, operations, baseDirectory, Console.WriteLine);

      var accounts = await updater.LoadAccountsAsync(connectionDetails);
      if (accounts is null)
        return;

      var displayNames = AccountDisplayNames.Load(baseDirectory);

      AccountHelper.ApplySelectedAccount(client, connectionDetails, await SelectAccountAsync(updater, accounts, connectionDetails, displayNames));

      var running = true;
      while (running)
      {
        Console.WriteLine();
        Console.WriteLine($"Aktives Konto: {AccountDisplayNames.GetDisplayName(displayNames, client.activeAccount)} – {client.activeAccount.AccountOwner} – {client.activeAccount.AccountNumber} ({client.activeAccount.AccountIban})");
        Console.WriteLine("Was möchtest du abrufen?");
        Console.WriteLine("  1) Konten anzeigen (HKSPA)");
        Console.WriteLine("  2) Kontostand (HKSAL)");
        Console.WriteLine("  3) Umsätze (HKKAZ)");
        Console.WriteLine("  4) Konto wechseln");
        Console.WriteLine("  5) Umsätze aktualisieren (nur Neues nachladen)");
        Console.WriteLine("  0) Beenden");
        Console.Write("> ");

        switch (Console.ReadLine()?.Trim())
        {
          case "1":
            foreach (var account in accounts)
              Console.WriteLine($"  {AccountDisplayNames.GetDisplayName(displayNames, account)} – {account.AccountOwner} – {account.AccountNumber} ({account.AccountIban})");
            break;

          case "2":
            await ShowBalanceAsync(client, operations);
            break;

          case "3":
            await ShowTransactionsAsync(client, operations, baseDirectory);
            break;

          case "4":
            AccountHelper.ApplySelectedAccount(client, connectionDetails, await SelectAccountAsync(updater, accounts, connectionDetails, displayNames));
            break;

          case "5":
            await updater.UpdateTransactionsAsync(client.activeAccount, connectionDetails);
            break;

          case "0":
            running = false;
            break;

          default:
            Console.WriteLine("Unbekannte Eingabe.");
            break;
        }
      }
    }

    private static async Task<AccountInformation> SelectAccountAsync(AccountUpdater updater, List<AccountInformation> accounts, ConnectionDetails connectionDetails, Dictionary<string, string> displayNames)
    {
      while (true)
      {
        Console.WriteLine();
        Console.WriteLine("Welches Konto?");
        for (var i = 0; i < accounts.Count; i++)
          Console.WriteLine($"  {i + 1}) {AccountDisplayNames.GetDisplayName(displayNames, accounts[i])} – {accounts[i].AccountOwner} – {accounts[i].AccountNumber} ({accounts[i].AccountIban})");
        Console.WriteLine("  a) Alle Konten aktualisieren (Umsätze)");
        Console.Write("> ");

        var input = Console.ReadLine()?.Trim();

        if (string.Equals(input, "a", StringComparison.OrdinalIgnoreCase))
        {
          await updater.UpdateAllAsync(accounts, connectionDetails);
          continue;
        }

        if (int.TryParse(input, out var choice) && choice >= 1 && choice <= accounts.Count)
          return accounts[choice - 1];

        Console.WriteLine("Ungültige Auswahl.");
      }
    }

    private static async Task ShowBalanceAsync(FinTsClient client, FinTsOperations operations)
    {
      var result = await operations.WaitForResultAsync(client.Balance(new TANDialog(operations.WaitForTanAsync)));
      if (result is null)
        return;

      PrintBankMessages(result.Messages);
      if (FinTsOperations.HasError(result.Messages))
        return;

      Console.WriteLine($"Saldo: {result.Data.Balance} EUR");
    }

    private static async Task ShowTransactionsAsync(FinTsClient client, FinTsOperations operations, string baseDirectory)
    {
      Console.Write("Umsätze der letzten wie vielen Tage? [10, * = so weit wie möglich]: ");
      var daysInput = Console.ReadLine()?.Trim();

      DateTime? startDate;
      if (daysInput == "*")
        startDate = null;
      else
        startDate = DateTime.Today.AddDays(-(int.TryParse(daysInput, out var parsedDays) ? parsedDays : 10));

      var result = await operations.WaitForResultAsync(client.Transactions_camt(new TANDialog(operations.WaitForTanAsync), CamtVersion.Camt052, startDate));
      if (result is null)
        return;

      PrintBankMessages(result.Messages);
      if (FinTsOperations.HasError(result.Messages))
        return;

      foreach (var statement in result.Data)
      {
        Console.WriteLine($"Auszug {statement.StartDate:d} - {statement.EndDate:d}, Saldo {statement.StartBalance} -> {statement.EndBalance} {statement.Currency}");
        foreach (var transaction in statement.Transactions)
          Console.WriteLine($"  {transaction.ValueDate:d} | {transaction.Amount,10} {statement.Currency} | {transaction.PartnerName} | {transaction.Description}");
      }

      var path = TransactionStore.GetFilePath(baseDirectory, client.activeAccount.AccountIban);
      var added = TransactionStore.Save(path, result.Data);
      Console.WriteLine($"Umsätze gespeichert: {added} neu hinzugekommen: {path}");
    }

    private static void PrintBankMessages(IEnumerable<HBCIBankMessage> messages)
    {
      foreach (var message in messages)
        Console.WriteLine($"  [{message.Type}] {message.Code}: {message.Message}");
    }
  }
}
