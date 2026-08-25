# IntraTeams — WPF + SignalR + WebRTC POC

A Teams-like 1-1 communication app for a **closed intranet**: presence, voice, video,
screen share and text chat, with **no STUN and no TURN server anywhere**.

- **Signaling / presence** — ASP.NET Core SignalR hub
- **Media** — WebRTC via SIPSorcery (pure managed C#, no browser engine)
- **UI** — WPF desktop client
- **Identity** — Windows username, picked up automatically

---

## Why this works without STUN or TURN

ICE gathers three kinds of candidate: **host** (your own NIC addresses), **server
reflexive** (your public address, discovered via STUN) and **relay** (via TURN).

On a single intranet both machines can already reach each other directly, so the
host candidates alone produce a working pair on the first connectivity check.
STUN would tell you nothing you don't know, and TURN exists only to relay when a
direct path is impossible.

This is not a hack — it's what Microsoft Teams itself does. Microsoft's own call-flow
documentation states that when local IP/port candidates have connectivity, the direct
peer-to-peer path is selected and the Transport Relay is never touched. Teams only
falls back to its relay when a client is off-network or a firewall blocks the direct path.

The client encodes that decision explicitly:

```csharp
var configuration = new RTCConfiguration
{
    iceServers = new List<RTCIceServer>(),   // empty, NOT null -> host candidates only
    X_ICEIncludeAllInterfaceAddresses = true,
    X_GatherTimeoutMs = 3000
};
```

`X_ICEIncludeAllInterfaceAddresses` matters more than it looks. On a developer machine
with VPN, Hyper-V, WSL or Docker adapters, SIPSorcery's single "best" local address is
frequently the wrong one — and one wrong candidate with no fallback means no call at all.

### One clarification on wording

**ICE is still used. ICE *servers* are not.** ICE is the connectivity-establishment
algorithm built into WebRTC — candidate gathering, pairing, connectivity checks — and it
cannot be switched off. What this app has none of is any external **STUN** or **TURN**
server. Those are what people usually mean by "ICE servers", and there are zero of them.

### How that is enforced, not just intended

Three mechanisms, so it cannot regress silently:

1. **Construction fails loudly.** `PeerSession` throws `InvalidOperationException` if
   `iceServers` is ever non-empty. Adding a STUN URL cannot go unnoticed.
2. **Local candidates are filtered.** Every gathered candidate's `typ` is parsed. Only
   `host` is sent to the peer; an `srflx` (STUN-derived) or `relay` (TURN-derived)
   candidate is logged as a warning and dropped.
3. **Remote candidates are filtered too.** If the far end were misconfigured and offered
   a relay candidate, this client refuses it. Media therefore cannot be routed through a
   third party even by accident.

To confirm at runtime, open **Connection log** during a call. You will see:

```
ICE servers configured: 0. No STUN, no TURN - host candidates only.
Local candidate #1 [host]: candidate:... 192.168.1.24 51234 typ host
Remote candidate #1 [host]: candidate:... 192.168.1.31 49876 typ host
```

Every candidate is tagged `[host]`. No `srflx`, no `relay`, and no outbound traffic to
port 3478 anywhere. The only server this app talks to is your own signaling hub, and only
to exchange SDP and candidates — never media.

---

## Solution layout

```
IntraTeams.sln
├─ src/
│  ├─ IntraTeams.Shared     net10.0           DTOs + hub method-name constants
│  ├─ IntraTeams.Server     net10.0           ASP.NET Core SignalR hub
│  └─ IntraTeams.Client     net10.0-windows   WPF desktop app
├─ tests/
│  └─ IntraTeams.HubSmokeTest  net10.0        End-to-end signaling test
└─ verification/                              Build-time harness (see bottom; safe to delete)
```

### Client internals

| File | Responsibility |
|---|---|
| `Signaling/SignalingClient.cs` | SignalR hub connection, exposed as plain .NET events |
| `Rtc/PeerSession.cs` | `RTCPeerConnection` lifecycle, tracks, ICE, SDP |
| `Media/MediaStack.cs` | Mic/speaker, webcam, VP8 encode/decode, source switching |
| `Media/GdiScreenCapture.cs` | Desktop capture via GDI `StretchBlt` + `GetDIBits` |
| `Media/VideoSurface.cs` | Frame → `WriteableBitmap` marshalling for WPF |
| `ViewModels/MainViewModel.cs` | Call state machine, roster, chat, toggles |

---

## Prerequisites

| Machine | Needs |
|---|---|
| Server | .NET 10 runtime (ASP.NET Core), or publish self-contained |
| Client | **.NET 10 SDK**, Windows 10 1809+, a microphone (camera optional) |

Every project targets **.NET 10**. The client's `net10.0-windows10.0.17763.0` is
**forced by the dependency**, not a preference: `SIPSorceryMedia.Windows` 10.x ships that
single target framework and no other. The server and shared library follow it so the
whole solution runs on one runtime — and because a net8.0 project cannot reference a
net10.0 library. See "Version matrix" below before trying to lower any of them.

---

## Build and run

```powershell
# 1. Signaling server (one machine on the LAN)
dotnet run --project src\IntraTeams.Server
#    listens on http://0.0.0.0:5000

# 2. Client (each desktop)
dotnet run --project src\IntraTeams.Client
```

Point clients at the server by editing `src\IntraTeams.Client\appsettings.json`:

```json
{ "ServerUrl": "http://SERVER-HOSTNAME:5000" }
```

or override per-launch:

```powershell
dotnet run --project src\IntraTeams.Client -- --server http://10.20.30.40:5000
```

### Demoing both ends on one machine

Two instances would otherwise show the same Windows username twice:

```powershell
dotnet run --project src\IntraTeams.Client -- --suffix "A"
dotnet run --project src\IntraTeams.Client -- --suffix "B"
```

Use a headset on at least one of them, or the audio will feed back.

### Listing camera device ids

```powershell
dotnet run --project src\IntraTeams.Client -- --list-cameras
```

Then set `CameraDeviceId` in `appsettings.json`.

---

## Firewall

The signaling port is the only fixed one:

| Traffic | Port | Notes |
|---|---|---|
| SignalR | TCP 5000 | Inbound on the server machine only |
| RTP media | **UDP, ephemeral** | Direct between the two clients |

WebRTC picks its own UDP ports at runtime, so a strict environment needs either an
allow rule for `IntraTeams.exe` or a pinned port range. To pin a range, pass a
`PortRange` to the `RTCPeerConnection` constructor in `PeerSession.cs`:

```csharp
_pc = new RTCPeerConnection(configuration, 0, new PortRange(50000, 50100));
```

Windows Defender Firewall prompts on first run — **allow Private networks**. A call that
signals fine (both sides say "Connecting") but never reaches `connected` is almost always
this prompt having been dismissed.

---

## How a call flows

```
Caller                        Hub                        Callee
  │  InviteCall ──────────────►│                            │
  │                            │──── IncomingCall ─────────►│  (rings)
  │                            │◄─── AcceptCall ────────────│
  │◄──── CallAccepted ─────────│                            │
  │  createOffer()             │                            │
  │  SendSdp(offer) ──────────►│──── ReceiveSdp ───────────►│  setRemoteDescription
  │                            │                            │  createAnswer()
  │◄──── ReceiveSdp ───────────│◄─── SendSdp(answer) ───────│
  │  ◄── trickle ICE both ways, relayed by the hub ──►      │
  │                                                          │
  │  ═══════ SRTP audio / video / screen, DIRECT ═══════════ │
```

**Only the caller ever creates an offer.** That single constraint removes glare and all
rollback handling — worth keeping if you extend this.

Once connected, media never returns to the server. The hub is idle for the rest of the call.

---

## Design decisions worth knowing

**One video track, two sources.** The camera and the screen capture feed the *same* VP8
encoder and the same outgoing track. Toggling screen share swaps the frame producer
rather than renegotiating SDP — the single biggest simplification here. A keyframe is
forced on every switch, otherwise the far end shows garbage until the next scheduled one.

**We drive the codec directly.** Rather than using SIPSorcery's higher-level encoder
endpoints, `MediaStack` calls `VP8Codec.EncodeVideo` / `DecodeVideo` itself. That keeps
camera and screen on one encode path and touches much less library surface. Use only
those two: the `EncodeVideoFaster` / `DecodeVideoFaster` overloads on `IVideoEncoder`
throw `NotImplementedException` in `SIPSorcery.VP8`.

**Screen capture is hand-written.** `SIPSorceryMedia.Windows` has **no** screen source —
`WindowsVideoEndPoint` is webcam-only. The alternative was `SIPSorceryMedia.FFmpeg`,
which requires installing FFmpeg 8.1 shared binaries on every desktop. A ~250-line GDI
capture loop avoids that deployment burden entirely.

**Frames are dropped, never queued.** Both the encoder and the WPF surface skip a frame
when the previous one is still in flight. A backlog would appear to the user as steadily
growing latency, which is worse than a dropped frame.

---

## Version matrix

These four packages are mutually constrained. Changing one usually breaks another.

| Package | Version | Why |
|---|---|---|
| `SIPSorcery` | 10.0.16 | Core WebRTC |
| `SIPSorceryMedia.Windows` | 10.0.15 | **net10.0-windows only** — this sets the client TFM |
| `SIPSorcery.VP8` | 10.0.16 | Pure managed VP8, no native binaries |
| `Microsoft.AspNetCore.SignalR.Client` | 8.0.11 | Works fine on net10 |

Do **not** add `SIPSorceryMedia.Encoders` — it is the legacy VP8 package, still compiled
against `SIPSorceryMedia.Abstractions` 8.0.12, and superseded by `SIPSorcery.VP8`.

If you must target `net8.0-windows`, the only coherent set drops the Windows package and
uses `SIPSorceryMedia.FFmpeg` for camera, mic and screen — at the cost of shipping FFmpeg
binaries, and you would need to supply your own audio sink.

---

## Known limitations (it's a POC)

| Area | Limitation |
|---|---|
| Scale | 1-1 only. Group calls need an SFU or a mesh. |
| Server | In-memory roster, single instance. Multiple instances need a Redis backplane. |
| Security | Hub is anonymous over HTTP. Media itself is DTLS-SRTP encrypted. |
| Screen share | Primary monitor only; no monitor picker; **no mouse cursor**. |
| Screen share | GDI cannot capture some DRM-protected or exclusive-fullscreen apps. |
| Video quality | `SIPSorcery.VP8` is self-described experimental with no motion estimation. Fine for a demo; bitrate-inefficient on high-motion screen content. |
| Audio | G.711/G.722. No echo cancellation — **use headsets**. |
| Reconnect | Reconnecting gives a new SignalR connection id; an in-progress call will not survive. |

### Swapping the video codec

If screen-share quality is the blocker, replace `SIPSorcery.VP8` with
`SIPSorceryMedia.FFmpeg` (H.264, far better compression). Touch points are small and
localised: the `using Vpx.Net;` line and the two `VP8Codec` fields in `MediaStack.cs`.
The cost is installing FFmpeg 8.1 shared binaries on every desktop.

---

## Troubleshooting

**"Media connection failed"** — signaling worked, ICE did not. Check that the two machines
can reach each other directly (`ping`, and no client-isolation on the wifi AP), and that
the firewall prompt was allowed. Open **Connection log** in the client; it prints every
ICE state transition and every candidate error.

**No incoming video on an audio call** — expected. Video is negotiated but nothing is sent
until the far end starts their camera or shares their screen.

**Upside-down or skewed screen share** — the DIB is requested top-down with a negative
`biHeight` and rows are padded to 4 bytes. If you change `ScreenShareWidth`, keep it a
multiple of 16; `GdiScreenCapture` rounds for you, but a custom change might not.

**`VP8Codec` does not resolve** — the `using Vpx.Net;` at the top of `MediaStack.cs` is the
one line to correct. That namespace was read from the package source but not compiled
against; if it differs, fix it there and nowhere else.

**Everyone shows as the same person** — two instances on one machine. Use `--suffix`.

**"Video decode: The method or operation is not implemented"** — fixed in `MediaStack.cs`.
`IVideoEncoder` declares four codec methods, but `SIPSorcery.VP8` only implements two of
them; `EncodeVideoFaster` and `DecodeVideoFaster` both throw `NotImplementedException`.
Use `EncodeVideo` and `DecodeVideo` only. Note that `DecodeVideo` **ignores** the
`pixelFormat` argument you pass and always returns tightly packed **BGR**
(it runs `PixelConverter.I420toBGR` internally), so the stride is always `width * 3`.
If the remote picture ever appears with red and blue swapped, that assumption is what
changed.

---

## What was verified, and what was not

Being explicit, because this was built in a Linux environment that could not restore
Windows NuGet packages:

**Verified by execution**
- The server compiles and runs; the SignalR hub negotiates.
- **31/31 end-to-end signaling checks pass** (`tests/IntraTeams.HubSmokeTest`), covering
  registration, roster broadcast, presence transitions, ring/accept/decline, busy
  rejection, SDP relay, ICE relay, sender-spoofing protection, chat echo, hang up, and
  mid-call peer disconnect. Run it with the server up:
  ```powershell
  dotnet run --project tests\IntraTeams.HubSmokeTest -- http://localhost:5000
  ```

**Verified by compilation against transcribed API stubs**
- All non-WPF client code plus `MainViewModel` and `VideoSurface` compile clean against
  the `verification/` harness. This catches C# errors; it does **not** prove the real
  libraries behave as declared.

**Not verified — test these first**
- Actual NuGet restore of the four packages together on Windows.
- The `Vpx.Net` namespace for `VP8Codec`.
- Real audio/video capture, encode, transport and playback.
- XAML compiles as XML but has not been run through the WPF compiler.

The `verification/` folder is a build-time harness, is **not** referenced by
`IntraTeams.sln`, and can be deleted once the solution builds on Windows.
