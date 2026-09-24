using libfintx.FinTS;

namespace FinTSExplorer.Core;

public class FinTsOperations
{
    private readonly IApprovalNotifier _approvalNotifier;
    private readonly Action<string> _log;
    private readonly TimeSpan _approvalTimeout;
    private readonly TimeSpan _heartbeatInterval;

    public FinTsOperations(IApprovalNotifier approvalNotifier, Action<string> log, TimeSpan? approvalTimeout = null, TimeSpan? heartbeatInterval = null)
    {
        _approvalNotifier = approvalNotifier;
        _log = log;
        _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(5);
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
    }

    // Wird nur im interaktiven Konsolen-Host gesetzt - im unbeaufsichtigten Dienst-Betrieb gibt es
    // niemanden, der eine getippte TAN eingeben könnte (nur Freigabe über anderen Kanal wird unterstützt).
    public Func<string, Task<string>>? TypedTanPrompt { get; set; }

    public async Task<string> WaitForTanAsync(TANDialog tanDialog)
    {
        foreach (var message in tanDialog.DialogResult.Messages)
            _log($"[Bank] {message}");

        if (tanDialog.DialogResult.IsApprovalRequired)
        {
            _log("Freigabe über anderen Kanal erforderlich – warte automatisch auf Bestätigung ...");
            _approvalNotifier.NotifyApprovalRequired();
            return string.Empty;
        }

        if (TypedTanPrompt is not null)
            return await TypedTanPrompt("TAN eingeben: ");

        _log("Es wird eine eingetippte TAN benötigt, aber es gibt keine Eingabemöglichkeit (unbeaufsichtigter Betrieb) - Abbruch.");
        return string.Empty;
    }

    public async Task<HBCIDialogResult<T>?> WaitForResultAsync<T>(Task<HBCIDialogResult<T>> operation)
    {
        using var cts = new CancellationTokenSource();
        var heartbeat = ReportHeartbeatAsync(cts.Token);
        var timeout = Task.Delay(_approvalTimeout);

        var finished = await Task.WhenAny(operation, timeout);
        cts.Cancel();

        _approvalNotifier.NotifyApprovalResolved();

        if (finished == timeout)
        {
            _log($"Zeitüberschreitung: keine Freigabe innerhalb von {_approvalTimeout.TotalMinutes:0} Minuten erkannt – Abbruch.");
            return null;
        }

        return await operation;
    }

    private async Task ReportHeartbeatAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_heartbeatInterval, token);
                _log("... warte weiter auf Freigabe ...");
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    public static bool HasError(IEnumerable<HBCIBankMessage> messages) => messages.Any(m => m.IsError);
}
