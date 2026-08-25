using IntraTeams.Client.Infrastructure;
using IntraTeams.Shared;

namespace IntraTeams.Client.ViewModels;

/// <summary>One row in the people list.</summary>
public sealed class UserItemViewModel : ObservableObject
{
    private string _displayName = "";
    private PresenceState _presence;

    public UserItemViewModel(UserInfo user)
    {
        ConnectionId = user.ConnectionId;
        Update(user);
    }

    public string ConnectionId { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => Set(ref _displayName, value);
    }

    public PresenceState Presence
    {
        get => _presence;
        private set
        {
            if (Set(ref _presence, value))
            {
                Raise(nameof(PresenceText));
                Raise(nameof(IsAvailable));
            }
        }
    }

    public string PresenceText => Presence == PresenceState.InCall ? "In a call" : "Available";

    public bool IsAvailable => Presence == PresenceState.Available;

    /// <summary>Initials shown in the avatar circle, e.g. "AS".</summary>
    public string Initials
    {
        get
        {
            var name = DisplayName.Trim();
            if (name.Length == 0)
            {
                return "?";
            }

            var parts = name.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]))
                : char.ToUpperInvariant(name[0]).ToString();
        }
    }

    public void Update(UserInfo user)
    {
        DisplayName = user.DisplayName;
        Presence = user.Presence;
        Raise(nameof(Initials));
    }
}
