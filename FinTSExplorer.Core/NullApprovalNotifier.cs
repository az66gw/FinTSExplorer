namespace FinTSExplorer.Core;

public sealed class NullApprovalNotifier : IApprovalNotifier
{
    public void NotifyApprovalRequired()
    {
    }

    public void NotifyApprovalResolved()
    {
    }
}
