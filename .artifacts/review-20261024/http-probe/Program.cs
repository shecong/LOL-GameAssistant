using System.Diagnostics;
using System.Net;
using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;

string config = Path.GetFullPath(".artifacts/review-20261024/http-probe/source.json");
await File.WriteAllTextAsync(config,"{\"serverBaseUrl\":\"https://local-test.invalid/\"}");
using var client = new HttpClient(new HeadersOnly()) { Timeout = TimeSpan.FromMilliseconds(30) };
var fallback = new Fallback();
var service = new ServerUpdateReleaseService(client,fallback,config);
using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
var watch = Stopwatch.StartNew();
try { await service.GetLatestAsync(caller.Token); }
catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
Console.WriteLine($"F05 HttpClient.Timeout=30ms, operation elapsed={watch.ElapsedMilliseconds}ms, stopped by caller={caller.IsCancellationRequested}, fallback calls={fallback.Calls}");
if (!caller.IsCancellationRequested || fallback.Calls != 0) throw new Exception("Not reproduced");
var lcu = new RuneClient();
string snapshots = Path.GetFullPath(".artifacts/review-20261024/http-probe/snapshots-" + Guid.NewGuid().ToString("N"));
var builds = new VerifiedBuildApplyService(new OpggBuildApplyService(lcu,new Catalog()),lcu,snapshots);
var option = new OpggBuildOption(1,[],[3006,3031,3094],[],8000,8200,[8005,8009,9104,8014,8234,8236],100,50);
var first = await builds.ApplyBuildAsync(1,"TOP",option,allowReplaceCurrentRunePage:true,applySummonerSpells:false,applyItemSet:false);
var second = await builds.ApplyBuildAsync(2,"TOP",option with { RunePerkIds=[8008,8009,9104,8014,8234,8236] },allowReplaceCurrentRunePage:true,applySummonerSpells:false,applyItemSet:false);
var undo = await builds.RestoreLastConfigurationAsync();
var backup = await File.ReadAllTextAsync(Directory.GetFiles(snapshots,"*.json").Single());
Console.WriteLine($"F08 full-page overwrite, two automatic calls: {first.Succeeded}/{second.Succeeded}; undo={undo.Succeeded}; restored page={lcu.Page.Value<string>("name")}; original present in sole snapshot={backup.Contains("USER ORIGINAL")}");
if (!first.Succeeded || !second.Succeeded || !undo.Succeeded || backup.Contains("USER ORIGINAL")) throw new Exception("Not reproduced");
sealed class Fallback : IUpdateReleaseService {
 public int Calls;
 public Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken=default) { Calls++; return Task.FromResult<UpdateRelease?>(null); }
}
sealed class HeadersOnly : HttpMessageHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new Stalled())});
}
sealed class Stalled : Stream {
 public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false;
 public override long Length=>throw new NotSupportedException(); public override long Position {get=>0;set=>throw new NotSupportedException();}
 public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default) {await Task.Delay(Timeout.Infinite,token);return 0;}
 public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException(); public override void Flush(){}
 public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException(); public override void SetLength(long value)=>throw new NotSupportedException(); public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
sealed class Catalog : IChampionCatalog {
 public string GetDisplayName(int id)=>"Champion"+id; public IReadOnlyList<ChampionReference> GetAll()=>[]; public int? FindIdByDisplayName(string? name)=>null;
}
sealed class RuneClient : ILcuRequestSender {
 public JObject Page=JObject.Parse("""{"id":1,"name":"USER ORIGINAL","current":true,"isEditable":true,"primaryStyleId":8000,"subStyleId":8200,"selectedPerkIds":[1,2,3,4,5,6]}""");
 public Task<string?> GetStringAsync(string route,CancellationToken cancellationToken=default)=>Task.FromResult<string?>(route switch {
  "/lol-summoner/v1/current-summoner"=>"{\"puuid\":\"test\",\"summonerId\":42}",
  "/lol-gameflow/v1/gameflow-phase"=>"\"Lobby\"",
  "/lol-perks/v1/pages"=>new JArray(Page.DeepClone()).ToString(),
  "/lol-perks/v1/currentpage"=>Page.ToString(),
  _=>"{\"itemSets\":[]}"
 });
 public Task<byte[]?> GetBytesAsync(string route,CancellationToken cancellationToken=default)=>Task.FromResult<byte[]?>(null);
 public Task<bool> PostAsync(string route,string body,CancellationToken cancellationToken=default)=>Task.FromResult(false);
 public Task<bool> PutAsync(string route,string body,CancellationToken cancellationToken=default){if(route.EndsWith("/pages/1")){Page=JObject.Parse(body);Page["id"]=1;}return Task.FromResult(true);}
 public Task<bool> PatchAsync(string route,string body,CancellationToken cancellationToken=default)=>Task.FromResult(true);
 public Task<bool> DeleteAsync(string route,CancellationToken cancellationToken=default)=>Task.FromResult(true);
}
