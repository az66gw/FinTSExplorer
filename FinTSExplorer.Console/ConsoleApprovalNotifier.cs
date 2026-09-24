using System.Drawing;
using System.Windows.Forms;
using FinTSExplorer.Core;

namespace FinTSExplorer
{
    public sealed class ConsoleApprovalNotifier : IApprovalNotifier
    {
        private CancellationTokenSource? _cts;

        public void NotifyApprovalRequired()
        {
            _cts = new CancellationTokenSource();
            ShowApprovalPopup(_cts.Token);
        }

        public void NotifyApprovalResolved()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private static void ShowApprovalPopup(CancellationToken closeToken)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new Form
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
                    form.Controls.Add(label);

                    _ = form.Handle; // Fenster-Handle sofort erzeugen, damit BeginInvoke unten nicht racen kann.

                    closeToken.Register(() =>
                    {
                        if (!form.IsDisposed)
                            form.BeginInvoke(new Action(form.Close));
                    });

                    Application.Run(form);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Freigabe-Popup konnte nicht angezeigt werden: {ex.Message}");
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
