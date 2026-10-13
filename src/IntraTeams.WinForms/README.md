# IntraTeams.WinForms — Windows Forms client

A second desktop client for the same `IntraTeams.Server`. It interoperates with the WPF
client: a WinForms user can chat with and call a WPF user, and vice versa. The protocol,
ICE policy (no STUN, no TURN, host candidates only) and codecs are identical.

## Shape

```
MainForm  (wrapper / mediator)            owns the ONE SignalR connection
├─ People list, each row with its own audio & video call icons
├─ Chat pane                              chat handled by the form itself
├─ CallControl  (UserControl)             audio, video, screen share via SIPSorcery
│    ├─ VideoView remote (fill)            knows nothing about SignalR
│    ├─ VideoView local  (picture-in-picture)
│    └─ Mute / Start video / Share screen / Hang up
└─ IncomingCallForm                       non-modal ringing prompt
```

### The CallControl contract

`CallControl` speaks only WebRTC. Any host can drop it on a form and supply the
signaling; `MainForm` is one such host.

| Direction | Member | Meaning |
|---|---|---|
| host → control | `StartOutgoingCallAsync(name, withVideo)` | Caller: build session, raise the offer |
| host → control | `PrepareIncomingCall(name, withVideo)` | Callee: build session **before** accepting |
| host → control | `ApplyRemoteDescriptionAsync(type, sdp)` | Peer's offer/answer; an offer produces an answer |
| host → control | `AddRemoteIceCandidate(...)` | Peer's candidate (queued if early) |
| host → control | `EndCallAsync(status)` | Tear down; safe when idle |
| control → host | `LocalSdpReady` | Send this offer/answer to the peer |
| control → host | `LocalIceCandidateReady` | Send this candidate to the peer |
| control → host | `StateChanged` | Connecting / Connected / Failed / Idle |
| control → host | `HangUpRequested` | User pressed Hang up |
| control → host | `Diagnostic` | Log line |

Call methods on the UI thread; events are always raised on the UI thread.
The control never ends a call by itself. It *reports* `Failed` or `HangUpRequested`, and
the host decides what to tell the peer before calling `EndCallAsync`. Call lifecycle stays
in one place, which is what lets the control be reused under a different signaling stack.

**Integrating a different signaling hub?** See [`docs/CallControl-Integration.md`](../../docs/CallControl-Integration.md) for the full contract, call-flow diagrams, failure handling and an example wrapper.

### Shared code

The UI-agnostic engine is **linked** from `IntraTeams.Client` rather than copied, so a
fix lands in both clients. These files appear under *Linked* in Solution Explorer:

`AppConfig.cs`, `SignalingClient.cs`, `PeerSession.cs`, `MediaStack.cs`, `GdiScreenCapture.cs`

Only the rendering (`VideoView`, replacing WPF's `VideoSurface`) and the UI are new.

## Fixes over the WPF client's call handling

Gaps found while reviewing the WPF client are handled in this wrapper. The WPF client
itself is unchanged.

1. **Early ICE candidates.** The callee trickles candidates before it sends `AcceptCall`.
   They can reach the caller before `CallAccepted`, when there is no session yet. The WPF
   client drops them. `MainForm` holds them and hands them over once the session exists.
2. **Peer vanishes while ringing.** The hub only sends `CallEnded` to users already *in* a
   call. If the other side crashes while dialing or ringing, `MainForm` notices it has left
   the roster and ends the call, instead of ringing forever.
3. **Stale accept clean-up.** If an accept crosses a cancel on the wire, the server would
   leave both users marked busy. The wrapper sends an explicit `EndCall` for the stale call.
4. **Outgoing calls can be cancelled and time out.** **Cancel call** appears under the people
   list while ringing, and an unanswered call gives up after 45 seconds.
5. **Reconnect keeps `--suffix`.** Re-registration uses the same machine string as the first
   registration, and an in-progress call is ended cleanly, because the new connection id
   cannot resume it.

## Run

```powershell
dotnet run --project src\IntraTeams.Server
dotnet build src\IntraTeams.WinForms
cd src\IntraTeams.WinForms\bin\Debug\net10.0-windows10.0.17763.0
start IntraTeams.WinForms.exe --suffix W1
start IntraTeams.WinForms.exe --suffix W2      # or a WPF client: IntraTeams.exe --suffix A
```

The command line is the same as the WPF client's: `--server`, `--suffix`, `--camera`,
`--list-cameras`. Settings are in `appsettings.json` next to the exe.

Use a headset on at least one side, because there is no echo cancellation. **Show
connection log** in the status bar shows the same ICE/SDP trace as the WPF client.

## Not yet verified

This project was written without a .NET SDK available, so it has **not been compiled yet**.
The SIPSorcery-facing code is the linked WPF-client code, which does build. The new code
uses only WinForms and GDI+ APIs. The first build may still turn up small errors. Build it,
then work through `TESTING.md` phases 4–10 using this exe.
