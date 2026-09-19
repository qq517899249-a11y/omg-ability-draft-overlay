using System.Net;
using System.Text.Json;

namespace OmgAd;

public sealed record SyncProgress(string Message,int Step,int Total);

public static class WindrunSync
{
    const string Base="https://api.windrun.io/api/v2/";
    static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(45)};

    public static async Task<Statistics> Download(IProgress<SyncProgress>? progress=null,CancellationToken cancel=default)
    {
        string[] paths=["static/abilities","static/heroes","static/patches","abilities","ability-high-skill","ability-pairs","ability-triplets"];
        var docs=new Dictionary<string,JsonDocument>();
        try
        {
            for(int i=0;i<paths.Length;i++)
            {
                progress?.Report(new($"Windrun：正在下载 {paths[i]}（{i+1}/{paths.Length}）",i+1,paths.Length));
                docs[paths[i]]=await Get(paths[i],cancel);
                if(i+1<paths.Length)await Task.Delay(900,cancel);
            }
            return Transform(docs);
        }
        finally{foreach(var d in docs.Values)d.Dispose();}
    }

    static async Task<JsonDocument> Get(string path,CancellationToken cancel)
    {
        for(int attempt=0;;attempt++)
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,Base+path);
            req.Headers.Accept.ParseAdd("application/json");
            req.Headers.UserAgent.ParseAdd("OMG-AD-Overlay/0.5 (+local desktop client)");
            using var res=await Http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,cancel);
            if(res.IsSuccessStatusCode)return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancel),cancellationToken:cancel);
            if(attempt<5&&(res.StatusCode==HttpStatusCode.ServiceUnavailable||(int)res.StatusCode==429))
            {
                var delay=res.Headers.RetryAfter?.Delta??TimeSpan.FromSeconds(Math.Min(30,2<<attempt));
                await Task.Delay(delay,cancel);continue;
            }
            throw new InvalidOperationException($"Windrun 接口 {path} 返回 HTTP {(int)res.StatusCode}。旧数据未改动。");
        }
    }

    static JsonElement Data(JsonDocument d)=>d.RootElement.TryGetProperty("data",out var x)?x:d.RootElement;
    static Statistics Transform(Dictionary<string,JsonDocument> d)
    {
        var abilityNames=new Dictionary<int,(string Name,bool Ultimate)>();
        foreach(var x in Data(d["static/abilities"]).EnumerateArray())
        {
            int id=x.GetProperty("valveId").GetInt32();string name=x.GetProperty("shortName").GetString()!;
            bool ultimate=x.TryGetProperty("isUltimate",out var u)&&u.ValueKind==JsonValueKind.True;
            if(id>0)abilityNames[id]=(name,ultimate);
        }
        var heroNames=new Dictionary<int,string>();
        var localHeroes=Store.Read<Catalog>("catalog.json").Heroes.Select(h=>h.Code).ToDictionary(h=>h["npc_dota_hero_".Length..].Replace("_","").ToLowerInvariant(),h=>h);
        foreach(var x in Data(d["static/heroes"]).EnumerateObject())
        {
            int id=x.Value.GetProperty("id").GetInt32();string shortName=x.Value.GetProperty("shortName").GetString()!;
            var flat=shortName.Replace("_","").Replace("-","").Replace(" ","").ToLowerInvariant();
            if(localHeroes.TryGetValue(flat,out var code))heroNames[id]=code;
        }
        var stats=new Statistics
        {
            Source=Base,
            Attribution="Windrun.io API v2；组合增益 = 组合胜率减去各单项胜率平均值。",
            FetchedAt=DateTimeOffset.UtcNow.ToString("O"),
            Patch=ReadPatch(d)
        };
        var rates=new Dictionary<int,double>();
        var overall=Data(d["abilities"]).GetProperty("abilityStats");
        foreach(var x in overall.EnumerateArray())
        {
            int id=x.GetProperty("abilityId").GetInt32();double wr=x.GetProperty("winrate").GetDouble();int n=x.GetProperty("numPicks").GetInt32();
            rates[id]=wr;
            if(id>0&&abilityNames.TryGetValue(id,out var a))stats.Abilities[a.Name]=new(){WinRate=wr,Samples=n,AvgPick=x.GetProperty("avgPickPosition").GetDouble(),Ultimate=a.Ultimate};
            else if(id<0&&heroNames.TryGetValue(-id,out var hero))stats.Heroes[hero]=new(){WinRate=wr,Source=Base+"abilities"};
        }
        foreach(var p in Data(d["ability-pairs"]).GetPropertyOrEmpty("abilityPairs"))
        {
            int ia=p.GetProperty("abilityIdOne").GetInt32(),ib=p.GetProperty("abilityIdTwo").GetInt32();
            if(!Name(ia,abilityNames,heroNames,out var a)||!Name(ib,abilityNames,heroNames,out var b)||a==b)continue;
            double synergy=(p.GetProperty("winrate").GetDouble()-((rates.GetValueOrDefault(ia,.5)+rates.GetValueOrDefault(ib,.5))/2))*100;
            stats.Pairs.Add(new(){A=a,B=b,SynergyPp=synergy,Samples=p.GetProperty("numPicks").GetInt32(),Source=Base+"ability-pairs"});
        }
        foreach(var p in Data(d["ability-triplets"]).GetPropertyOrEmpty("abilityTriplets"))
        {
            int ia=p.GetProperty("abilityIdOne").GetInt32(),ib=p.GetProperty("abilityIdTwo").GetInt32(),ic=p.GetProperty("abilityIdThree").GetInt32();
            if(!Name(ia,abilityNames,heroNames,out var a)||!Name(ib,abilityNames,heroNames,out var b)||!Name(ic,abilityNames,heroNames,out var c))continue;
            double baseline=(rates.GetValueOrDefault(ia,.5)+rates.GetValueOrDefault(ib,.5)+rates.GetValueOrDefault(ic,.5))/3;
            stats.Triplets.Add(new(){A=a,B=b,C=c,SynergyPp=(p.GetProperty("winrate").GetDouble()-baseline)*100,Samples=p.GetProperty("numPicks").GetInt32(),Source=Base+"ability-triplets"});
        }
        if(stats.Abilities.Count<400)throw new InvalidDataException($"Windrun 只返回 {stats.Abilities.Count} 个技能，拒绝覆盖旧库。");
        return stats;
    }
    static bool Name(int id,Dictionary<int,(string Name,bool Ultimate)> abilities,Dictionary<int,string> heroes,out string name)
    {if(id<0)return heroes.TryGetValue(-id,out name!);if(abilities.TryGetValue(id,out var a)){name=a.Name;return true;}name="";return false;}
    static string ReadPatch(Dictionary<string,JsonDocument> d)
    {
        var root=Data(d["static/patches"]);if(root.ValueKind==JsonValueKind.Array&&root.GetArrayLength()>0)return root[0].ToString();
        var abilities=Data(d["abilities"]);if(abilities.TryGetProperty("patches",out var p)&&p.TryGetProperty("overall",out var a)&&a.GetArrayLength()>0)return a[0].ToString();return "未知";
    }
    static IEnumerable<JsonElement> GetPropertyOrEmpty(this JsonElement x,string name)=>x.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.Array?p.EnumerateArray():[];
}
