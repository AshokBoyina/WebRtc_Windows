// Minimal SignalR JSON-protocol client written directly on System.Net.WebSockets.
//
// This exists so the signaling hub can be exercised end to end without pulling
// in Microsoft.AspNetCore.SignalR.Client. It drives two simulated peers through
// the exact call flow the WPF client uses: register, roster, invite, accept,
// SDP offer/answer relay, ICE trickle, chat, hang up.
//
// Usage: dotnet run --project tests/IntraTeams.HubSmokeTest -- http://localhost:5000

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var baseUrl = (args.Length > 0 ? args[0] : "http://localhost:5000").TrimEnd('/');
var failures = 0;
var checks = 0;

void Check(bool condition, string description)
{
    checks++;
    if (condition)
    {
        Console.WriteLine($"  PASS  {description}");
    }
    else
    {
        failures++;
        Console.WriteLine($"  FAIL  {description}");
    }
}

Console.WriteLine($"IntraTeams hub smoke test against {baseUrl}");
Console.WriteLine();

await using var alice = await Peer.ConnectAsync(baseUrl, "alice", "DESK-A");
await using var bob = await Peer.ConnectAsync(baseUrl, "bob", "DESK-B");

// ---------------------------------------------------------------- 1. register

Console.WriteLine("1. Registration and roster");

var aliceRoster = await alice.InvokeAsync("Register", alice.UserName, alice.MachineName);
Check(aliceRoster.ValueKind == JsonValueKind.Array, "Register returns a roster array");

var bobRoster = await bob.InvokeAsync("Register", bob.UserName, bob.MachineName);
Check(bobRoster.GetArrayLength() == 2, $"Bob's roster contains both users (got {bobRoster.GetArrayLength()})");

// Alice gets a roster push from her own registration as well as Bob's, so
// wait for the snapshot that actually contains both users.
var rosterPush = await alice.WaitForAsync("RosterChanged", TimeSpan.FromSeconds(5),
    args => args[0].GetArrayLength() == 2);
Check(rosterPush is not null, "Alice received a RosterChanged push when Bob joined");

if (rosterPush is not null)
{
    var names = rosterPush[0].EnumerateArray()
        .Select(u => u.GetProperty("displayName").GetString())
        .ToList();
    Check(names.Contains("alice (DESK-A)") && names.Contains("bob (DESK-B)"),
        $"Roster display names are correct: [{string.Join(", ", names)}]");
    Check(rosterPush[0].EnumerateArray().All(u => u.GetProperty("presence").GetInt32() == 0),
        "Both users start with presence Available");
}

// ------------------------------------------------------------------ 2. invite

Console.WriteLine();
Console.WriteLine("2. Call invite and accept");

const string callId = "call-0001";
await alice.SendAsync("InviteCall", bob.ConnectionId, callId, true);

var incoming = await bob.WaitForAsync("IncomingCall", TimeSpan.FromSeconds(5));
Check(incoming is not null, "Bob received IncomingCall");

if (incoming is not null)
{
    var invite = incoming[0];
    Check(invite.GetProperty("callId").GetString() == callId, "Invite carries the correct CallId");
    Check(invite.GetProperty("fromConnectionId").GetString() == alice.ConnectionId,
        "Invite carries Alice's real connection id");
    Check(invite.GetProperty("fromDisplayName").GetString() == "alice (DESK-A)",
        "Invite carries Alice's display name");
    Check(invite.GetProperty("withVideo").GetBoolean(), "Invite preserves the WithVideo flag");
}

await bob.SendAsync("AcceptCall", alice.ConnectionId, callId);

var accepted = await alice.WaitForAsync("CallAccepted", TimeSpan.FromSeconds(5));
Check(accepted is not null, "Alice received CallAccepted");
Check(accepted?[0].GetString() == bob.ConnectionId, "CallAccepted identifies Bob");

var busyRoster = await alice.WaitForAsync("RosterChanged", TimeSpan.FromSeconds(5),
    args => args[0].EnumerateArray().All(u => u.GetProperty("presence").GetInt32() == 1));
Check(busyRoster is not null, "Both users flip to presence InCall after accept");

// --------------------------------------------------------------- 3. SDP relay

Console.WriteLine();
Console.WriteLine("3. SDP and ICE relay");

await alice.SendAsync("SendSdp", new
{
    callId,
    fromConnectionId = "spoofed-value-should-be-overwritten",
    toConnectionId = bob.ConnectionId,
    type = "offer",
    sdp = "v=0\r\no=- 1 1 IN IP4 192.168.1.10\r\n"
});

var offer = await bob.WaitForAsync("ReceiveSdp", TimeSpan.FromSeconds(5));
Check(offer is not null, "Bob received the SDP offer");
Check(offer?[0].GetProperty("type").GetString() == "offer", "Offer type survives the relay");
Check(offer?[0].GetProperty("sdp").GetString()?.StartsWith("v=0") == true, "Offer SDP body survives the relay");
Check(offer?[0].GetProperty("fromConnectionId").GetString() == alice.ConnectionId,
    "Server overwrites a spoofed FromConnectionId with the true sender");

await bob.SendAsync("SendSdp", new
{
    callId,
    toConnectionId = alice.ConnectionId,
    type = "answer",
    sdp = "v=0\r\no=- 2 2 IN IP4 192.168.1.11\r\n"
});

var answer = await alice.WaitForAsync("ReceiveSdp", TimeSpan.FromSeconds(5));
Check(answer?[0].GetProperty("type").GetString() == "answer", "Alice received the SDP answer");

await alice.SendAsync("SendIceCandidate", new
{
    callId,
    toConnectionId = bob.ConnectionId,
    candidate = "candidate:1 1 udp 2130706431 192.168.1.10 54321 typ host",
    sdpMid = "0",
    sdpMLineIndex = 0,
    usernameFragment = "abcd"
});

var ice = await bob.WaitForAsync("ReceiveIceCandidate", TimeSpan.FromSeconds(5));
Check(ice is not null, "Bob received the ICE candidate");
Check(ice?[0].GetProperty("candidate").GetString()?.Contains("typ host") == true,
    "Candidate is a host candidate and passes through unmodified");
Check(ice?[0].GetProperty("sdpMid").GetString() == "0", "Candidate sdpMid survives the relay");

// -------------------------------------------------------------------- 4. chat

Console.WriteLine();
Console.WriteLine("4. Text chat");

await alice.SendAsync("SendChat", bob.ConnectionId, "can you see my screen?");

var bobChat = await bob.WaitForAsync("ReceiveChat", TimeSpan.FromSeconds(5));
var aliceEcho = await alice.WaitForAsync("ReceiveChat", TimeSpan.FromSeconds(5));

Check(bobChat?[0].GetProperty("text").GetString() == "can you see my screen?", "Bob received the chat message");
Check(aliceEcho is not null, "Alice received an echo of her own message");
Check(aliceEcho?[0].GetProperty("fromDisplayName").GetString() == "alice (DESK-A)",
    "Chat echo carries the sender display name");

// ----------------------------------------------------------------- 5. hang up

Console.WriteLine();
Console.WriteLine("5. Hang up");

await alice.SendAsync("EndCall", bob.ConnectionId, callId, "hungup");

var ended = await bob.WaitForAsync("CallEnded", TimeSpan.FromSeconds(5));
Check(ended is not null, "Bob received CallEnded");
Check(ended?[1].GetString() == "hungup", "CallEnded carries the reason");

var freeRoster = await alice.WaitForAsync("RosterChanged", TimeSpan.FromSeconds(5),
    args => args[0].EnumerateArray().All(u => u.GetProperty("presence").GetInt32() == 0));
Check(freeRoster is not null, "Both users return to presence Available after hang up");

// ------------------------------------------------------- 6. busy and teardown

Console.WriteLine();
Console.WriteLine("6. Busy signal and disconnect cleanup");

await alice.SendAsync("InviteCall", bob.ConnectionId, "call-0002", false);
await bob.WaitForAsync("IncomingCall", TimeSpan.FromSeconds(5));
await bob.SendAsync("AcceptCall", alice.ConnectionId, "call-0002");
await alice.WaitForAsync("CallAccepted", TimeSpan.FromSeconds(5));

await using (var carol = await Peer.ConnectAsync(baseUrl, "carol", "DESK-C"))
{
    await carol.InvokeAsync("Register", carol.UserName, carol.MachineName);
    await carol.SendAsync("InviteCall", bob.ConnectionId, "call-0003", false);

    var rejected = await carol.WaitForAsync("CallRejected", TimeSpan.FromSeconds(5));
    Check(rejected is not null, "Carol calling a busy Bob gets CallRejected");
    Check(rejected?[1].GetString() == "busy", "Rejection reason is 'busy'");
}

// Alice drops without hanging up; Bob must be told.
await alice.AbortAsync();

var orphaned = await bob.WaitForAsync("CallEnded", TimeSpan.FromSeconds(8));
Check(orphaned is not null, "Bob is notified when Alice's connection drops mid-call");
Check(orphaned?[1].GetString() == "peer-disconnected", "Drop reason is 'peer-disconnected'");

// ------------------------------------------------------------------- summary

Console.WriteLine();
Console.WriteLine(new string('-', 60));
Console.WriteLine($"{checks - failures}/{checks} checks passed");
Console.WriteLine(failures == 0 ? "SIGNALING LAYER OK" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;


/// <summary>One simulated client: a WebSocket speaking the SignalR JSON protocol.</summary>
sealed class Peer : IAsyncDisposable
{
    private const char Rs = '\u001e';

    private readonly ClientWebSocket _socket = new();
    private readonly List<(string Target, JsonElement[] Args)> _inbox = new();
    private readonly SemaphoreSlim _inboxSignal = new(0);
    private readonly Dictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextInvocationId;

    public string UserName { get; }
    public string MachineName { get; }
    public string ConnectionId { get; private set; } = "";

    private Peer(string userName, string machineName)
    {
        UserName = userName;
        MachineName = machineName;
    }

    public static async Task<Peer> ConnectAsync(string baseUrl, string userName, string machineName)
    {
        var peer = new Peer(userName, machineName);

        // Bypass any ambient proxy: this is a loopback call.
        using var handler = new HttpClientHandler { UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var negotiateUrl = $"{baseUrl}/hubs/signaling/negotiate?negotiateVersion=1";
        var negotiateJson = await http.PostAsync(negotiateUrl, new StringContent(""))
            .ContinueWith(t => t.Result.Content.ReadAsStringAsync()).Unwrap();

        using var negotiate = JsonDocument.Parse(negotiateJson);
        peer.ConnectionId = negotiate.RootElement.GetProperty("connectionId").GetString()!;
        var token = negotiate.RootElement.GetProperty("connectionToken").GetString()!;

        var wsUrl = baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        peer._socket.Options.Proxy = null;
        await peer._socket.ConnectAsync(
            new Uri($"{wsUrl}/hubs/signaling?id={token}"), CancellationToken.None);

        _ = peer.ReceiveLoopAsync();

        // SignalR handshake.
        await peer.SendRawAsync("{\"protocol\":\"json\",\"version\":1}");
        await Task.Delay(250);

        return peer;
    }

    private async Task SendRawAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + Rs);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>Fire and forget hub invocation (SignalR "Send").</summary>
    public Task SendAsync(string target, params object[] arguments)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            type = 1,
            target,
            arguments
        }));

    /// <summary>Hub invocation that awaits a return value.</summary>
    public async Task<JsonElement> InvokeAsync(string target, params object[] arguments)
    {
        var id = (++_nextInvocationId).ToString();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) { _pending[id] = tcs; }

        await SendRawAsync(JsonSerializer.Serialize(new
        {
            type = 1,
            invocationId = id,
            target,
            arguments
        }));

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (completed != tcs.Task)
        {
            throw new TimeoutException($"No completion for '{target}'.");
        }

        return tcs.Task.Result;
    }

    /// <summary>
    /// Waits for a server-to-client callback with the given name. When a
    /// predicate is supplied, non-matching messages of the same name are
    /// discarded, which is what you want for state pushes like RosterChanged
    /// where only the newest snapshot is meaningful.
    /// </summary>
    public async Task<JsonElement[]?> WaitForAsync(
        string target, TimeSpan timeout, Func<JsonElement[], bool>? predicate = null)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            lock (_inbox)
            {
                var index = _inbox.FindIndex(m => m.Target == target && (predicate is null || predicate(m.Args)));
                if (index >= 0)
                {
                    var args = _inbox[index].Args;
                    // Drop this message and every earlier message of the same
                    // name, so a later wait cannot match a stale snapshot.
                    _inbox.RemoveRange(0, index + 1);
                    return args;
                }
            }

            await Task.WhenAny(_inboxSignal.WaitAsync(_cts.Token), Task.Delay(100));
        }

        return null;
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        var pendingText = new StringBuilder();

        try
        {
            while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                pendingText.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                var text = pendingText.ToString();
                var parts = text.Split(Rs);

                // The final element is either empty or a partial frame; keep it.
                pendingText.Clear();
                pendingText.Append(parts[^1]);

                for (var i = 0; i < parts.Length - 1; i++)
                {
                    HandleFrame(parts[i]);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private void HandleFrame(string frame)
    {
        if (string.IsNullOrWhiteSpace(frame))
        {
            return;
        }

        JsonDocument document;
        try { document = JsonDocument.Parse(frame); }
        catch (JsonException) { return; }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeProperty))
            {
                return; // handshake response
            }

            var type = typeProperty.GetInt32();

            if (type == 1 && root.TryGetProperty("target", out var target))
            {
                var args = root.TryGetProperty("arguments", out var argumentsElement)
                    ? argumentsElement.EnumerateArray().Select(a => a.Clone()).ToArray()
                    : Array.Empty<JsonElement>();

                lock (_inbox) { _inbox.Add((target.GetString()!, args)); }
                _inboxSignal.Release();
            }
            else if (type == 3 && root.TryGetProperty("invocationId", out var invocationId))
            {
                var id = invocationId.GetString()!;
                TaskCompletionSource<JsonElement>? tcs;
                lock (_pending)
                {
                    _pending.TryGetValue(id, out tcs);
                    _pending.Remove(id);
                }

                var value = root.TryGetProperty("result", out var resultElement)
                    ? resultElement.Clone()
                    : default;

                tcs?.TrySetResult(value);
            }
            else if (type == 6)
            {
                // Ping. Echo it so the server does not time us out.
                _ = SendRawAsync("{\"type\":6}");
            }
        }
    }

    /// <summary>Kills the socket without a clean close, simulating a crashed client.</summary>
    public Task AbortAsync()
    {
        _socket.Abort();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
        }
        catch { /* best effort */ }

        _socket.Dispose();
        _cts.Dispose();
    }
}
