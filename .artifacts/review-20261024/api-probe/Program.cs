using System.Collections;
using System.Reflection;
using LOL.Lounge;
using LOL_GameApi.Lounge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
    ["Lounge:DatabasePath"] = Path.GetFullPath(".artifacts/review-20261024/api-probe/" + Guid.NewGuid().ToString("N") + ".db"),
    ["Lounge:MaxConnections"] = "2"
});
builder.AddLounge();
builder.Services.AddRateLimiter(o => o.AddPolicy("lounge-connect", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("local-test")));
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapLounge();
await app.StartAsync();
string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
HubConnection Client(char c) => new HubConnectionBuilder().WithUrl(url + "/hubs/lounge", o => o.AccessTokenProvider = () => Task.FromResult<string?>(new string(c,64))).Build();
await using var alice = Client('a'); await using var bob = Client('b');
await alice.StartAsync(); await bob.StartAsync();
var claimed = new LoungePresence("synthetic-puuid", "Test Alice", "TEST", "test-region", "Lobby");
await alice.InvokeAsync("UpdatePresence", claimed);
var post = await alice.InvokeAsync<LoungeMessage>("PublishRecruitment", new LoungeSend(Guid.NewGuid().ToString("N"), "", "recruit", Recruitment:new("test-region",420,"不限段位","任意位置",1)));
await bob.InvokeAsync("UpdatePresence", claimed);
var closed = await bob.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment",post.Id,"close");
Console.WriteLine($"F02 other installation closes owner's recruitment: {closed.Closed}");
if (!closed.Closed) throw new Exception("Not reproduced");

var room = app.Services.GetRequiredService<LoungeRoom>();
var connections = (IDictionary)typeof(LoungeRoom).GetField("_connections",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(room)!;
foreach (var value in connections.Values) value!.GetType().GetProperty("Seen")!.SetValue(value,DateTimeOffset.UtcNow.AddMinutes(-2));
var stale = room.StaleConnections();
await using var third = Client('c'); await third.StartAsync();
await alice.InvokeAsync<LoungeSnapshot>("Sync",0L);
await bob.InvokeAsync<LoungeSnapshot>("Sync",0L);
Console.WriteLine($"F03 removed presence={stale.Length}; all 3 transports remain Connected with MaxConnections=2: {alice.State}/{bob.State}/{third.State}");

var store = app.Services.GetRequiredService<LoungeStore>();
for (int i=0;i<100;i++) await store.AppendAsync("gap:"+i,"","test","test","test","chat","message");
var replay = await store.ReadAsync(post.Id);
var reconnect = await store.ReadAsync(0);
Console.WriteLine($"F04 reconnect resets cursor: expected replay={replay.Length}, returned latest={reconnect.Length}, skipped={replay.Length-reconnect.Length}");
await third.StopAsync(); await bob.StopAsync(); await alice.StopAsync(); await app.StopAsync();
