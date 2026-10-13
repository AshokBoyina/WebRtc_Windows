using System.Windows.Forms;

namespace IntraTeams.WinForms;

/// <summary>
/// Non-modal "X is calling" prompt.
///
/// Non-modal on purpose: a MessageBox would block the wrapper's message loop
/// and could not be closed when the caller hangs up while it is ringing.
/// The wrapper closes it with <see cref="Dismiss"/>, which does NOT raise
/// <see cref="Declined"/>.
/// </summary>
public partial class IncomingCallForm : Form
{
    private bool _answered;

    /// <summary>Designer only.</summary>
    public IncomingCallForm()
        : this("Someone", withVideo: false)
    {
    }

    public IncomingCallForm(string callerDisplayName, bool withVideo)
    {
        InitializeComponent();

        lblCaller.Text = callerDisplayName;
        lblKind.Text = withVideo ? "is calling you with video" : "is calling you";
        Text = $"Incoming call - {callerDisplayName}";
    }

    public event EventHandler? Accepted;

    public event EventHandler? Declined;

    /// <summary>Closes the prompt without raising any event (caller cancelled, call superseded).</summary>
    public void Dismiss()
    {
        _answered = true;
        Close();
    }

    private void btnAccept_Click(object? sender, EventArgs e)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        Accepted?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void btnDecline_Click(object? sender, EventArgs e)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        Declined?.Invoke(this, EventArgs.Empty);
        Close();
    }

    /// <summary>
    /// Appear without taking focus, so someone typing in the chat box when a
    /// call arrives cannot answer or decline it with a stray Enter / Escape.
    /// </summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>Closing with the title-bar X counts as declining.</summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_answered)
        {
            _answered = true;
            Declined?.Invoke(this, EventArgs.Empty);
        }

        base.OnFormClosed(e);
    }
}
