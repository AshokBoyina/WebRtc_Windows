namespace IntraTeams.Client.ViewModels;

/// <summary>One rendered chat bubble.</summary>
public sealed class ChatEntryViewModel
{
    public ChatEntryViewModel(string sender, string text, DateTimeOffset sentAtUtc, bool isMine)
    {
        Sender = sender;
        Text = text;
        Timestamp = sentAtUtc.ToLocalTime().ToString("HH:mm");
        IsMine = isMine;
    }

    public string Sender { get; }

    public string Text { get; }

    public string Timestamp { get; }

    /// <summary>Drives right-alignment and bubble colour in the view.</summary>
    public bool IsMine { get; }
}
