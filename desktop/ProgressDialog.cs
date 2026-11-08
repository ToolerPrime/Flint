/* Runs a long job on a worker thread with a progress bar and Cancel, keeping
 * the window responsive. Modal on purpose: editing is blocked while the job
 * reads the document, which is what makes that read safe. */
namespace Flint;

public sealed class ProgressDialog : ThemedForm
{
    private readonly Label _msg = new() { Left = 14, Top = 14, Width = 372, Height = 20 };
    private readonly ProgressBar _bar = new() { Left = 14, Top = 40, Width = 372, Height = 18, Maximum = 1000 };
    private readonly Button _cancel = new() { Text = "Cancel", Left = 306, Top = 70, Width = 80 };
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<IProgress<(double, string)>, CancellationToken, string> _work;

    public string? Result { get; private set; }
    public Exception? Error { get; private set; }
    public bool Cancelled { get; private set; }

    private ProgressDialog(string title, Func<IProgress<(double, string)>, CancellationToken, string> work)
    {
        _work = work;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ControlBox = false;
        ClientSize = new Size(400, 104);
        Controls.Add(_msg);
        Controls.Add(_bar);
        Controls.Add(_cancel);
        _cancel.Click += (_, _) =>
        {
            _cts.Cancel();
            _cancel.Enabled = false;
            _cancel.Text = "Stopping…";
        };
        Shown += async (_, _) =>
        {
            // Progress<T> made here posts reports back to this UI thread.
            var progress = new Progress<(double frac, string msg)>(r =>
            {
                _bar.Value = (int)Math.Clamp(r.frac * 1000, 0, 1000);
                _msg.Text = r.msg;
            });
            try { Result = await Task.Run(() => _work(progress, _cts.Token)); }
            catch (OperationCanceledException) { Cancelled = true; }
            catch (Exception ex) { Error = ex; }
            Close();
        };
    }

    /// <summary>
    /// Show the dialog and run the job. Headless runs execute it directly,
    /// because a modal window nobody can see would hang the run.
    /// </summary>
    public static (bool ok, string message) Run(IWin32Window? owner, string title,
        Func<IProgress<(double, string)>, CancellationToken, string> work)
    {
        if (Program.Headless)
        {
            try { return (true, work(null!, CancellationToken.None)); }
            catch (Exception ex) { return (false, ex.Message); }
        }
        using var d = new ProgressDialog(title, work);
        d.ShowDialog(owner);
        if (d.Cancelled) return (false, "cancelled");
        if (d.Error != null) return (false, d.Error.Message);
        return (true, d.Result ?? "done");
    }
}
