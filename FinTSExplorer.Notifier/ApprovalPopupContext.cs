using System.IO.Pipes;

namespace FinTSExplorer.Notifier;

// Lauscht auf der Named Pipe des Dienstes und zeigt/versteckt das Freigabe-Popup entsprechend.
// Diese Werte müssen mit FinTSExplorer.Core.ApprovalPipe übereinstimmen (bewusst keine
// Projektreferenz auf Core für diese kleine, reine UI-App).
internal static class ApprovalPipeProtocol
{
    public const string Name = "FinTSExplorerApproval";
    public const string ShowCommand = "SHOW";
    public const string HideCommand = "HIDE";
}

internal sealed class ApprovalPopupContext : ApplicationContext
{
    private readonly Form _uiThreadMarshal;
    private Form? _popup;

    public ApprovalPopupContext()
    {
        _uiThreadMarshal = new Form
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            Opacity = 0,
            Size = new Size(1, 1),
        };
        _ = _uiThreadMarshal.Handle; // Handle erzwingen, damit BeginInvoke von einem Hintergrund-Task aus sicher funktioniert.

        _ = ListenLoopAsync();
    }

    private async Task ListenLoopAsync()
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", ApprovalPipeProtocol.Name, PipeDirection.In, PipeOptions.Asynchronous);
                await pipe.ConnectAsync();
                using var reader = new StreamReader(pipe);

                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    if (line == ApprovalPipeProtocol.ShowCommand)
                        RunOnUiThread(ShowPopup);
                    else if (line == ApprovalPipeProtocol.HideCommand)
                        RunOnUiThread(HidePopup);
                }
            }
            catch
            {
                // Dienst noch nicht gestartet oder Verbindung verloren - kurz warten und erneut versuchen.
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_uiThreadMarshal.InvokeRequired)
            _uiThreadMarshal.BeginInvoke(action);
        else
            action();
    }

    private void ShowPopup()
    {
        if (_popup is { IsDisposed: false })
            return;

        _popup = new Form
        {
            Text = "FinTS-Freigabe erforderlich",
            Width = 420,
            Height = 160,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };

        var label = new Label
        {
            Text = "Bitte jetzt in der Sparda-Bank-App (SecureGo+) freigeben.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11),
            Padding = new Padding(12),
        };
        _popup.Controls.Add(label);

        _popup.Show();
    }

    private void HidePopup()
    {
        _popup?.Close();
        _popup = null;
    }
}
