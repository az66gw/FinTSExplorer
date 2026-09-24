using libfintx.FinTS;
using libfintx.FinTS.Data;

namespace FinTSExplorer.Core;

public static class AccountHelper
{
    public static void FillMissingAccountFields(AccountInformation account, ConnectionDetails connectionDetails)
    {
        if (string.IsNullOrWhiteSpace(account.AccountBic))
            account.AccountBic = connectionDetails.Bic;

        if (string.IsNullOrWhiteSpace(account.AccountBankCode))
            account.AccountBankCode = connectionDetails.Blz.ToString();
    }

    public static void ApplySelectedAccount(FinTsClient client, ConnectionDetails connectionDetails, AccountInformation account)
    {
        // HKSAL nutzt client.activeAccount, HKCAZ liest die Kontodaten dagegen direkt aus ConnectionDetails - beides muss synchron gehalten werden.
        client.activeAccount = account;
        connectionDetails.Account = account.AccountNumber;
        connectionDetails.Iban = account.AccountIban;
        connectionDetails.Bic = account.AccountBic;
        connectionDetails.SubAccount = account.SubAccountFeature;
    }
}
