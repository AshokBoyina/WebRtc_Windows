using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Windows.Forms;
using IntraTeams.Client.Infrastructure;
using IntraTeams.Client.Signaling;
using IntraTeams.Shared;
using IntraTeams.WinForms.Controls;

namespace IntraTeams.WinForms;

/// <summary>
/// The wrapper. It owns the ONE SignalR connection and mediates between it
/// and the two features hosted in the form:
///
///   - chat:        handled here directly (roster selection + RichTextBox);
///   - media:       delegated to <see cref="CallControl"/>, which never sees
///                  SignalR. This form translates hub messages into control
///                  method calls, and control events into hub messages.
///
///   SignalR hub                 MainForm (mediator)               CallControl
///   -----------                 -------------------               -----------
///   IncomingCall      ->        ringing dialog
///                               Accept: PrepareIncomingCall  ->   build session
///                     <-        AcceptCall
///   CallAccepted      ->        StartOutgoingCallAsync       ->   build + offer
///                     <-        SendSdp            <-  LocalSdpReady
///   ReceiveSdp        ->        ApplyRemoteDescriptionAsync  ->   (answer)
///                     <-        SendIceCandidate   <-  LocalIceCandidateReady
///   ReceiveIceCandidate ->      AddRemoteIceCandidate        ->
///                               StateChanged(Connected)      <-   media up
///                     <-        EndCall            <-  HangUpRequested / Failed
///   CallEnded         ->        EndCallAsync                 ->   teardown
///   ReceiveChat       ->        chat pane (never touches the control)
///
/// The signaling-level call state machine lives here (Idle, Dialing, Ringing,
/// Connecting, InCall). Only the caller ever creates an offer, exactly as in
/// the WPF client, so there is no glare handling.
/// </summary>
public partial class MainForm : Form
{
    private const int MaxLogLines = 500;

    /// <summary>How long an outgoing call rings before it is cancelled as unanswered.</summary>
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(45);

    private readonly AppConfig _config;
    private readonly SignalingClient _signaling;

    private readonly string _userName = Environment.UserName;
    private readonly string _machineName;

    private Font? _boldFont;

    // Per-row call icons in the people list.
    private const int RowIconSize = 30;
    private const int RowIconGap = 4;
    private const int RowIconRightMargin = 6;
    private const string PhoneGlyph = "\uE717";   // Segoe MDL2 Assets / Segoe Fluent Icons "Phone"
    private const string VideoGlyph = "\uE714";   // ... "Video"
    private Font? _iconFont;
    private int _hoverIndex = -1;
    private RowButton _hoverButton = RowButton.None;
    private string _hoverToolTip = "";
    private bool _registered;
    private bool _shutdownStarted;
    private bool _closeAllowed;
    private bool _suppressSelectionEvents;

    // ---------------------------------------------------------- call state

    private WrapperCallState _callState = WrapperCallState.Idle;
    private string _callId = "";
    private string _peerId = "";
    private string _peerName = "";
    private bool _callWithVideo;
    private CallInvite? _pendingInvite;
    private IncomingCallForm? _incomingDialog;

    /// <summary>
    /// ICE candidates from the peer that arrived before our CallControl had a
    /// session. This happens on the CALLER: the callee builds its session and
    /// starts trickling candidates before it sends AcceptCall, so some can
    /// overtake CallAccepted. The WPF client silently drops these; here they
    /// are held and handed to the control once its session exists.
    /// </summary>
    private readonly List<IceCandidateMessage> _earlyRemoteCandidates = new();

    // ---------------------------------------------------------------- chat

    /// <summary>Conversation history keyed by the OTHER party's connection id.</summary>
    private readonly Dictionary<string, List<ChatLine>> _conversations = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _unread = new(StringComparer.Ordinal);

    /// <summary>Whose conversation the chat pane is currently rendering.</summary>
    private string? _renderedConversationId;

    public MainForm(AppConfig config)
    {
        _config = config;
        _machineName = string.IsNullOrWhiteSpace(config.DisplayNameSuffix)
            ? Environment.MachineName
            : $"{Environment.MachineName}/{config.DisplayNameSuffix}";

        InitializeComponent();

        _boldFont = new Font(lstPeople.Font, FontStyle.Bold);
        _iconFont = CreateIconFont();

        Text = $"IntraTeams - {_userName} ({_machineName})";
        lblMe.Text = $"{_userName} ({_machineName})";

        callControl.Settings = config;
        WireCallControl();

        _signaling = new SignalingClient(config.ServerUrl);
        WireSignaling();

        UpdateCommands();
        UpdateChatHeader();
    }

    // =================================================================
    //  Startup / connection
    // =================================================================

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        await ConnectAndRegisterAsync();
    }

    private async Task ConnectAndRegisterAsync()
    {
        try
        {
            ShowBanner(null);
            SetStatus($"Connecting to {_config.ServerUrl}...");

            if (_signaling.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected)
            {
                await _signaling.ConnectAsync();
            }

            await RegisterAsync();
        }
        catch (Exception ex)
        {
            _registered = false;
            ShowBanner($"Cannot reach the signaling server at {_config.ServerUrl} - {ex.Message}   (click to retry)");
            SetStatus("Not connected.");
            Log($"Connect failed: {ex.Message}");
            UpdateCommands();
        }
    }

    /// <summary>
    /// Registers (or re-registers after a reconnect) with the SAME name and
    /// machine string, including any --suffix, so two local instances stay
    /// distinguishable after a network blip.
    /// </summary>
    private async Task RegisterAsync()
    {
        var roster = await _signaling.RegisterAsync(_userName, _machineName);
        _registered = true;

        ApplyRoster(roster);
        ShowBanner(null);
        SetStatus("Connected. Pick someone to chat with or call.");
        Log($"Registered as {_userName} ({_machineName}) on {_config.ServerUrl}");
        UpdateCommands();
    }

    private async void lblBanner_Click(object? sender, EventArgs e)
    {
        // Retry when SignalR has given up its own automatic retries, or when the
        // connection is up but registration failed. Never while it is mid-reconnect.
        var state = _signaling.State;
        if (state == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected ||
            (state == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected && !_registered))
        {
            await ConnectAndRegisterAsync();
        }
    }

    // =================================================================
    //  Signaling -> mediator
    // =================================================================

    private void WireSignaling()
    {
        // SignalR raises these on pool threads; every handler is marshalled.
        _signaling.RosterChanged += roster => Ui(() => ApplyRoster(roster));
        _signaling.IncomingCall += invite => Ui(() => OnIncomingCall(invite));
        _signaling.CallAccepted += (from, callId) => Ui(() => OnCallAccepted(from, callId));
        _signaling.CallRejected += (from, reason) => Ui(() => OnCallRejected(from, reason));
        _signaling.CallEnded += (from, reason) => Ui(() => OnCallEnded(from, reason));
        _signaling.SdpReceived += message => Ui(() => OnRemoteSdp(message));
        _signaling.IceCandidateReceived += message => Ui(() => OnRemoteIceCandidate(message));
        _signaling.ChatReceived += message => Ui(() => OnChatReceived(message));

        _signaling.Reconnecting += () => Ui(OnSignalingReconnecting);
        _signaling.Reconnected += () => Ui(OnSignalingReconnected);
        _signaling.Closed += error => Ui(() => OnSignalingClosed(error));
    }

    private async void OnSignalingReconnecting()
    {
        _registered = false;
        ShowBanner("Connection to the signaling server lost. Reconnecting...");
        UpdateCommands();

        // Our connection id is about to change; the server will tell the peer
        // we disconnected. The call cannot be resumed, so end it cleanly here.
        if (_callState != WrapperCallState.Idle)
        {
            await ResetCallAsync("Call dropped: lost the connection to the signaling server.");
        }
    }

    private async void OnSignalingReconnected()
    {
        Log("Signaling reconnected with a new connection id; re-registering.");

        try
        {
            await RegisterAsync();
        }
        catch (Exception ex)
        {
            Log($"Re-register failed: {ex.Message}");
            ShowBanner($"Reconnected, but registration failed: {ex.Message}   (click to retry)");
        }
    }

    private async void OnSignalingClosed(string? error)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _registered = false;
        ShowBanner($"Disconnected from the signaling server. {error}   (click to retry)");
        SetStatus("Not connected.");
        UpdateCommands();

        if (_callState != WrapperCallState.Idle)
        {
            await ResetCallAsync("Call dropped: disconnected from the signaling server.");
        }
    }

    // =================================================================
    //  Roster
    // =================================================================

    private async void ApplyRoster(IReadOnlyList<UserInfo> roster)
    {
        var mine = _signaling.ConnectionId;
        var others = roster
            .Where(u => !string.Equals(u.ConnectionId, mine, StringComparison.Ordinal))
            .Select(u => new PersonItem(u))
            .ToList();

        var selectedId = SelectedPerson?.ConnectionId;

        _suppressSelectionEvents = true;
        lstPeople.BeginUpdate();

        try
        {
            lstPeople.Items.Clear();

            foreach (var person in others)
            {
                lstPeople.Items.Add(person);
            }

            if (selectedId is not null)
            {
                var index = others.FindIndex(p => p.ConnectionId == selectedId);
                lstPeople.SelectedIndex = index; // -1 clears the selection if they left
            }
        }
        finally
        {
            lstPeople.EndUpdate();
            _suppressSelectionEvents = false;
        }

        lblPeopleHeader.Text = $"PEOPLE ({others.Count})";
        OnSelectedPersonChanged();

        // The hub only sends CallEnded for users who were already IN a call. If
        // the peer vanishes while we are dialing or ringing, nothing else would
        // ever tell us - so treat "gone from the roster" as a hang-up.
        if (_callState != WrapperCallState.Idle &&
            _peerId.Length > 0 &&
            others.All(p => p.ConnectionId != _peerId))
        {
            await ResetCallAsync($"{_peerName} went offline.");
        }
    }

    private PersonItem? SelectedPerson => lstPeople.SelectedItem as PersonItem;

    private void lstPeople_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!_suppressSelectionEvents)
        {
            OnSelectedPersonChanged();
        }
    }

    private void OnSelectedPersonChanged()
    {
        var target = ChatTargetId;

        if (!string.Equals(target, _renderedConversationId, StringComparison.Ordinal))
        {
            RenderConversation(target);
        }

        if (target is not null && _unread.Remove(target))
        {
            lstPeople.Invalidate();
        }

        UpdateChatHeader();
        UpdateCommands();
    }

    private void SelectPerson(string connectionId)
    {
        for (var i = 0; i < lstPeople.Items.Count; i++)
        {
            if (lstPeople.Items[i] is PersonItem p && p.ConnectionId == connectionId)
            {
                lstPeople.SelectedIndex = i;
                return;
            }
        }
    }

    private void lstPeople_DrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();

        if (e.Index < 0 || e.Index >= lstPeople.Items.Count || lstPeople.Items[e.Index] is not PersonItem person)
        {
            return;
        }

        var g = e.Graphics;
        var bounds = e.Bounds;
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var foreColor = selected ? SystemColors.HighlightText : lstPeople.ForeColor;
        var mutedColor = selected ? SystemColors.HighlightText : SystemColors.GrayText;

        // Presence dot: green available, amber in a call.
        var dotColor = person.IsAvailable ? Color.FromArgb(46, 158, 91) : Color.FromArgb(214, 140, 40);
        var dot = new Rectangle(bounds.Left + 10, bounds.Top + ((bounds.Height - 10) / 2), 10, 10);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(dotColor))
        {
            g.FillEllipse(brush, dot);
        }

        g.SmoothingMode = SmoothingMode.Default;

        var unread = _unread.TryGetValue(person.ConnectionId, out var count) ? count : 0;
        var badgeWidth = unread > 0 ? 30 : 0;
        var iconsWidth = (2 * RowIconSize) + RowIconGap + RowIconRightMargin;
        var textLeft = bounds.Left + 28;
        var textWidth = Math.Max(10, bounds.Width - 28 - iconsWidth - badgeWidth - 8);
        var half = bounds.Height / 2;

        TextRenderer.DrawText(g, person.DisplayName, _boldFont ?? lstPeople.Font,
            new Rectangle(textLeft, bounds.Top + 2, textWidth, half),
            foreColor, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, person.IsAvailable ? "Available" : "In a call", lstPeople.Font,
            new Rectangle(textLeft, bounds.Top + half, textWidth, half - 2),
            mutedColor, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        if (unread > 0)
        {
            var badge = new Rectangle(bounds.Right - iconsWidth - badgeWidth - 6, bounds.Top + ((bounds.Height - 18) / 2), badgeWidth, 18);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Color.FromArgb(91, 91, 214)))
            {
                g.FillEllipse(brush, badge);
            }

            g.SmoothingMode = SmoothingMode.Default;

            TextRenderer.DrawText(g, unread > 99 ? "99+" : unread.ToString(), lstPeople.Font, badge, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // 1-to-1 call icons on every row.
        var canCall = CanCall(person);
        DrawRowIcon(g, GetRowButtonRect(bounds, RowButton.Audio), RowButton.Audio, e.Index, canCall, selected);
        DrawRowIcon(g, GetRowButtonRect(bounds, RowButton.Video), RowButton.Video, e.Index, canCall, selected);

        e.DrawFocusRectangle();
    }

    private enum RowButton
    {
        None,
        Audio,
        Video
    }

    /// <summary>Hit area of a row's call icon. Video is right-most, audio to its left.</summary>
    private static Rectangle GetRowButtonRect(Rectangle row, RowButton button)
    {
        var top = row.Top + ((row.Height - RowIconSize) / 2);
        var videoLeft = row.Right - RowIconRightMargin - RowIconSize;

        return button == RowButton.Video
            ? new Rectangle(videoLeft, top, RowIconSize, RowIconSize)
            : new Rectangle(videoLeft - RowIconGap - RowIconSize, top, RowIconSize, RowIconSize);
    }

    private void DrawRowIcon(Graphics g, Rectangle rect, RowButton button, int index, bool enabled, bool rowSelected)
    {
        var hovered = enabled && index == _hoverIndex && button == _hoverButton;

        Color color;
        if (!enabled)
        {
            color = rowSelected ? Color.FromArgb(170, SystemColors.HighlightText) : Color.FromArgb(185, 185, 192);
        }
        else
        {
            color = rowSelected ? SystemColors.HighlightText : Color.FromArgb(91, 91, 214);
        }

        if (hovered)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var hover = new SolidBrush(rowSelected ? Color.FromArgb(60, Color.White) : Color.FromArgb(40, 91, 91, 214)))
            {
                g.FillEllipse(hover, rect);
            }

            g.SmoothingMode = SmoothingMode.Default;
        }

        if (_iconFont is not null)
        {
            TextRenderer.DrawText(g, button == RowButton.Video ? VideoGlyph : PhoneGlyph, _iconFont, rect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }

        // Fallback when no icon font is installed: simple vector shapes.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(color))
        {
            var cx = rect.Left + (rect.Width / 2);
            var cy = rect.Top + (rect.Height / 2);

            if (button == RowButton.Video)
            {
                // Camera body + lens triangle.
                g.FillRectangle(brush, cx - 9, cy - 5, 12, 10);
                g.FillPolygon(brush, new[] { new Point(cx + 4, cy), new Point(cx + 9, cy - 5), new Point(cx + 9, cy + 5) });
            }
            else
            {
                // Handset: two ear/mouth pieces joined by a thick arc.
                using var pen = new Pen(color, 3.5f);
                g.DrawArc(pen, cx - 7, cy - 7, 14, 14, 135, 180);
                g.FillEllipse(brush, cx - 9, cy - 1, 6, 6);
                g.FillEllipse(brush, cx + 1, cy - 9, 6, 6);
            }
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>
    /// Segoe MDL2 Assets ships with Windows 10; Segoe Fluent Icons with Windows 11.
    /// Both carry the same Phone / Video code points. GDI+ silently substitutes a
    /// different font when the requested one is missing, so check the name.
    /// </summary>
    private static Font? CreateIconFont()
    {
        foreach (var family in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var font = new Font(family, 12f, FontStyle.Regular, GraphicsUnit.Point);
            if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
        }

        return null;
    }

    private (int Index, RowButton Button) HitTestRowButton(Point location)
    {
        var index = lstPeople.IndexFromPoint(location);
        if (index < 0 || index >= lstPeople.Items.Count)
        {
            return (-1, RowButton.None);
        }

        var row = lstPeople.GetItemRectangle(index);

        if (GetRowButtonRect(row, RowButton.Audio).Contains(location))
        {
            return (index, RowButton.Audio);
        }

        if (GetRowButtonRect(row, RowButton.Video).Contains(location))
        {
            return (index, RowButton.Video);
        }

        return (index, RowButton.None);
    }

    private void lstPeople_MouseMove(object? sender, MouseEventArgs e)
    {
        var (index, button) = HitTestRowButton(e.Location);
        if (button == RowButton.None)
        {
            index = -1;
        }

        if (index != _hoverIndex || button != _hoverButton)
        {
            InvalidateRow(_hoverIndex);
            _hoverIndex = index;
            _hoverButton = button;
            InvalidateRow(_hoverIndex);
        }

        var person = index >= 0 ? lstPeople.Items[index] as PersonItem : null;
        var enabled = person is not null && CanCall(person);

        lstPeople.Cursor = enabled ? Cursors.Hand : Cursors.Default;

        var tip = person is null
            ? ""
            : enabled
                ? (button == RowButton.Video ? $"Video call {person.DisplayName}" : $"Audio call {person.DisplayName}")
                : WhyCannotCall(person);

        if (tip != _hoverToolTip)
        {
            _hoverToolTip = tip;
            peopleToolTip.SetToolTip(lstPeople, tip);
        }
    }

    private void lstPeople_MouseLeave(object? sender, EventArgs e)
    {
        InvalidateRow(_hoverIndex);
        _hoverIndex = -1;
        _hoverButton = RowButton.None;
        lstPeople.Cursor = Cursors.Default;
        _hoverToolTip = "";
        peopleToolTip.SetToolTip(lstPeople, "");
    }

    private void lstPeople_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var (index, button) = HitTestRowButton(e.Location);
        if (button == RowButton.None || lstPeople.Items[index] is not PersonItem person)
        {
            return;
        }

        // The mouse-down has already selected the row, so chat follows the call.
        StartCall(person, withVideo: button == RowButton.Video);
    }

    private void InvalidateRow(int index)
    {
        if (index >= 0 && index < lstPeople.Items.Count)
        {
            lstPeople.Invalidate(lstPeople.GetItemRectangle(index));
        }
    }

    private string WhyCannotCall(PersonItem person)
    {
        if (!_registered)
        {
            return "Not connected to the server";
        }

        if (_callState != WrapperCallState.Idle || callControl.State != CallControlState.Idle)
        {
            return "You are already in a call";
        }

        return person.IsAvailable ? "" : $"{person.DisplayName} is in a call";
    }

    // =================================================================
    //  Call control: user actions
    // =================================================================

    private async void StartCall(PersonItem target, bool withVideo)
    {
        if (!CanCall(target))
        {
            return;
        }

        _peerId = target.ConnectionId;
        _peerName = target.DisplayName;
        _callId = Guid.NewGuid().ToString("N");
        _callWithVideo = withVideo;
        _earlyRemoteCandidates.Clear();

        SetCallState(WrapperCallState.Dialing, $"Calling {_peerName}...");
        callControl.ShowStatus($"Calling {_peerName}... (Cancel call is under the people list)");
        Log($"Inviting {_peerName} (video={withVideo}).");

        var callId = _callId;
        if (!await SendAsync(() => _signaling.InviteAsync(_peerId, callId, withVideo), "Could not place the call") &&
            _callId == callId)
        {
            await ResetCallAsync("Could not place the call.");
        }
    }

    private void btnCancelCall_Click(object? sender, EventArgs e) => HangUp();

    private async void ringTimer_Tick(object? sender, EventArgs e)
    {
        ringTimer.Stop();

        if (_callState == WrapperCallState.Dialing)
        {
            Log($"No answer after {RingTimeout.TotalSeconds:0} s.");
            await EndCallAsync(CallEndReasons.Cancelled, $"{_peerName} did not answer.");
        }
    }

    private async void OnIncomingCall(CallInvite invite)
    {
        if (_callState != WrapperCallState.Idle)
        {
            // Already busy: decline immediately so the caller is not left ringing.
            Log($"Auto-declined a call from {invite.FromDisplayName}: already busy.");
            await SendAsync(() => _signaling.RejectAsync(invite.FromConnectionId, invite.CallId, CallEndReasons.Busy),
                "Could not decline the call");
            return;
        }

        _pendingInvite = invite;
        _peerId = invite.FromConnectionId;
        _peerName = invite.FromDisplayName;
        _callId = invite.CallId;
        _callWithVideo = invite.WithVideo;
        _earlyRemoteCandidates.Clear();

        SetCallState(WrapperCallState.Ringing,
            invite.WithVideo ? $"{_peerName} is calling with video" : $"{_peerName} is calling");
        Log($"Incoming call from {_peerName}.");

        SelectPerson(_peerId);
        ShowIncomingDialog(invite);
    }

    private void ShowIncomingDialog(CallInvite invite)
    {
        CloseIncomingDialog();

        var dialog = new IncomingCallForm(invite.FromDisplayName, invite.WithVideo);
        dialog.Accepted += (_, _) => AcceptIncomingCall();
        dialog.Declined += (_, _) => DeclineIncomingCall();

        // A minimised owner hides its owned windows and reports a -32000
        // position, so bring the main window back first.
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        // CenterParent only applies to ShowDialog, and this dialog must stay
        // non-modal, so centre it by hand.
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(
            Left + Math.Max(0, (Width - dialog.Width) / 2),
            Top + Math.Max(0, (Height - dialog.Height) / 2));

        _incomingDialog = dialog;
        dialog.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_incomingDialog, dialog))
            {
                _incomingDialog = null;
            }
        };

        dialog.Show(this);
        SystemSounds.Asterisk.Play();
    }

    private void CloseIncomingDialog()
    {
        var dialog = _incomingDialog;
        _incomingDialog = null;

        if (dialog is not null && !dialog.IsDisposed)
        {
            dialog.Dismiss();
        }
    }

    private async void AcceptIncomingCall()
    {
        var invite = _pendingInvite;
        if (invite is null || _callState != WrapperCallState.Ringing)
        {
            return;
        }

        _pendingInvite = null;
        CloseIncomingDialog();

        SetCallState(WrapperCallState.Connecting, $"Connecting to {_peerName}...");

        try
        {
            // Session first, THEN accept: the caller's offer follows the accept
            // immediately and must find a session ready to answer it.
            callControl.PrepareIncomingCall(invite.FromDisplayName, invite.WithVideo);
            FlushEarlyCandidates();

            await _signaling.AcceptAsync(invite.FromConnectionId, invite.CallId);
            Log("Call accepted.");
        }
        catch (Exception ex)
        {
            Log($"Could not accept the call: {ex.Message}");
            await EndCallAsync(CallEndReasons.Failed, "Could not accept the call.");
        }
    }

    private async void DeclineIncomingCall()
    {
        var invite = _pendingInvite;
        if (invite is null)
        {
            return;
        }

        _pendingInvite = null;

        await SendAsync(() => _signaling.RejectAsync(invite.FromConnectionId, invite.CallId, CallEndReasons.Rejected),
            "Could not decline the call");

        await ResetCallAsync("Call declined.");
    }

    /// <summary>Hang up from either the CallControl's button or anywhere else.</summary>
    private async void HangUp()
    {
        switch (_callState)
        {
            case WrapperCallState.Idle:
                return;

            case WrapperCallState.Ringing:
                DeclineIncomingCall();
                return;

            case WrapperCallState.Dialing:
                await EndCallAsync(CallEndReasons.Cancelled, "Call cancelled.");
                return;

            default:
                await EndCallAsync(CallEndReasons.HungUp, "Call ended.");
                return;
        }
    }

    // =================================================================
    //  Call control: signaling events
    // =================================================================

    private async void OnCallAccepted(string fromConnectionId, string callId)
    {
        if (callId == _callId &&
            (_callState != WrapperCallState.Dialing || fromConnectionId != _peerId))
        {
            return; // Duplicate or out-of-order accept for the current call.
        }

        if (_callState != WrapperCallState.Dialing ||
            fromConnectionId != _peerId ||
            callId != _callId)
        {
            // An accept for a call we already cancelled (it crossed our EndCall
            // on the wire). The server has just marked both of us busy for it,
            // so end it explicitly or neither of us could be called again.
            Log("Ignoring a stale CallAccepted; telling the server that call is over.");
            await SendAsync(() => _signaling.EndCallAsync(fromConnectionId, callId, CallEndReasons.Cancelled),
                "Could not cancel a stale call");
            return;
        }

        SetCallState(WrapperCallState.Connecting, $"Connecting to {_peerName}...");

        try
        {
            // Only the caller ever offers - no glare, no rollback.
            var startTask = callControl.StartOutgoingCallAsync(_peerName, _callWithVideo);

            // The session exists as soon as the call above returns its task
            // (BuildSession runs synchronously before the first await), so
            // anything the callee trickled early can be handed over now.
            FlushEarlyCandidates();

            await startTask;
        }
        catch (Exception ex)
        {
            Log($"Could not start the call: {ex.Message}");
            await EndCallAsync(CallEndReasons.Failed, "Could not start the call.");
        }
    }

    private async void OnCallRejected(string fromConnectionId, string reason)
    {
        if (fromConnectionId != _peerId ||
            _callState is not (WrapperCallState.Dialing or WrapperCallState.Connecting))
        {
            return;
        }

        await ResetCallAsync(reason == CallEndReasons.Busy
            ? $"{_peerName} is already in a call."
            : $"{_peerName} declined.");
    }

    private async void OnCallEnded(string fromConnectionId, string reason)
    {
        if (_callState == WrapperCallState.Idle || fromConnectionId != _peerId)
        {
            return;
        }

        await ResetCallAsync(reason switch
        {
            CallEndReasons.PeerDisconnected => $"{_peerName} disconnected.",
            CallEndReasons.Rejected => $"{_peerName} declined.",
            CallEndReasons.Cancelled => $"{_peerName} cancelled the call.",
            CallEndReasons.Failed => $"Call with {_peerName} failed to connect.",
            _ => "Call ended."
        });
    }

    private async void OnRemoteSdp(SdpMessage message)
    {
        if (_callState == WrapperCallState.Idle || !IsForCurrentCall(message.FromConnectionId, message.CallId))
        {
            return;
        }

        if (!callControl.HasSession)
        {
            Log($"Remote {message.Type} arrived before the media session existed - ignored.");
            return;
        }

        await callControl.ApplyRemoteDescriptionAsync(message.Type, message.Sdp);
    }

    private void OnRemoteIceCandidate(IceCandidateMessage message)
    {
        if (_callState == WrapperCallState.Idle || !IsForCurrentCall(message.FromConnectionId, message.CallId))
        {
            return;
        }

        if (callControl.HasSession)
        {
            callControl.AddRemoteIceCandidate(
                message.Candidate, message.SdpMid, message.SdpMLineIndex, message.UsernameFragment);
        }
        else
        {
            _earlyRemoteCandidates.Add(message);
            Log("Held an ICE candidate that arrived before the media session existed.");
        }
    }

    /// <summary>
    /// True when a relayed SDP/ICE message belongs to the call in progress, so a
    /// straggler from an earlier call with the same person cannot leak into a
    /// new one. An empty CallId is accepted for tolerance of other clients.
    /// </summary>
    private bool IsForCurrentCall(string fromConnectionId, string? callId)
        => fromConnectionId == _peerId &&
           (string.IsNullOrEmpty(callId) || callId == _callId);

    private void FlushEarlyCandidates()
    {
        if (_earlyRemoteCandidates.Count == 0 || !callControl.HasSession)
        {
            return;
        }

        var held = _earlyRemoteCandidates.ToArray();
        _earlyRemoteCandidates.Clear();

        Log($"Releasing {held.Length} early ICE candidate(s) to the media session.");

        foreach (var message in held)
        {
            callControl.AddRemoteIceCandidate(
                message.Candidate, message.SdpMid, message.SdpMLineIndex, message.UsernameFragment);
        }
    }

    // =================================================================
    //  CallControl -> mediator
    // =================================================================

    private void WireCallControl()
    {
        // CallControl raises all of these on the UI thread already.
        callControl.LocalSdpReady += OnLocalSdpReady;
        callControl.LocalIceCandidateReady += OnLocalIceCandidateReady;
        callControl.StateChanged += OnCallControlStateChanged;
        callControl.HangUpRequested += (_, _) => HangUp();
        callControl.Diagnostic += (_, e) => Log(e.Message);
    }

    private async void OnLocalSdpReady(object? sender, LocalSdpEventArgs e)
    {
        if (_peerId.Length == 0)
        {
            return;
        }

        var message = new SdpMessage
        {
            CallId = _callId,
            ToConnectionId = _peerId,
            Type = e.Type,
            Sdp = e.Sdp
        };

        await SendAsync(() => _signaling.SendSdpAsync(message), $"Could not send the {e.Type}");
        Log($"Sent {e.Type} to {_peerName}.");
    }

    private async void OnLocalIceCandidateReady(object? sender, LocalIceCandidateEventArgs e)
    {
        if (_peerId.Length == 0)
        {
            return;
        }

        var message = new IceCandidateMessage
        {
            CallId = _callId,
            ToConnectionId = _peerId,
            Candidate = e.Candidate,
            SdpMid = e.SdpMid,
            SdpMLineIndex = e.SdpMLineIndex,
            UsernameFragment = e.UsernameFragment
        };

        await SendAsync(() => _signaling.SendIceCandidateAsync(message), "Could not send an ICE candidate");
    }

    private async void OnCallControlStateChanged(object? sender, CallStateChangedEventArgs e)
    {
        switch (e.State)
        {
            case CallControlState.Connected when _callState == WrapperCallState.Connecting:
                SetCallState(WrapperCallState.InCall, $"In a call with {_peerName}");
                break;

            case CallControlState.Failed when _callState != WrapperCallState.Idle:
                // The control reports; the wrapper decides. Tell the peer, then tear down.
                await EndCallAsync(CallEndReasons.Failed, e.Detail);
                break;

            case CallControlState.Idle:
                // CanCall also looks at the control's state.
                UpdateCommands();
                break;
        }
    }

    // =================================================================
    //  Teardown
    // =================================================================

    /// <summary>
    /// Tears the call down locally, then tells the peer why. Local first, so
    /// Hang up takes effect immediately even if the hub is slow or stalled.
    /// </summary>
    private async Task EndCallAsync(string reason, string statusText)
    {
        var peerId = _peerId;
        var callId = _callId;

        if (_callState == WrapperCallState.Idle && peerId.Length == 0)
        {
            return; // Already ended (e.g. a double click on Hang up).
        }

        await ResetCallAsync(statusText);

        if (peerId.Length > 0 && _registered)
        {
            await SendAsync(() => _signaling.EndCallAsync(peerId, callId, reason), "Could not signal the hang up");
        }
    }

    /// <summary>Local teardown only. Idempotent.</summary>
    private async Task ResetCallAsync(string statusText)
    {
        CloseIncomingDialog();

        _pendingInvite = null;
        _earlyRemoteCandidates.Clear();
        _peerId = "";
        _peerName = "";
        _callId = "";
        _callWithVideo = false;

        SetCallState(WrapperCallState.Idle, statusText);

        try
        {
            await callControl.EndCallAsync(statusText);
        }
        catch (Exception ex)
        {
            Log($"Media teardown: {ex.Message}");
        }

        UpdateCommands();
    }

    private void SetCallState(WrapperCallState state, string statusText)
    {
        if (_callState != state)
        {
            Log($"Call state: {_callState} -> {state}");
        }

        _callState = state;

        if (state == WrapperCallState.Dialing)
        {
            ringTimer.Interval = (int)RingTimeout.TotalMilliseconds;
            ringTimer.Start();
        }
        else
        {
            ringTimer.Stop();
        }

        // The call pane exists only while a call does: it opens when the user
        // clicks a call icon (Dialing) or accepts (Connecting), and closes when
        // the call ends. While ringing in, the incoming-call dialog is enough.
        ShowCallPane(state is WrapperCallState.Dialing or WrapperCallState.Connecting or WrapperCallState.InCall);

        SetStatus(statusText);
        UpdateCommands();
        UpdateChatHeader();
    }

    /// <summary>Preferred chat width while the call pane is open; the call gets the rest.</summary>
    private const int ChatWidthDuringCall = 360;

    /// <summary>
    /// Opens or closes the call pane to the right of the chat. When it opens,
    /// the chat keeps about <see cref="ChatWidthDuringCall"/> pixels and the
    /// video takes the remaining width.
    /// </summary>
    private void ShowCallPane(bool show)
    {
        if (splitCallChat.Panel2Collapsed == !show)
        {
            return;
        }

        if (!show)
        {
            splitCallChat.Panel2Collapsed = true;
            return;
        }

        splitCallChat.Panel2Collapsed = false;

        var width = splitCallChat.ClientSize.Width;
        var max = width - splitCallChat.Panel2MinSize - splitCallChat.SplitterWidth;
        var distance = Math.Min(LogicalToDeviceUnits(ChatWidthDuringCall), max);

        if (distance >= splitCallChat.Panel1MinSize)
        {
            try
            {
                splitCallChat.SplitterDistance = distance;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Window too narrow for both minimums; WinForms keeps its own split.
            }
        }
    }

    // =================================================================
    //  Chat
    // =================================================================

    /// <summary>
    /// Who a message typed now would go to: the selected person, or - if
    /// nobody is selected - the person we are in a call with.
    /// </summary>
    private string? ChatTargetId
        => SelectedPerson?.ConnectionId ?? (_peerId.Length > 0 ? _peerId : null);

    private string ChatTargetName
        => SelectedPerson?.DisplayName ?? (_peerId.Length > 0 ? _peerName : "");

    private void btnSend_Click(object? sender, EventArgs e) => SendChat();

    private void txtChat_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            SendChat();
        }
    }

    private void txtChat_TextChanged(object? sender, EventArgs e) => UpdateCommands();

    private async void SendChat()
    {
        var target = ChatTargetId;
        var text = txtChat.Text.Trim();

        if (target is null || text.Length == 0 || !_registered)
        {
            return;
        }

        txtChat.Clear();

        // No local echo: the hub echoes the message back to the sender, so both
        // windows render from the same server-stamped object.
        await SendAsync(() => _signaling.SendChatAsync(target, text), "Could not send the message");
    }

    private void OnChatReceived(ChatMessage message)
    {
        var mine = string.Equals(message.FromConnectionId, _signaling.ConnectionId, StringComparison.Ordinal);
        var otherParty = mine ? message.ToConnectionId : message.FromConnectionId;

        if (string.IsNullOrEmpty(otherParty))
        {
            return;
        }

        var line = new ChatLine(mine ? "You" : message.FromDisplayName, message.Text, message.SentAtUtc, mine);

        if (!_conversations.TryGetValue(otherParty, out var conversation))
        {
            conversation = new List<ChatLine>();
            _conversations[otherParty] = conversation;
        }

        conversation.Add(line);

        if (string.Equals(otherParty, _renderedConversationId, StringComparison.Ordinal))
        {
            AppendChatLine(line);
        }
        else if (!mine)
        {
            _unread[otherParty] = (_unread.TryGetValue(otherParty, out var count) ? count : 0) + 1;
            lstPeople.Invalidate();

            if (!ContainsFocus)
            {
                SystemSounds.Asterisk.Play();
            }
        }
    }

    private void RenderConversation(string? connectionId)
    {
        _renderedConversationId = connectionId;
        rtbChat.Clear();

        if (connectionId is not null && _conversations.TryGetValue(connectionId, out var conversation))
        {
            foreach (var line in conversation)
            {
                AppendChatLine(line);
            }
        }
    }

    private void AppendChatLine(ChatLine line)
    {
        var nameColor = line.IsMine ? Color.FromArgb(91, 91, 214) : Color.FromArgb(46, 120, 80);

        AppendRun(line.Sender, _boldFont ?? rtbChat.Font, nameColor);
        AppendRun("   " + line.SentAtUtc.ToLocalTime().ToString("HH:mm") + Environment.NewLine,
            rtbChat.Font, SystemColors.GrayText);
        AppendRun(line.Text + Environment.NewLine + Environment.NewLine, rtbChat.Font, rtbChat.ForeColor);

        rtbChat.SelectionStart = rtbChat.TextLength;
        rtbChat.ScrollToCaret();
    }

    private void AppendRun(string text, Font font, Color color)
    {
        rtbChat.SelectionStart = rtbChat.TextLength;
        rtbChat.SelectionLength = 0;
        rtbChat.SelectionFont = font;
        rtbChat.SelectionColor = color;
        rtbChat.SelectedText = text;
    }

    private void UpdateChatHeader()
    {
        var name = ChatTargetName;
        lblChatHeader.Text = name.Length > 0 ? $"Chat with {name}" : "Chat - select someone";
    }

    // =================================================================
    //  Connection log
    // =================================================================

    private void lblToggleLog_Click(object? sender, EventArgs e)
    {
        pnlLog.Visible = !pnlLog.Visible;
        lblToggleLog.Text = pnlLog.Visible ? "Hide connection log" : "Show connection log";
    }

    private void Log(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            Ui(() => Log(message));
            return;
        }

        lstLog.Items.Add($"{DateTime.Now:HH:mm:ss}  {message}");

        while (lstLog.Items.Count > MaxLogLines)
        {
            lstLog.Items.RemoveAt(0);
        }

        lstLog.TopIndex = Math.Max(0, lstLog.Items.Count - 1);
    }

    // =================================================================
    //  UI state
    // =================================================================

    private bool CanCall(PersonItem person)
        => _registered &&
           _callState == WrapperCallState.Idle &&
           callControl.State == CallControlState.Idle &&
           person.IsAvailable;

    private void UpdateCommands()
    {
        // "Cancel call" only exists while an outgoing call is ringing.
        pnlCallButtons.Visible = _callState == WrapperCallState.Dialing;

        // Row icons enable/disable with call state and presence.
        lstPeople.Invalidate();

        btnSend.Enabled = _registered && ChatTargetId is not null && txtChat.TextLength > 0;
        txtChat.Enabled = _registered;
    }

    private void SetStatus(string text) => lblStatus.Text = text;

    private void ShowBanner(string? text)
    {
        lblBanner.Text = text ?? string.Empty;
        lblBanner.Visible = !string.IsNullOrEmpty(text);
    }

    // =================================================================
    //  Shutdown
    // =================================================================

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeAllowed)
        {
            return; // Cleanup finished; let this close through.
        }

        // Hold every close request until the async cleanup is done - including
        // a second click on X while it is still running.
        e.Cancel = true;

        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;

        try
        {
            if (_callState != WrapperCallState.Idle)
            {
                await EndCallAsync(CallEndReasons.HungUp, "Closing.");
            }

            await _signaling.DisposeAsync();
        }
        catch
        {
            // Closing anyway.
        }

        _closeAllowed = true;

        // Posted rather than called directly, so it can never re-enter this
        // handler if the awaits above all completed synchronously.
        if (!IsDisposed)
        {
            BeginInvoke(new Action(Close));
        }
    }

    // =================================================================
    //  Helpers
    // =================================================================

    /// <summary>Marshals to the UI thread. SignalR callbacks arrive on pool threads.</summary>
    private void Ui(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (!InvokeRequired)
        {
            action();
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>Runs a hub call, logging instead of throwing. Returns false if it failed.</summary>
    private async Task<bool> SendAsync(Func<Task> operation, string context)
    {
        try
        {
            await operation();
            return true;
        }
        catch (Exception ex)
        {
            Log($"{context}: {ex.Message}");
            return false;
        }
    }
}
