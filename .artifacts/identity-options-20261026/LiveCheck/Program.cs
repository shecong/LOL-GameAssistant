using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Infrastructure.LeagueClient;

var connection=new LeagueClientConnection();
if(!connection.TryConnect())throw new InvalidOperationException("Local client is unavailable.");
var service=new LcuClientIdentityService(new LcuHttpRequestSender(),Console.WriteLine);
var liveRequests=new LcuHttpRequestSender();
var catalog=await service.ReadAsync();string session=catalog.State.Session;
Console.WriteLine("Catalog: "+string.Join("; ",catalog.Choices.GroupBy(x=>x.Category).Select(g=>$"{g.Key}={g.Count()}")));
try {
    var level=await service.ApplyAsync(session,ClientIdentityCategory.Border,"21");
    Console.WriteLine($"Level border: verified={level.Verified}; level={level.State.BorderLevel}");
    var player=Newtonsoft.Json.Linq.JObject.Parse((await liveRequests.GetStringAsync("/lol-summoner/v1/current-summoner"))!);
    var resolved=Newtonsoft.Json.Linq.JObject.Parse((await liveRequests.GetStringAsync("/lol-regalia/v3/summoners/"+player["summonerId"]+"/regalia"))!);
    Console.WriteLine("Resolved border="+resolved["selectedPrestigeCrest"]);
    if(!level.Verified || level.State.BorderLevel!=500)throw new InvalidOperationException("Level preference did not verify.");
    foreach(var kind in new[]{ClientIdentityCategory.Title,ClientIdentityCategory.Banner,ClientIdentityCategory.Badge}) {
        var selected=catalog.Choices.First(x=>x.Category==kind && x.Owned==false);
        var result=await service.ApplyAsync(session,kind,selected.Id);
        Console.WriteLine($"Unowned {kind}: verified={result.Verified}; code={result.Code}; levelPreserved={result.State.BorderLevel==500}");
        if(result.Code=="session_changed")throw new InvalidOperationException("Identity changed.");
    }
    var preview=await service.GetImageAsync(catalog.Choices.First(x=>x.Category==ClientIdentityCategory.Border && x.Id=="21"));
    Console.WriteLine("Border preview bytes="+(preview?.Length??0));
} finally {
    var restored=await service.RestoreAsync(session);
    Console.WriteLine($"RESTORE verified={restored.Verified}; level={restored.State.BorderLevel}; badges={restored.State.Badges.Count}");
    if(!restored.Verified)throw new InvalidOperationException("Original identity did not verify after restore.");
}
