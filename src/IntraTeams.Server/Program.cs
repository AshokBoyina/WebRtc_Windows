using IntraTeams.Server.Hubs;
using IntraTeams.Server.Services;
using IntraTeams.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<UserRegistry>();

builder.Services.AddSignalR(options =>
{
    // SDP blobs are comfortably larger than the 32 KB default once a few
    // codecs and ICE candidates are in play.
    options.MaximumReceiveMessageSize = 256 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Intranet POC: allow any origin so you can point clients at the server by IP
// or hostname without fighting CORS. Tighten this before any wider rollout.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

app.UseCors();

app.MapHub<SignalingHub>(HubRoutes.Signaling);

// Trivial liveness endpoint so you can confirm reachability from a client
// machine with a browser before launching the WPF app.
app.MapGet("/", () => Results.Ok(new
{
    service = "IntraTeams signaling server",
    hub = HubRoutes.Signaling,
    utc = DateTimeOffset.UtcNow
}));

app.Run();
