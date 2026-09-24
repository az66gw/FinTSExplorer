using System.IO.Pipes;
using FinTSExplorer.Core;
using Microsoft.Extensions.Logging;

namespace FinTSExplorer.Service;

// Schickt "SHOW"/"HIDE" an die Notifier-Tray-App in der Desktop-Sitzung des Nutzers,
// weil ein echter Windows-Dienst (Session 0) selbst keine sichtbaren Fenster erzeugen kann.
// Best-effort: klappt die Verbindung nicht (z.B. niemand angemeldet, Notifier läuft nicht),
// wird das nur geloggt - die eigentliche FinTS-Freigabe-Abfrage läuft unabhängig davon weiter.
public sealed class PipeApprovalNotifier : IApprovalNotifier, IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private NamedPipeServerStream? _pipe;
    private StreamWriter? _writer;
    private bool _isShown;

    public PipeApprovalNotifier(ILogger logger)
    {
        _logger = logger;
    }

    public void NotifyApprovalRequired() => _ = SendAsync(ApprovalPipe.ShowCommand, requiresPriorShow: false);

    public void NotifyApprovalResolved() => _ = SendAsync(ApprovalPipe.HideCommand, requiresPriorShow: true);

    private async Task SendAsync(string command, bool requiresPriorShow)
    {
        // Zugriff auf Pipe/Writer serialisieren: ohne das könnte ein HIDE-Aufruf die Pipe entsorgen,
        // auf deren Verbindung ein zeitgleicher SHOW-Aufruf gerade noch wartet.
        await _lock.WaitAsync();
        try
        {
            // Jede WaitForResultAsync-Anfrage löst am Ende ein "Resolved" aus, auch wenn nie eine
            // Freigabe nötig war (z.B. reine Kontenabfrage) - dafür braucht es keine Pipe-Verbindung.
            if (requiresPriorShow && !_isShown)
                return;

            await EnsureConnectedAsync();

            if (_writer is not null)
                await _writer.WriteLineAsync(command);

            _isShown = !requiresPriorShow;
        }
        catch (IOException)
        {
            _logger.LogInformation("Notifier-Tray-App nicht erreichbar ({Command}) - vermutlich niemand angemeldet oder Notifier läuft nicht.", command);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Notifier-Tray-App hat sich nicht rechtzeitig verbunden ({Command}).", command);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notifier-Tray-App konnte nicht benachrichtigt werden ({Command})", command);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (_pipe is { IsConnected: true })
            return;

        _pipe?.Dispose();
        _pipe = new NamedPipeServerStream(ApprovalPipe.Name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _pipe.WaitForConnectionAsync(timeout.Token);

        _writer = new StreamWriter(_pipe) { AutoFlush = true };
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
            await _writer.DisposeAsync();

        if (_pipe is not null)
            await _pipe.DisposeAsync();

        _lock.Dispose();
    }
}
