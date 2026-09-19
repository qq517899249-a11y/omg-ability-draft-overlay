using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;

namespace OmgAd;

public sealed class Recognizer:IDisposable
{
    readonly Catalog catalog;
    readonly HeroOcr ocr;
    public string? OcrError=>ocr.LastError;
    public Dictionary<int,string> OcrText=>ocr.LastText;
    readonly List<(string Key,bool Hero,float[] Feature)> templates=[];
    public Recognizer(Catalog c)
    {
        catalog=c;ocr=new(c);
        foreach(var a in c.All.Values) if(a.Image is not null)
        {
            // Multiple visible crops handle perspective cards and labels over the lower edge.
            foreach(var (x,y,w,h) in new (double,double,double,double)[]{(0,0,1,1),(0,0,1,.75),(0,0,1,.55),(.05,.05,.9,.9),(.05,.05,.9,.7),(0,.15,1,.85),(.1,.15,.8,.65),(0,.25,1,.75)})
                templates.Add((a.Code,a.Hero,Feature(a.Image,new Rectangle((int)(x*a.Image.Width),(int)(y*a.Image.Height),(int)(w*a.Image.Width),(int)(h*a.Image.Height)))));
        }
    }
    public static float[] Feature(Bitmap image,Rectangle rect)
    {
        using var b=new Bitmap(12,12,PixelFormat.Format24bppRgb);
        using(var g=Graphics.FromImage(b)) {g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.DrawImage(image,new Rectangle(0,0,12,12),rect,GraphicsUnit.Pixel);}
        var f=new float[432];double sum=0;
        for(int y=0;y<12;y++)for(int x=0;x<12;x++) {var c=b.GetPixel(x,y);int i=(y*12+x)*3;f[i]=c.R;f[i+1]=c.G;f[i+2]=c.B;sum+=c.R+c.G+c.B;}
        var mean=sum/f.Length;double norm=0;
        for(int i=0;i<f.Length;i++){f[i]-=(float)mean;norm+=f[i]*f[i];}
        norm=Math.Sqrt(norm);if(norm<200)return new float[432];
        for(int i=0;i<f.Length;i++)f[i]/=(float)norm;return f;
    }
    static double Dot(float[] a,float[] b)
    {
        var sum=Vector<float>.Zero;int i=0;
        for(;i<=a.Length-Vector<float>.Count;i+=Vector<float>.Count)sum+=new Vector<float>(a,i)*new Vector<float>(b,i);
        float v=Vector.Sum(sum);for(;i<a.Length;i++)v+=a[i]*b[i];return v;
    }
    public List<Recognition> Scan(Bitmap image,Settings settings)
    {
        var result=new Recognition[settings.Regions.Count];var imageSize=image.Size;
        Parallel.For(0,result.Length,i=>
        {
            // GDI+ image access itself is serialized; similarity calculations are parallel.
            var r=settings.Regions[i]; var rect=r.Rect(imageSize);var features=new List<float[]>();
            if(r.Role.StartsWith("hero")){result[i]=new(i,r,null,0,0);return;}
            if(!new Rectangle(Point.Empty,imageSize).Contains(rect)){result[i]=new(i,r,null,0,0);return;}
            lock(image)
            {
                if(r.Role=="pool")
                {
                    double brightness=0;int samples=0;
                    for(int y=1;y<8;y++)for(int x=1;x<8;x++){var pixel=image.GetPixel(rect.X+rect.Width*x/8,rect.Y+rect.Height*y/8);brightness+=Math.Max(pixel.R,Math.Max(pixel.G,pixel.B));samples++;}
                    if(brightness/samples<45){result[i]=new(i,r,null,0,0);return;}
                }
                // Small local alignment search compensates for perspective and DPI rounding.
                foreach(float scale in new[]{1f,.86f,1.14f})foreach(float dx in new[]{0f,-.10f,.10f})foreach(float dy in new[]{0f,-.10f,.10f})
                {
                    int w=(int)(rect.Width*scale),h=(int)(rect.Height*scale);
                    var q=new Rectangle(rect.X+(rect.Width-w)/2+(int)(rect.Width*dx),rect.Y+(rect.Height-h)/2+(int)(rect.Height*dy),w,h);
                    if(new Rectangle(Point.Empty,imageSize).Contains(q))features.Add(Feature(image,q));
                }
            }
            var scores=new Dictionary<string,double>();
            foreach(var t in templates)
            {
                if(r.Role=="hero"&&!t.Hero||r.Role=="pick"&&t.Hero)continue;
                var sim=features.Max(f=>Dot(f,t.Feature));
                if(!scores.TryGetValue(t.Key,out var old)||sim>old)scores[t.Key]=sim;
            }
            var top=scores.OrderByDescending(p=>p.Value).Take(2).ToArray();
            double best=top.Length>0?top[0].Value:0, margin=top.Length>1?best-top[1].Value:0;
            result[i]=new(i,r,best>=settings.Threshold&&margin>=settings.Margin?top[0].Key:null,best,margin,top.Length>0?top[0].Key:null);
        });
        foreach(var hero in ocr.Read(image,settings))result[hero.Index]=hero;
        // A duplicate icon detection in the pool is ambiguous: keep only the strongest match.
        foreach(var group in result.Where(r=>r.Region.Role=="pool"&&r.Key!=null).GroupBy(r=>r.Key))
            foreach(var duplicate in group.OrderByDescending(r=>r.Confidence).Skip(1))result[duplicate.Index]=duplicate with{Key=null};
        return result.ToList();
    }
    public void Dispose()=>ocr.Dispose();
    public static Rectangle FindViewport(Bitmap b)
    {
        bool BlackColumn(int x) {for(int n=1;n<10;n++){var c=b.GetPixel(x,b.Height*n/10);if(c.R+c.G+c.B>36)return false;}return true;}
        int left=0,right=b.Width-1;
        while(left<b.Width/5&&BlackColumn(left))left++;
        while(right>b.Width*4/5&&BlackColumn(right))right--;
        return new Rectangle(left,0,right-left+1,b.Height);
    }
}

public sealed class StateTracker(Catalog catalog,Engine engine)
{
    readonly Dictionary<int,(string? Key,int Count)> stable=[];
    readonly Dictionary<int,string> poolLocations=[];
    public bool LastFrameCommitted { get; private set; }
    public void Reset(){stable.Clear();poolLocations.Clear();LastFrameCommitted=false;}
    public string Apply(Draft draft,List<Recognition> matches,bool immediate=false)
    {
        LastFrameCommitted=false;
        // Withdraw vanished/dimmed cards immediately on a recognizable draft frame, even if
        // the remaining pool is too small to pass the full-frame commit threshold.
        bool draftVisible=matches.Count(m=>m.Region.Role=="pool"&&m.Key!=null)>=2||matches.Any(m=>m.Region.Role=="hero-name"&&m.Key!=null)||matches.Count(m=>m.Region.Role=="pick"&&m.Key!=null)>=2;
        if(draftVisible)
        {
            var visible=matches.Where(m=>m.Region.Role=="pool"&&m.Key!=null).Select(m=>m.Key!).ToHashSet();
            draft.Unavailable.ExceptWith(visible);
            foreach(var m in matches.Where(m=>m.Region.Role=="pool"))
            {
                if(poolLocations.TryGetValue(m.Index,out var old)&&m.Key!=old&&!visible.Contains(old))draft.Unavailable.Add(old);
                if(m.Key!=null)poolLocations[m.Index]=m.Key;
            }
        }
        // A model/spell visibly owned by any player is unavailable before ownership settles
        // over two frames. Manual pool entries must not bring it back into recommendations.
        foreach(var m in matches.Where(m=>m.Key!=null&&m.Region.Role is "hero-name" or "pick"))draft.Unavailable.Add(m.Key!);
        var accepted=new List<Recognition>();
        foreach(var m in matches)
        {
            var previous=stable.GetValueOrDefault(m.Index);
            string? token=m.Key??(m.Region.Role=="hero-name"?"slot:"+m.HeroSlot:null);
            var n=previous.Key==token?previous.Count+1:1;stable[m.Index]=(token,n);
            if(m.Region.Role=="hero-name"&&m.Region.Seat is >=0 and <10)
            {
                var seat=draft.Seats[m.Region.Seat];
                // Fail closed for hero recommendations while the name is uncertain. Once occupied,
                // a blank/OCR failure/old empty label can never reopen this slot within the draft.
                if(m.Key!=null||m.HeroSlot==HeroSlotState.Occupied)seat.HeroSlot=HeroSlotState.Occupied;
                else if(!seat.HasHero)seat.HeroSlot=m.HeroSlot==HeroSlotState.Empty&&(immediate||n>=2)?HeroSlotState.Empty:HeroSlotState.Unknown;
            }
            if(m.Key!=null&&(immediate||n>=2))accepted.Add(m);
        }
        int pool=accepted.Count(r=>r.Region.Role=="pool");
        // Never replace a draft from a nearly blank frame, loading screen or tooltip.
        var heroes=accepted.Count(r=>r.Region.Role=="hero-name");
        var picks=accepted.Count(r=>r.Region.Role=="pick");
        int changes=0;
        // Hero confirmation is independent of the remaining pool size and skill-recognition count.
        foreach(var m in accepted.Where(r=>r.Region.Role=="hero-name"&&r.Region.Seat is >=0 and <10))
        {
            if(!catalog.All.TryGetValue(m.Key!,out var hero)||!hero.Hero)continue;
            var seat=draft.Seats[m.Region.Seat];
            // A drafted model cannot change; later OCR must not replace it with another name.
            if(seat.Hero is not null||draft.Used().Contains(m.Key!))continue;
            seat.Hero=m.Key;seat.HeroSlot=HeroSlotState.Occupied;draft.History.Add(engine.Estimate(draft));changes++;
        }
        if(pool<6&&heroes<2&&picks<8)return $"等待稳定选技画面 · 已确认 {pool} 个池内图标 · 英雄更新 {changes}";
        LastFrameCommitted=true;
        draft.Pool=accepted.Where(r=>r.Region.Role=="pool").Select(r=>r.Key!).Concat(draft.ManualPool).ToHashSet();
        foreach(var m in accepted.Where(r=>r.Region.Role=="pick"))
        {
            int idx=m.Region.Seat;if(idx<0||idx>=10)continue;
            var key=m.Key!;if(!catalog.All.ContainsKey(key))continue;var s=draft.Seats[idx];
            if(s.Hero==key||s.Skills.Contains(key)||draft.Used().Contains(key))continue;
            draft.Pool.Add(key);
            bool unavailable=draft.Unavailable.Remove(key);
            try{if(engine.Pick(draft,idx,key,out _))changes++;}
            finally{if(unavailable)draft.Unavailable.Add(key);}
            draft.Pool.Remove(key);
        }
        return $"可选池 {pool} 项 · 英雄文字 {heroes}/10 · 更新 {changes} 个已选项";
    }
}
