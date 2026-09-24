namespace FinTSExplorer.Core;

public interface IApprovalNotifier
{
    void NotifyApprovalRequired();

    void NotifyApprovalResolved();
}
