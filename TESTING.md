# IntraTeams — step-by-step test plan

Work through these **in order**. Each phase depends on the one before it, and they are
deliberately sequenced so the riskiest, cheapest-to-check things fail first. Do not jump
to "make a video call" — if something is broken you will not know which layer it was.

Total time: about 30 minutes for phases 0–8 on one machine.

---

## Phase 0 — Prerequisites (2 min)

```powershell
dotnet --list-sdks
```

You need a **10.x** entry. The WPF client targets `net10.0-windows10.0.17763.0` because
`SIPSorceryMedia.Windows` ships no other target framework.

If 10.x is missing, install the .NET 10 SDK from https://dotnet.microsoft.com/download
and reopen your terminal.

```powershell
cd C:\Project\WebRtc_NoICE
dir
```

Expect `IntraTeams.sln`, `src`, `tests`, `verification`, `README.md`.

- [ ] .NET 10 SDK present
- [ ] Solution files present

---

## Phase 1 — Does it build? (5 min) ← **highest risk, do this first**

The four NuGet packages have never been restored together on a Windows machine, so this
is the phase most likely to need a fix.

```powershell
cd C:\Project\WebRtc_NoICE
dotnet restore IntraTeams.sln
```

Then build:

```powershell
dotnet build IntraTeams.sln -c Debug
```

**Expected:** `Build succeeded. 0 Error(s)`

### If it fails, in likely order

**`error CS0246: The type or namespace name 'Vpx' could not be found`**

The single most likely failure. The `using Vpx.Net;` at the top of
`src\IntraTeams.Client\Media\MediaStack.cs` was transcribed from package source, not
compiled against. To find the real namespace:

```powershell
dotnet build src\IntraTeams.Client 2>&1 | Select-String "VP8"
# then inspect the package:
dir $env:USERPROFILE\.nuget\packages\sipsorcery.vp8\10.0.16\lib
```

Open the DLL in ILSpy or dotPeek, find `VP8Codec`, and correct that one `using`. Nothing
else in the codebase references it.

**`error NU1102: Unable to find package ... version`**

A version has moved. Check the current one and update `IntraTeams.Client.csproj`:

```powershell
dotnet list package --outdated
```

**`error NU1201: Project ... is not compatible with net10.0-windows`**

The .NET 10 SDK is not installed — go back to Phase 0.

- [ ] `dotnet restore` succeeds
- [ ] `dotnet build` succeeds with 0 errors

---

## Phase 2 — Server starts (2 min)

```powershell
dotnet run --project src\IntraTeams.Server
```

Expect:

```
Now listening on: http://0.0.0.0:5000
Application started. Press Ctrl+C to shut down.
```

In a browser, open **http://localhost:5000** — you should get JSON:

```json
{"service":"IntraTeams signaling server","hub":"/hubs/signaling","utc":"..."}
```

**Leave this terminal running** for every phase from here on.

- [ ] Server listening on 5000
- [ ] Health endpoint returns JSON

---

## Phase 3 — Signaling layer, no UI and no media (2 min)

This is the automated test. It drives two simulated peers through the entire call
protocol over raw WebSockets. It proves the hub works before any media is involved.

In a **second** terminal:

```powershell
cd C:\Project\WebRtc_NoICE
dotnet run --project tests\IntraTeams.HubSmokeTest -- http://localhost:5000
```

**Expect exactly this ending:**

```
31/31 checks passed
SIGNALING LAYER OK
```

It covers registration, roster broadcast, presence transitions, ring/accept, busy
rejection, SDP relay, ICE relay, sender-spoofing protection, chat echo, hang up, and
mid-call peer disconnect.

If this fails, **stop and fix it here** — nothing above this layer can work.

- [ ] 31/31 checks passed

---

## Phase 4 — Presence with two clients (5 min)

Build once, then launch the executable twice. Do **not** use `dotnet run` twice — the
second invocation fights the first for the build output.

```powershell
dotnet build src\IntraTeams.Client -c Debug
cd src\IntraTeams.Client\bin\Debug\net10.0-windows10.0.17763.0
```

Launch two windows (each in its own terminal, or use `start`):

```powershell
start IntraTeams.exe --suffix A
start IntraTeams.exe --suffix B
```

The `--suffix` matters: both instances read the same Windows username, so without it you
cannot tell the two rows apart.

**Check in each window:**

| What | Expected |
|---|---|
| Top-left | Your name, e.g. `ashok (DESK-01) [A]` |
| Status line | `Connected. Pick someone to call.` |
| People list | The *other* instance, with a green dot and "Available" |
| No red banner | A red banner means the server is unreachable |
| Server terminal | Two `Registered ...` log lines |

Now expand **Connection log** at the bottom of a client. You should see
`Registered as ...`.

**Test roster cleanup:** close window B. Window A's people list should empty within a
second or two. Reopen B and it should reappear.

- [ ] Both clients connect
- [ ] Each sees the other with a green Available dot
- [ ] Closing one removes it from the other's list

---

## Phase 5 — Chat (2 min) ← cheapest real feature

Still with two windows open, no call in progress.

1. In window A, click the person in the list to select them.
2. Type in the chat box on the right, press **Enter**.

**Expect:** the message appears in *both* windows — right-aligned and purple in A (yours),
left-aligned and grey in B. Timestamps on both.

Reply from B and confirm it arrives in A.

- [ ] Message reaches the other client
- [ ] Sender sees their own echo, right-aligned

---

## Phase 6 — Audio call (5 min)

> **Plug in a headset first.** There is no echo cancellation in this POC. Two speaker-and-
> mic instances on one desk will produce a feedback howl within a second.

1. In window A, select the other user.
2. Click **Audio call**.
3. Window B shows the full-screen incoming-call overlay with the caller's name.
4. Click **Accept**.

**Expect within ~2 seconds:** both status lines read `In a call with ...`, and the button
row changes to Mute / Start video / Share screen / Hang up.

### Read the Connection log — this is the real test

Expand **Connection log** and look for this sequence:

```
Inviting ashok (DESK-01) [B] (video=False)
ICE gathering state: gathering
ICE gathering state: complete
Offer sent.
Applied remote answer.
Audio negotiated: PCMU        (or G722)
ICE connection state: checking
ICE connection state: connected
Peer connection state: connected
```

**`Peer connection state: connected` is the moment media is flowing.** If you see
`checking` and it never reaches `connected`, jump to Troubleshooting below.

### Then verify

| Test | Expected |
|---|---|
| Speak into the mic | Heard in the other window |
| Click **Mute** | Button reads "Unmute"; far end hears silence |
| Click **Unmute** | Audio returns |
| Click **Hang up** | Both return to idle, both people show green/Available again |

- [ ] Reaches `Peer connection state: connected`
- [ ] Audio audible both directions
- [ ] Mute silences the far end
- [ ] Hang up cleans up both sides

---

## Phase 7 — Video call (3 min)

First confirm the camera is visible to the app:

```powershell
IntraTeams.exe --list-cameras
```

A dialog lists device names and ids. If it says none were found, video will not work —
set `CameraDeviceId` in `appsettings.json` if you have several and want a specific one.

Then: select the user, click **Video call**, accept on the other side.

| What | Expected |
|---|---|
| Large area | The far end's camera |
| Small box, bottom-right | Your own camera, labelled "You" |
| **Stop video** | Your preview clears; far end's large area goes to "No incoming video" |
| **Start video** | Comes back within a second or two |

Expect `Video negotiated: VP8` in the Connection log.

> First frames may be blocky for a moment. That is VP8 waiting for a keyframe and is
> normal. If it *stays* blocky on a still image, note it — see the video-quality
> limitation in README.md.

- [ ] Remote video renders
- [ ] Local preview renders
- [ ] Camera toggles off and on

---

## Phase 8 — Screen share (3 min)

While in a call, click **Share screen**.

| What | Expected |
|---|---|
| Far end's large area | Your desktop |
| Your local preview | Your desktop (so you can see what you are sharing) |
| Button | Now reads "Stop sharing" |
| Camera | Switched off automatically — one video track, one source at a time |

Move a window around and confirm the far end updates at roughly 8 fps (the configured
`ScreenShareFps`).

**Known and expected:** primary monitor only, and **your mouse cursor will not appear**.
Both are documented POC limitations, not bugs.

Click **Start video** to switch back to camera — it should swap cleanly without dropping
the call. That swap not requiring renegotiation is the main design bet in this POC, so
it is worth confirming explicitly.

- [ ] Desktop visible at the far end
- [ ] Screen ⇄ camera switching works without dropping the call

---

## Phase 9 — Two real machines on the LAN (10 min)

Everything so far was loopback, which cannot fail on ICE. This phase is the one that
actually tests your intranet assumption.

### 9a. On the server machine

Open the signaling port (admin PowerShell):

```powershell
New-NetFirewallRule -DisplayName "IntraTeams Signaling" -Direction Inbound `
  -Protocol TCP -LocalPort 5000 -Action Allow -Profile Private
```

Find its address:

```powershell
ipconfig | findstr IPv4
```

### 9b. On each client machine

Point at the server — edit `appsettings.json` next to the exe, or pass it in:

```powershell
IntraTeams.exe --server http://10.20.30.40:5000
```

Confirm reachability **before** launching the client:

```powershell
Test-NetConnection 10.20.30.40 -Port 5000
```

`TcpTestSucceeded : True` is required. If it is False, the problem is the firewall or
routing, not the app.

Allow media through the firewall (admin PowerShell, adjust the path):

```powershell
New-NetFirewallRule -DisplayName "IntraTeams Media" -Direction Inbound `
  -Program "C:\Project\WebRtc_NoICE\src\IntraTeams.Client\bin\Debug\net10.0-windows10.0.17763.0\IntraTeams.exe" `
  -Action Allow -Profile Private
```

WebRTC picks ephemeral UDP ports, which is why the rule is per-program rather than
per-port. To pin a range instead, see the Firewall section of README.md.

### 9c. Run the whole of phases 4–8 again, machine to machine

This is the real acceptance test. Repeat presence, chat, audio, video, screen share.

- [ ] `Test-NetConnection` succeeds from both clients
- [ ] Both machines appear in each other's people list
- [ ] Audio call reaches `connected`
- [ ] Video and screen share work across the network

---

## Phase 10 — Edge cases (5 min)

| Test | How | Expected |
|---|---|---|
| Decline | A calls B, B clicks **Decline** | A: "… declined.", both idle |
| Busy | A and B in a call; C calls B | C: "… is already in a call." |
| Caller cancels | A calls B, A hits **Hang up** while ringing | B's ringing overlay disappears |
| Crash mid-call | In a call, kill one client from Task Manager | Survivor: "… disconnected.", returns to idle within seconds |
| Server restart | Restart the server mid-call | Clients show the reconnect banner, then re-register. **The call will not survive** — reconnect gives a new connection id. Documented limitation. |
| Self-call | Try to call yourself | Not possible — you are filtered out of your own roster |

- [ ] Decline, busy, cancel all behave
- [ ] Killed client does not leave the survivor hanging

---

## Troubleshooting

### Signals fine, but never reaches `connected`

The log shows `ICE connection state: checking` and stops. Media cannot get through.
In order of likelihood:

1. **The Windows Firewall prompt was dismissed.** By far the most common cause. Add the
   per-program rule from Phase 9b.
2. **Wireless client isolation.** Many corporate/guest APs block client-to-client traffic
   entirely. Test both machines on wired ethernet to confirm.
3. **The wrong NIC was advertised.** Look for `candidate:` lines in the log. If the only
   addresses shown are `172.x` (Docker), `192.168.56.x` (VirtualBox) or a VPN adapter,
   that is the problem. Disable the adapter, or pin the right one in
   `Rtc\PeerSession.cs`:
   ```csharp
   X_BindAddress = IPAddress.Parse("10.20.30.41")
   ```
4. **VPN split-tunnelling.** A VPN client will happily capture LAN traffic. Disconnect it
   and retest.

### No audio, but the call connects

Check the Windows sound mixer for per-app volume; confirm the right default recording
device; look for `Microphone:` or `Speaker:` errors in the Connection log.

### Video is very blocky or laggy on screen share

Expected to a degree — the pure-managed VP8 encoder has no motion estimation. Lower the
load in `appsettings.json`:

```json
{ "ScreenShareWidth": 960, "ScreenShareFps": 5 }
```

If it is still not good enough, README.md has the FFmpeg/H.264 swap.

### Red banner: cannot reach the signaling server

The client never connected. Verify `Test-NetConnection <server> -Port 5000` from that
machine, and check `ServerUrl` — `localhost` in `appsettings.json` will only ever work on
the server machine itself.

---

## Quick reference

```powershell
# server
dotnet run --project src\IntraTeams.Server

# automated signaling test
dotnet run --project tests\IntraTeams.HubSmokeTest -- http://localhost:5000

# two local clients
cd src\IntraTeams.Client\bin\Debug\net10.0-windows10.0.17763.0
start IntraTeams.exe --suffix A
start IntraTeams.exe --suffix B

# remote server / camera list
IntraTeams.exe --server http://10.20.30.40:5000
IntraTeams.exe --list-cameras
```
