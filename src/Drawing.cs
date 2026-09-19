using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace OmgAd;
public static class Art
{
    public static string SeatName(int seat)=>(seat<5?"天辉":"夜魇")+(seat%5+1)+"号";
    public static string OptionName(Item item)=>(item.Hero?"[英雄] ":"[技能] ")+item.Name;
    public static RectangleF ComboPanel(int side)=>new(side==0?408:1096,142,176,138);
    public static readonly Color Bg=Color.FromArgb(12,18,26),Panel=Color.FromArgb(21,30,41),Muted=Color.FromArgb(139,158,177),Ink=Color.FromArgb(229,239,247);
    public static readonly Color[] Rank=[Color.FromArgb(110,235,191),Color.FromArgb(115,180,255),Color.FromArgb(219,160,255)];
    public static Color RankColor(int index)=>index switch
    {0=>Color.FromArgb(255,69,91),1=>Color.FromArgb(255,164,62),2 or 3=>Color.FromArgb(188,119,255),4 or 5 or 6=>Color.FromArgb(71,173,255),_=>Color.FromArgb(72,224,166)};
    static readonly Color[] Spectrum=[Color.FromArgb(255,81,121),Color.FromArgb(255,184,73),Color.FromArgb(228,240,105),Color.FromArgb(80,233,166),Color.FromArgb(84,186,255),Color.FromArgb(166,126,255),Color.FromArgb(255,81,121)];
    public static void Text(Graphics g,string text,float x,float y,float size=11,Color? color=null,bool bold=false,float width=2000,float height=60)
    {
        using var font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);
        using var brush=new SolidBrush(color??Ink);
        using var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter};
        g.DrawString(text,font,brush,new RectangleF(x,y,width,height),format);
    }
    public static void Box(Graphics g,RectangleF r,Color c){using var b=new SolidBrush(c);g.FillRectangle(b,r);}
    public static void Border(Graphics g,RectangleF r,Color c,float width=2){using var p=new Pen(c,width);g.DrawRectangle(p,r.X,r.Y,r.Width,r.Height);}
    public static void Icon(Graphics g,Item? item,RectangleF r)
    {
        if(item?.Image is not null)g.DrawImage(item.Image,r);else Box(g,r,Color.FromArgb(35,45,57));
    }
    static GraphicsPath Rounded(RectangleF r,float radius=5)
    {
        var path=new GraphicsPath();float d=radius*2;
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    public static void Glass(Graphics g,RectangleF r,Color? accent=null)
    {
        using var path=Rounded(r,6);
        using var fill=new LinearGradientBrush(r,Color.FromArgb(240,24,33,46),Color.FromArgb(232,9,15,25),90);
        g.FillPath(fill,path);using var edge=new Pen(Color.FromArgb(90,accent??Color.FromArgb(139,163,193)),1);g.DrawPath(edge,path);
    }
    public static void Rainbow(Graphics g,RectangleF r)
    {
        var points=new[]{new PointF(r.Left,r.Top),new PointF(r.Right,r.Top),new PointF(r.Right,r.Bottom),new PointF(r.Left,r.Bottom),new PointF(r.Left,r.Top)};
        for(int side=0;side<4;side++)
        {
            using var brush=new LinearGradientBrush(points[side],points[side+1],Spectrum[side],Spectrum[side+2]);
            using var glow=new Pen(Color.FromArgb(50,Spectrum[side]),7);g.DrawLine(glow,points[side],points[side+1]);
            using var pen=new Pen(brush,2);g.DrawLine(pen,points[side],points[side+1]);
        }
    }
    public static void SkillHighlight(Graphics g,RectangleF r,Recommendation recommendation,int rank,bool compact=false)
    {
        var color=RankColor(rank);
        // Alpha edges are composited by the layered window, never against a magenta color key.
        for(int step=rank==0?7:4;step>=1;step--)
        {
            var glow=RectangleF.Inflate(r,step,step);using var p=new Pen(Color.FromArgb(rank==0?17:10,color),3);
            g.DrawRectangle(p,glow.X,glow.Y,glow.Width,glow.Height);
        }
        using(var path=Rounded(r,3))
        {
            using var edge=new Pen(color,rank==0?3:2);g.DrawPath(edge,path);
            using var shine=new Pen(Color.FromArgb(170,255,255,255),.7f);var inner=RectangleF.Inflate(r,-2,-2);g.DrawRectangle(shine,inner.X,inner.Y,inner.Width,inner.Height);
        }
        if(recommendation.Combos.Count>0)Rainbow(g,RectangleF.Inflate(r,5,5));
        var badge=new RectangleF(r.X-8,r.Y-10,rank==9?28:24,22);
        using(var path=Rounded(badge,5)){using var fill=new SolidBrush(Color.FromArgb(245,13,18,29));g.FillPath(fill,path);using var pen=new Pen(color,1.6f);g.DrawPath(pen,path);}
        Text(g,(rank+1).ToString("00"),badge.X+3,badge.Y+1,9,color,true,26,21);
        if(!compact)
        {
            var footer=new RectangleF(r.X+1,r.Bottom-14,r.Width-2,14);Box(g,footer,Color.FromArgb(225,8,13,22));
            Text(g,$"+{recommendation.GainPp:0.00}pp",footer.X+1,footer.Y,7.5f,color,true,footer.Width,15);
            if(recommendation.Combos.Count>0)
            {
                var hint=recommendation.Combos.Any(c=>c.Kind=="联动")?(recommendation.Combos.Any(c=>c.AlreadyPicked&&c.Kind=="联动")?"联动":"可追"):"适配";
                Box(g,new RectangleF(r.Right-26,r.Y-11,30,13),Color.FromArgb(240,13,18,29));Text(g,hint,r.Right-25,r.Y-11,7,Color.White,true,33,15);
            }
        }
    }
    public static void Overlay(Graphics g,Size size,Draft draft,List<Recommendation> recommendations,List<Recognition> matches,Engine engine,string status)
    {
        var saved=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        recommendations=engine.SelectableDisplay(draft,recommendations);
        float scale=Math.Min(size.Width/1676f,size.Height/942f);g.ScaleTransform(scale,scale);float w=size.Width/scale,h=size.Height/scale;
        // Raise the complete header above the game's countdown instead of covering it.
        g.TranslateTransform(0,-60);
        float left=w/2-550;var panel=new RectangleF(left,65,1100,65);Glass(g,panel);
        var p=engine.Estimate(draft);var previous=draft.History.Count>1?draft.History[^2]:.5;
        var shift=(p-previous)*100;
        Text(g,$"查看 {SeatName(draft.TargetSeat)}{(draft.TargetSeat==draft.Me?" · 我":" · 队伍视角")}",left+15,72,10,Ink,true,260,22);
        Text(g,$"天辉 {p:P1}  ({shift:+0.00;-0.00;0.00}pp)",left+280,72,11,Rank[0],true,270,22);
        Text(g,$"夜魇 {1-p:P1}  ({-shift:+0.00;-0.00;0.00}pp)",left+555,72,11,Color.FromArgb(255,151,160),true,280,22);
        var target=draft.Seats[draft.TargetSeat];
        Text(g,target.HasHero?"英雄已锁定 · 仅推荐技能 / 估算":target.HeroSlot==HeroSlotState.Unknown?"英雄槽待确认 · 暂停英雄推荐":"英雄未选 · 正增益 TOP 10 / 估算",left+835,74,8,Muted,false,260,20);
        Box(g,new RectangleF(left+15,98,1070,3),Color.FromArgb(120,213,89,113));Box(g,new RectangleF(left+15,98,(float)(1070*p),3),Rank[0]);
        if(recommendations.Count==0)Text(g,draft.Seats[draft.TargetSeat].Hero!=null&&draft.Seats[draft.TargetSeat].Skills.Count>=4?"该位置已选满英雄 + 4 个技能 · 可切换位置查看":"当前没有可确认的正增益英雄或技能 · 等待识别或手动校准",left+15,107,9,Muted);
        for(int i=0;i<Math.Min(3,recommendations.Count);i++)
        {
            var r=recommendations[i];double own=draft.TargetSeat<5?r.RadiantAfter:1-r.RadiantAfter;
            Text(g,$"{i+1:00} {OptionName(r.Item)} +{r.GainPp:0.00}pp → {(draft.TargetSeat<5?"天辉":"夜魇")} {own:P1}",left+15+i*357,107,9,RankColor(i),true,350,22);
        }
        g.TranslateTransform(0,60);
        var viewed=PlayerAreas.Card(draft.TargetSeat,size);
        Border(g,new RectangleF(viewed.X/scale,viewed.Y/scale,viewed.Width/scale,viewed.Height/scale),Color.FromArgb(180,Rank[1]),1.5f);
        Text(g,"正在查看推荐",viewed.X/scale,viewed.Y/scale-17,8,Rank[1],true,180,18);
        for(int i=0;i<Math.Min(10,recommendations.Count);i++)
        {
            var r=recommendations[i];
            foreach(var m in matches.Where(m=>m.Region.Role=="pool"&&m.Key==r.Item.Code))
            {
                var rect=m.Region.Rect(size);var rr=new RectangleF(rect.X/scale-3,rect.Y/scale-3,rect.Width/scale+6,Math.Max(rect.Height/scale,rect.Width/scale*.85f)+6);
                SkillHighlight(g,rr,r,i);
            }
        }
        var combos=engine.ComboOptions(draft);
        foreach(var r in combos.Where(c=>recommendations.All(r=>r.Item.Code!=c.Item.Code)))
            foreach(var m in matches.Where(m=>m.Region.Role=="pool"&&m.Key==r.Item.Code))
            {
                var rect=m.Region.Rect(size);var rr=new RectangleF(rect.X/scale-3,rect.Y/scale-3,rect.Width/scale+6,Math.Max(rect.Height/scale,rect.Width/scale*.85f)+6);
                Rainbow(g,rr);Box(g,new RectangleF(rr.X,rr.Bottom-14,rr.Width,14),Color.FromArgb(230,8,13,22));
                Text(g,r.Combos[0].Kind=="适配"?"适配":r.Combos[0].AlreadyPicked?"联动":"可追",rr.X,rr.Bottom-14,8,Ink,true,rr.Width,15);
            }
        if(combos.Count>0)
        {
            var details=combos.SelectMany(r=>r.Combos.Select(c=>(Option:r.Item,Hint:c))).DistinctBy(p=>string.Join("|",new[]{p.Option.Code,p.Hint.Partner}.Order())).Take(4).ToList();
            for(int side=0;side<2;side++)
            {
                var entries=details.Skip(side*2).Take(2).ToList();if(entries.Count==0)continue;
                var strip=ComboPanel(side);Glass(g,strip);Rainbow(g,new RectangleF(strip.X+6,strip.Y+9,3,14));
                Text(g,"联动 / 适配",strip.X+16,strip.Y+6,9,Rank[1],true,150,20);
                for(int row=0;row<entries.Count;row++)
                {
                    var (option,hint)=entries[row];float y=strip.Y+30+row*53;
                    Text(g,"可选 · "+hint.Label,strip.X+10,y,8,Muted,false,156,16);
                    Text(g,$"选：{option.Name}\n{(hint.AlreadyPicked?"配合已选：":"后续可选：")}{hint.Name}",strip.X+10,y+17,8.5f,Ink,true,156,36);
                }
            }
        }
        else
        {
            var strip=ComboPanel(0);Glass(g,new RectangleF(strip.X,strip.Y,strip.Width,62));
            Text(g,"联动提示",strip.X+10,strip.Y+7,9,Muted,true,156,20);
            Text(g,"暂无匹配的合法组合",strip.X+10,strip.Y+30,8,Muted,false,156,25);
        }
        Glass(g,new RectangleF(14,h-30,w-28,23));
        Text(g,"点击两侧玩家头像或名字切换推荐  ·  数字 = 增益前十 / 彩虹 = 联动或适配  ·  F8 显隐 / F9 暂停 / F10 控制台  |  "+status,22,h-27,8,Muted,false,w-44,22);
        g.Restore(saved);
    }
}

public sealed class OverlayForm:Form
{
    readonly Func<(Draft,List<Recommendation>,List<Recognition>,string)> state;
    readonly Engine engine;
    public bool CaptureExclusionAvailable { get; private set; }
    public bool LastLayeredRenderSucceeded { get; private set; }
    public int LastLayeredError { get; private set; }
    public OverlayForm(Engine engine,Func<(Draft,List<Recommendation>,List<Recognition>,string)> state)
    {
        this.engine=engine;this.state=state;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        DoubleBuffered=true;
    }
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80000|0x20|0x08000000|0x80;return p;}}
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        CaptureExclusionAvailable=OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)&&Native.SetWindowDisplayAffinity(Handle,0x11);
    }
    public void Present(Rectangle bounds)
    {
        bool changed=Bounds!=bounds||!Visible;
        if(Bounds!=bounds)Bounds=bounds;
        if(!Visible)Show();
        if(changed)RefreshFrame();
    }
    protected override void OnPaintBackground(PaintEventArgs e){}
    protected override void OnPaint(PaintEventArgs e){}
    public void RefreshFrame()
    {
        if(!IsHandleCreated||!Visible||ClientSize.Width<1||ClientSize.Height<1)return;
        using var surface=new Bitmap(ClientSize.Width,ClientSize.Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(surface)){g.Clear(Color.Transparent);var(d,r,m,s)=state();Art.Overlay(g,ClientSize,d,r,m,engine,s);}
        LastLayeredRenderSucceeded=Native.PaintLayered(Handle,surface,Location);
        LastLayeredError=LastLayeredRenderSucceeded?0:System.Runtime.InteropServices.Marshal.GetLastWin32Error();
    }
}

public sealed class DraftBoard:Control
{
    public Catalog Catalog=null!;public Draft Draft=null!;public Engine Engine=null!;
    public List<Recommendation> Recommendations=[];
    public Bitmap? Frame;
    public List<Recognition> Matches=[];
    public bool ShowFrame;
    public event Action<string>? ItemClicked;
    public event Action<int>? SeatClicked;
    readonly List<(RectangleF Rect,string Key)> hit=[];
    public DraftBoard(){DoubleBuffered=true;BackColor=Art.Bg;ResizeRedraw=true;}
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Catalog==null)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;hit.Clear();
        var selectable=Engine.SelectableDisplay(Draft,Recommendations);
        if(ShowFrame&&Frame!=null)
        {
            float scale=Math.Min((float)Width/Frame.Width,(float)Height/Frame.Height);
            var sz=new Size((int)(Frame.Width*scale),(int)(Frame.Height*scale));g.DrawImage(Frame,new Rectangle(Point.Empty,sz));
            Art.Overlay(g,sz,Draft,Recommendations,Matches,Engine,"截图预览 · 非实时游戏");return;
        }
        g.ScaleTransform(Width/1360f,Height/740f);
        Art.Text(g,$"{Art.SeatName(Draft.TargetSeat)}下一手 · 英雄 / 技能 TOP 10",20,10,9,Art.Muted,true);
        Art.Text(g,"01 红  /  02 橙  /  03–04 紫  /  05–07 蓝  /  08–10 绿  ·  彩虹 = COMBO",580,10,9,Art.Muted);
        for(int i=0;i<3;i++)
        {
            float x=20+i*445;var rect=new RectangleF(x,38,428,112);Art.Box(g,rect,Art.Panel);
            Art.Box(g,new RectangleF(x,38,4,112),Art.RankColor(i));
            if(i>=selectable.Count){Art.Text(g,"暂无正增益英雄或技能",x+20,66,14,Art.Muted);continue;}
            var r=selectable[i];Art.Icon(g,r.Item,new RectangleF(x+17,54,54,54));
            Art.Text(g,$"0{i+1} {Art.OptionName(r.Item)}",x+83,52,12,Art.RankColor(i),true,235,28);
            Art.Text(g,$"+{r.GainPp:0.00}pp",x+323,55,13,Art.RankColor(i),true,105,30);
            Art.Text(g,$"选后：天辉 {r.RadiantAfter:P1} / 夜魇 {1-r.RadiantAfter:P1}",x+83,84,9,Art.Muted,false,327,24);
            Art.Text(g,r.Combos.Count>0?string.Join(" / ",r.Combos.Select(c=>c.Label+"："+c.Name)):r.Reasons.FirstOrDefault()??"",x+17,119,9,Art.Muted,false,400,25);
            if(r.Combos.Count>0)Art.Rainbow(g,new RectangleF(x+16,53,56,56));
            hit.Add((rect,r.Item.Code));
        }
        double p=Engine.Estimate(Draft);
        Art.Text(g,$"天辉 {p:P1}",20,164,12,Art.Rank[0],true);
        Art.Text(g,"阵容强度折算 · 未经实战校准 · 不是 Windrun 对局预测",425,167,9,Art.Muted);
        Art.Text(g,$"夜魇 {1-p:P1}",1200,164,12,Color.FromArgb(244,142,153),true);
        Art.Box(g,new RectangleF(20,197,1320,5),Color.FromArgb(137,73,90));Art.Box(g,new RectangleF(20,197,(float)(1320*p),5),Art.Rank[0]);
        for(int side=0;side<2;side++)for(int row=0;row<5;row++)
        {
            int idx=side*5+row;var s=Draft.Seats[idx];float x=side==0?20:1090,y=227+row*91;
            Art.Box(g,new RectangleF(x,y,250,83),Art.Panel);
            if(idx==Draft.TargetSeat)Art.Border(g,new RectangleF(x,y,250,83),Art.Rank[1]);
            var hero=s.Hero is null?null:Catalog.All.GetValueOrDefault(s.Hero);
            Art.Text(g,$"{(side==0?"天辉":"夜魇")}{row+1}{(idx==Draft.Me?" · 我":"")}{(idx==Draft.TargetSeat?" · 查看":"")}  {hero?.Name??(s.HasHero?"英雄已选（待识别）":s.HeroSlot==HeroSlotState.Unknown?"英雄槽待确认":"英雄未选")}",x+9,y+6,10,idx==Draft.TargetSeat?Art.Rank[1]:Art.Ink,true,239,24);
            Art.Icon(g,hero,new RectangleF(x+9,y+34,38,38));
            for(int j=0;j<4;j++)
            {
                var skill=j<s.Skills.Count?Catalog.All.GetValueOrDefault(s.Skills[j]):null;
                Art.Icon(g,skill,new RectangleF(x+57+j*46,y+34,38,38));
            }
        }
        var available=Draft.Pool.Where(k=>Draft.Available(k)&&Catalog.All.ContainsKey(k)).Select(k=>Catalog.All[k]).OrderByDescending(i=>i.Hero).ThenByDescending(i=>i.Ultimate).ThenBy(i=>i.Id).ToList();
        var comboOptions=Engine.ComboOptions(Draft);
        Art.Text(g,$"可选池  /  {available.Count}      点击图标：为编辑位置选取或排除",293,224,10,Art.Muted);
        for(int i=0;i<Math.Min(available.Count,70);i++)
        {
            var a=available[i];float x=295+(i%10)*77,y=255+(i/10)*62;
            var rect=new RectangleF(x,y,44,44);Art.Icon(g,a,rect);
            int rank=selectable.FindIndex(r=>r.Item.Code==a.Code);
            if(rank>=0)Art.SkillHighlight(g,rect,selectable[rank],rank,true);
            else if(comboOptions.Any(r=>r.Item.Code==a.Code))Art.Rainbow(g,rect);
            Art.Text(g,a.Name,x-8,y+45,7,Art.Muted,false,72,16);hit.Add((new RectangleF(x-5,y,67,61),a.Code));
        }
        Art.Text(g,"估算走势",293,701,8,Art.Muted);
        if(Draft.History.Count>1)
        {
            using var pen=new Pen(Art.Rank[0],2);
            var history=Draft.History.TakeLast(50).ToArray();
            for(int i=1;i<history.Length;i++)g.DrawLine(pen,375+(i-1)*690f/(history.Length-1),720-(float)(history[i-1]-.3)*60,375+i*690f/(history.Length-1),720-(float)(history[i]-.3)*60);
        }
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        if(e.Button!=MouseButtons.Left)return;
        if(ShowFrame&&Frame!=null)
        {
            float scale=Math.Min((float)Width/Frame.Width,(float)Height/Frame.Height);
            int target=PlayerAreas.Hit(new Point((int)(e.X/scale),(int)(e.Y/scale)),Frame.Size);
            if(target>=0)SeatClicked?.Invoke(target);return;
        }
        var p=new PointF(e.X*1360f/Width,e.Y*740f/Height);
        for(int target=0;target<10;target++)if(new RectangleF(target<5?20:1090,227+target%5*91,250,83).Contains(p)){SeatClicked?.Invoke(target);return;}
        foreach(var h in hit)if(h.Rect.Contains(p)){ItemClicked?.Invoke(h.Key);return;}
    }
}
