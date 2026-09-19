using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmgAd;

public static class Store
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string Root = AppContext.BaseDirectory;
    public static string Data(string name) => Path.Combine(Root, "data", name);
    public static T Read<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Data(name)), Json)!;
    public static void Save<T>(string name, T value)
    {
        var file = Data(name); var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, file, true);
    }
}

public static class UserSettings
{
    static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OMG AD");
    public static string FilePath=>Path.Combine(Folder,"settings.json");
    public static Settings Load()
    {
        Directory.CreateDirectory(Folder);
        if(File.Exists(FilePath))return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath),Store.Json)!;
        var legacy=Store.Data("settings.json");
        var value=File.Exists(legacy)?JsonSerializer.Deserialize<Settings>(File.ReadAllText(legacy),Store.Json)!:Settings.Reference();
        Save(value);return value;
    }
    public static void Save(Settings value)
    {
        Directory.CreateDirectory(Folder);var temp=FilePath+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(value,Store.Json));
        if(File.Exists(FilePath))File.Copy(FilePath,Path.Combine(Folder,"settings.backup.json"),true);
        File.Move(temp,FilePath,true);
    }
}

public sealed class Item
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonPropertyName("is_ultimate")] public bool Ultimate { get; set; }
    public string Template { get; set; } = "";
    [JsonIgnore] public bool Hero => Code.StartsWith("npc_dota_hero_");
    [JsonIgnore] public Bitmap? Image { get; set; }
    public override string ToString() => Name;
}
public sealed class Catalog
{
    public List<Item> Abilities { get; set; } = [];
    public List<Item> Heroes { get; set; } = [];
    [JsonIgnore] public Dictionary<string, Item> All { get; private set; } = [];
    public static Catalog Load()
    {
        var c = Store.Read<Catalog>("catalog.json");
        c.All = c.Abilities.Concat(c.Heroes).ToDictionary(a => a.Code);
        foreach (var a in c.All.Values)
        {
            var p = Store.Data(a.Template);
            if (File.Exists(p)) { using var src = new Bitmap(p); a.Image = new Bitmap(src); }
        }
        return c;
    }
}
public sealed class AbilityStat
{
    public double WinRate { get; set; }
    public int Samples { get; set; }
    public double AvgPick { get; set; }
    public bool Ultimate { get; set; }
}
public sealed class HeroStat
{
    public string Attribute { get; set; } = "";
    public string Attack { get; set; } = "";
    public double? WinRate { get; set; }
    public string? Source { get; set; }
}
public sealed class PairStat
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public double SynergyPp { get; set; }
    public int Samples { get; set; }
    public string Source { get; set; } = "";
}
public sealed class TripletStat
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public string C { get; set; } = "";
    public double SynergyPp { get; set; }
    public int Samples { get; set; }
    public string Source { get; set; } = "";
}
public sealed class Statistics
{
    public string Source { get; set; } = "";
    public string Attribution { get; set; } = "";
    public string FetchedAt { get; set; } = "";
    public string Patch { get; set; } = "未知";
    public Dictionary<string, AbilityStat> Abilities { get; set; } = [];
    public Dictionary<string, HeroStat> Heroes { get; set; } = [];
    public List<string[]> Exclusive { get; set; } = [];
    public List<PairStat> Pairs { get; set; } = [];
    public List<TripletStat> Triplets { get; set; } = [];
    public void ReplaceWith(Statistics other)
    {
        Source=other.Source;Attribution=other.Attribution;FetchedAt=other.FetchedAt;Patch=other.Patch;
        Abilities=other.Abilities;Heroes=other.Heroes;Exclusive=other.Exclusive;Pairs=other.Pairs;Triplets=other.Triplets;
    }
    public static Statistics Load()
    {
        var stats=Store.Read<Statistics>("statistics.json");
        if(File.Exists(Store.Data("hero-statistics.json")))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(Store.Data("hero-statistics.json")));
            string source=doc.RootElement.GetProperty("source").GetString()!;
            foreach(var h in doc.RootElement.GetProperty("rates").EnumerateObject())
                if(stats.Heroes.TryGetValue(h.Name,out var hero)&&hero.WinRate is null){hero.WinRate=h.Value.GetDouble()/100;hero.Source=source;}
        }
        return stats;
    }
}
public enum HeroSlotState { Unknown, Empty, Occupied }
public sealed class Seat
{
    public string? Hero { get; set; }
    public HeroSlotState HeroSlot { get; set; }=HeroSlotState.Empty;
    [JsonIgnore] public bool HasHero=>Hero is not null||HeroSlot==HeroSlotState.Occupied;
    public List<string> Skills { get; set; } = [];
}
public sealed class Draft
{
    public List<Seat> Seats { get; set; } = Enumerable.Range(0, 10).Select(_ => new Seat()).ToList();
    public HashSet<string> Pool { get; set; } = [];
    public HashSet<string> ManualPool { get; set; } = [];
    public HashSet<string> Banned { get; set; } = [];
    public HashSet<string> Unavailable { get; set; } = [];
    public int Me { get; set; } = 4;
    public int? ViewSeat { get; set; }
    [JsonIgnore] public int TargetSeat => ViewSeat??Me;
    public List<double> History { get; set; } = [];
    public HashSet<string> Used() => Seats.SelectMany(s => s.Skills.Concat(s.Hero is null ? [] : new[] { s.Hero })).ToHashSet();
    public bool Available(string key) => Pool.Contains(key) && !Banned.Contains(key) && !Unavailable.Contains(key) && !Used().Contains(key);
}
public sealed class Region
{
    public string Role { get; set; } = "pool";
    public int Seat { get; set; } = -1;
    public int Slot { get; set; } = -1;
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public Rectangle Rect(Size s) => new((int)(X*s.Width), (int)(Y*s.Height), Math.Max(1,(int)(W*s.Width)), Math.Max(1,(int)(H*s.Height)));
}
public sealed class CalibrationProfile
{
    public int Width { get; set; }
    public int Height { get; set; }
    public List<Region> Regions { get; set; }=[];
}
public sealed class Settings
{
    public int MySeat { get; set; } = 4;
    public List<Region> Regions { get; set; } = [];
    public double Threshold { get; set; } = .77;
    public double Margin { get; set; } = .035;
    public int IntervalMs { get; set; } = 1200;
    public int CalibrationWidth { get; set; }
    public int CalibrationHeight { get; set; }
    public List<CalibrationProfile> Profiles { get; set; }=[];
    [JsonIgnore] public string ActiveLayout { get; private set; }="内置 16:9";
    static Region Copy(Region r)=>new(){Role=r.Role,Seat=r.Seat,Slot=r.Slot,X=r.X,Y=r.Y,W=r.W,H=r.H};
    public void EnsureProfiles()
    {
        if(Profiles.Count==0&&CalibrationWidth>0&&CalibrationHeight>0&&Regions.Count>0)
            Profiles.Add(new(){Width=CalibrationWidth,Height=CalibrationHeight,Regions=Regions.Select(Copy).ToList()});
    }
    public void Activate(Size size)
    {
        EnsureProfiles();if(Profiles.Count==0){ActiveLayout=$"内置布局 → {size.Width}×{size.Height}";return;}
        double ratio=(double)size.Width/size.Height;
        var exact=Profiles.FirstOrDefault(p=>p.Width==size.Width&&p.Height==size.Height);
        var chosen=exact??Profiles.OrderBy(p=>Math.Abs((double)p.Width/p.Height-ratio)*100+Math.Abs(Math.Log((double)p.Height/size.Height))).First();
        Regions=chosen.Regions.Select(Copy).ToList();CalibrationWidth=chosen.Width;CalibrationHeight=chosen.Height;
        ActiveLayout=exact is not null?$"精确校准 {chosen.Width}×{chosen.Height}":$"自动缩放 {chosen.Width}×{chosen.Height} → {size.Width}×{size.Height}";
    }
    public void SaveProfile(Size size)
    {
        EnsureProfiles();var profile=Profiles.FirstOrDefault(p=>p.Width==size.Width&&p.Height==size.Height);
        if(profile is null){profile=new(){Width=size.Width,Height=size.Height};Profiles.Add(profile);}
        profile.Regions=Regions.Select(Copy).ToList();CalibrationWidth=size.Width;CalibrationHeight=size.Height;ActiveLayout=$"精确校准 {size.Width}×{size.Height}";
    }
    public void EnsureHeroNameRegions()
    {
        Regions.RemoveAll(r=>r.Role=="hero");
        for(int side=0;side<2;side++)for(int row=0;row<5;row++)
        {
            int seat=side*5+row;
            if(!Regions.Any(r=>r.Role=="hero-name"&&r.Seat==seat))Regions.Add(new Region{Role="hero-name",Seat=seat,X=(side==0?193f:1340f)/1676,Y=(130f+row*147)/942,W=155f/1676,H=25f/942});
        }
    }
    public static Settings Reference()
    {
        // Coordinates relative to the game viewport, excluding the supplied screenshot's side bars.
        var s = new Settings{CalibrationWidth=1676,CalibrationHeight=942};
        void Add(string role, float x,float y,float w,float h,int seat=-1,int slot=-1) =>
            s.Regions.Add(new Region{Role=role,X=x/1676,Y=y/942,W=w/1676,H=h/942,Seat=seat,Slot=slot});
        for (int row=0;row<2;row++) for(int col=0;col<6;col++) Add("pool",604+col*83,144+row*87,47,39);
        // 12 body cards interspersed with 36 regular skills in the reference layout.
        float[] ys = [302,360,418,516,584,660];
        float[][] xs = [[548,634,705,775,864,935,1005,1091], [541,629,703,775,862,935,1012,1095], [532,624,701,775,861,936,1018,1104], [520,613,692,771,864,945,1025,1130], [510,608,689,771,868,950,1036,1145], [502,602,684,769,874,957,1044,1160]];
        for(int row=0;row<6;row++) for(int col=0;col<8;col++)
        {
            Add("pool",xs[row][col],ys[row],47,row<3?24:30);
        }
        for(int side=0;side<2;side++) for(int row=0;row<5;row++)
        {
            int seat=side*5+row;
            for(int col=0;col<4;col++) Add("pick",(side==0?189:1281)+col*53,207+row*147,47,47,seat,col);
        }
        s.EnsureHeroNameRegions();
        s.Profiles.Add(new(){Width=1676,Height=942,Regions=s.Regions.Select(Copy).ToList()});
        return s;
    }
}
public record Recognition(int Index, Region Region, string? Key, double Confidence, double Margin, string? Candidate=null)
{
    public HeroSlotState HeroSlot { get; init; }=HeroSlotState.Unknown;
}
public record ComboHint(string Partner, string Name, bool AlreadyPicked, string Reason)
{
    public string Kind { get; init; }="联动";
    public string Label=>Kind=="适配"?(AlreadyPicked?"英雄适配":"可配英雄/技能"):(AlreadyPicked?"已选联动":"池内可追");
}
public record Recommendation(Item Item, double Score, double? WinRate, List<string> Reasons)
{
    public double GainPp { get; init; }
    public double RadiantAfter { get; init; }
    public List<ComboHint> Combos { get; init; } = [];
}
