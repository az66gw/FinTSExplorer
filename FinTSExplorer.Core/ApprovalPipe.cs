namespace FinTSExplorer.Core;

// Named-Pipe-Protokoll zwischen Dienst (Server) und Notifier-Tray-App (Client).
// Der Notifier kennt diese Werte nicht über eine Projektreferenz (bewusst kein Core-Verweis
// für die kleine UI-App), sondern hält sie als eigene Konstanten synchron - siehe dortige Kopie.
public static class ApprovalPipe
{
    public const string Name = "FinTSExplorerApproval";
    public const string ShowCommand = "SHOW";
    public const string HideCommand = "HIDE";
}
