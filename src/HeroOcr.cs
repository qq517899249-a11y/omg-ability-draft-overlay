using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmgAd;

public sealed class HeroOcr(Catalog catalog):IDisposable
{
    Process? worker;readonly object gate=new();
    public string? LastError { get; private set; }
    public Dictionary<int,string> LastText { get; private set; }=[];
    static string Normalize(string value)=>Regex.Replace(value,"[^\\p{IsCJKUnifiedIdeographs}a-zA-Z]","").ToLowerInvariant();
    public string? MatchName(string text)
    {
        var normalized=Normalize(text);
        if(normalized.Contains("无英雄"))return null;
        var aliases=new Dictionary<string,string>{{"火女","lina"},{"影魔","nevermore"},{"拉希克","leshrac"},{"拉席克","leshrac"},{"娜迦海妖","naga_siren"},{"伐木机","shredder"},{"骷髅弓箭手","clinkz"}};
        foreach(var hero in catalog.Heroes.OrderByDescending(h=>h.Name.Length))if(normalized.Contains(Normalize(hero.Name)))return hero.Code;
        foreach(var(a,key)in aliases)if(normalized.Contains(a)&&catalog.All.ContainsKey("npc_dota_hero_"+key))return "npc_dota_hero_"+key;
        return null;
    }
    public List<Recognition> Read(Bitmap image,Settings settings)
    {
        lock(gate)
        {
            LastText=[];LastError=null;
            var regions=settings.Regions.Select((r,i)=>(Region:r,Index:i)).Where(p=>p.Region.Role=="hero-name").ToList();
            if(regions.Count==0)return [];
            var path=Path.Combine(Path.GetTempPath(),$"omgad-hero-ocr-{Environment.ProcessId}.png");
            try
            {
                // One atlas = one OCR call for ten names. Two contrast variants per row.
                using(var atlas=new Bitmap(1400,regions.Count*110+40,PixelFormat.Format24bppRgb))
                {
                    using var g=Graphics.FromImage(atlas);g.Clear(Color.White);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    using var labelFont=new Font("Microsoft YaHei UI",25);
                    for(int n=0;n<regions.Count;n++)
                    {
                        // A neutral text anchor keeps short two-character names from being discarded as logos.
                        g.DrawString("英雄",labelFont,Brushes.Black,0,n*110+30);
                        var rect=regions[n].Region.Rect(image.Size);
                        if(!new Rectangle(Point.Empty,image.Size).Contains(rect))continue;
                        using var crop=image.Clone(rect,PixelFormat.Format24bppRgb);
                        using var mask=new Bitmap(crop.Width,crop.Height);
                        using var gray=new Bitmap(crop.Width,crop.Height);
                        for(int y=0;y<crop.Height;y++)for(int x=0;x<crop.Width;x++)
                        {
                            var c=crop.GetPixel(x,y);int max=Math.Max(c.R,Math.Max(c.G,c.B));
                            bool white=Math.Min(c.R,Math.Min(c.G,c.B))>135;
                            bool color=(c.G>105&&c.G>c.R*1.3&&c.G>c.B*1.12)||(c.R>115&&c.R>c.G*1.4&&c.R>c.B*1.25);
                            mask.SetPixel(x,y,white||color?Color.Black:Color.White);
                            int v=255-max;gray.SetPixel(x,y,Color.FromArgb(v,v,v));
                        }
                        float scale=Math.Min(570f/crop.Width,80f/crop.Height);int w=(int)(crop.Width*scale),h=(int)(crop.Height*scale);
                        g.DrawImage(mask,new Rectangle(140,n*110+30,w,h));g.DrawImage(gray,new Rectangle(740,n*110+30,w,h));
                    }
                    atlas.Save(path,ImageFormat.Png);
                    if(Environment.GetEnvironmentVariable("OMGAD_OCR_DEBUG") is string debug)atlas.Save(debug,ImageFormat.Png);
                }
                EnsureWorker();worker!.StandardInput.WriteLine(path);worker.StandardInput.Flush();
                var pending=worker.StandardOutput.ReadLineAsync();
                if(!pending.Wait(TimeSpan.FromSeconds(8)))throw new TimeoutException("英雄名 OCR 超时");
                var json=pending.Result??throw new IOException("OCR 进程退出");
                using var doc=JsonDocument.Parse(json);
                if(doc.RootElement.GetProperty("error").ValueKind==JsonValueKind.String)throw new IOException(doc.RootElement.GetProperty("error").GetString());
                var texts=new Dictionary<int,List<string>>();
                foreach(var line in doc.RootElement.GetProperty("lines").EnumerateArray())
                {
                    int row=(int)(line.GetProperty("y").GetDouble()/110);
                    if(row<0||row>=regions.Count)continue;
                    if(!texts.ContainsKey(row))texts[row]=[];texts[row].Add(line.GetProperty("text").GetString()??"");
                }
                var output=new List<Recognition>();
                for(int n=0;n<regions.Count;n++)
                {
                    var text=string.Join(" | ",texts.GetValueOrDefault(n)??[]);LastText[regions[n].Region.Seat]=text;
                    var keys=(texts.GetValueOrDefault(n)??[]).Select(MatchName).Where(k=>k!=null).Distinct().ToList();
                    string? key=keys.Count==1?keys[0]:null;
                    var slot=key!=null?HeroSlotState.Occupied:Normalize(text).Contains("无英雄")?HeroSlotState.Empty:HeroSlotState.Unknown;
                    output.Add(new(regions[n].Index,regions[n].Region,key,key==null?0:1,key==null?0:1,key){HeroSlot=slot});
                }
                return output;
            }
            catch(Exception ex){LastError=ex.Message;StopWorker();return regions.Select(p=>new Recognition(p.Index,p.Region,null,0,0)).ToList();}
            finally{if(File.Exists(path))File.Delete(path);}
        }
    }
    void EnsureWorker()
    {
        if(worker is {HasExited:false})return;
        var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8};
        foreach(var a in new[]{"-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Store.Data("ocr-worker.ps1")})start.ArgumentList.Add(a);
        worker=Process.Start(start)??throw new IOException("无法启动本机 OCR");worker.ErrorDataReceived+=(_,_)=>{};worker.BeginErrorReadLine();
    }
    void StopWorker(){if(worker is null)return;try{if(!worker.HasExited)worker.Kill();}catch{}worker.Dispose();worker=null;}
    public void Dispose(){lock(gate)StopWorker();}
}
