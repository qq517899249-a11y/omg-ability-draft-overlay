using System.Text.Json;

namespace OmgAd;

public sealed class MainForm:Form
{
    readonly Catalog catalog;readonly Statistics statistics;readonly Engine engine;readonly Recognizer recognizer;readonly StateTracker tracker;
    Draft draft=new();Settings settings;List<Recommendation> recommendations=[];List<Recognition> matches=[];
    readonly DraftBoard board=new(){Dock=DockStyle.Fill};readonly Label status=new(){Dock=DockStyle.Bottom,Height=30,ForeColor=Art.Muted,Padding=new Padding(14,5,0,0)};
    readonly ComboBox me=new(){Width=110,DropDownStyle=ComboBoxStyle.DropDownList},seat=new(){Width=110,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly ComboBox viewSeat=new(){Width=110,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly ComboBox item=new(){Width=205,DropDownStyle=ComboBoxStyle.DropDown,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.ListItems};
    readonly CheckBox preview=new(){Text="显示原始截图",AutoSize=true,ForeColor=Art.Ink,Padding=new Padding(3,5,0,0)};
    readonly OverlayForm overlay;readonly System.Windows.Forms.Timer timer=new(){Interval=1200};
    readonly Stack<string> undo=[];
    PlayerClickListener? playerClicks;
    IntPtr trackedGame;
    bool live,busy,overlayEnabled=true,overlayAttached,demoMode;int revision;Rectangle gameBounds;Bitmap? frame;string recognitionStatus="先导入截图，或连接 Dota 2";
    RectangleF viewport=new(0,0,1,1);

    public MainForm(Catalog catalog,Statistics stats)
    {
        this.catalog=catalog;statistics=stats;engine=new(catalog,stats);recognizer=new(catalog);tracker=new(catalog,engine);
        try{settings=UserSettings.Load();}catch{settings=Settings.Reference();}
        settings.EnsureHeroNameRegions();
        Text="OMG AD · 技能征召助手 · 0.4.2 可选性优先";Size=new Size(1440,960);MinimumSize=new Size(1120,780);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",9);BackColor=Art.Bg;ForeColor=Art.Ink;
        var title=new Label{Text="OMG / ABILITY DRAFT       每一手，都有依据。",Dock=DockStyle.Top,Height=59,Padding=new Padding(18,16,0,0),Font=new Font(Font.FontFamily,17,FontStyle.Bold),ForeColor=Art.Rank[0]};
        var actions=new FlowLayoutPanel{Dock=DockStyle.Top,Height=45,Padding=new Padding(12,4,0,0)};
        actions.Controls.AddRange([
            Button("导入截图",async()=>await ImportScreenshot()),Button("连接 Dota 2",StartLive),Button("暂停 / 继续",ToggleLive),
            Button("校准识别格",Calibrate),Button("新的一局",NewDraft),Button("演示阵容",Demo),Button("撤销",Undo),Button("更新完整统计",async()=>await SyncStatistics()),Button("来源 / 算法",ShowSources),preview]);
        var edit=new FlowLayoutPanel{Dock=DockStyle.Top,Height=46,Padding=new Padding(12,4,0,0),WrapContents=false,AutoScroll=true};
        string[] seats=Enumerable.Range(0,10).Select(i=>(i<5?"天辉":"夜魇")+(i%5+1)).ToArray();me.Items.AddRange(seats);seat.Items.AddRange(seats);me.SelectedIndex=Math.Clamp(settings.MySeat,0,9);seat.SelectedIndex=me.SelectedIndex;draft.Me=me.SelectedIndex;
        item.Items.AddRange(catalog.All.Values.OrderBy(a=>a.Name).Cast<object>().ToArray());
        viewSeat.Items.AddRange(seats);viewSeat.SelectedIndex=me.SelectedIndex;
        edit.Controls.AddRange([Label("我的位置"),me,Label("查看推荐"),viewSeat,Button("回到我",()=>SelectViewSeat(draft.Me)),Label("编辑位置"),seat,item,Button("加到池",AddPool),Button("选取",PickManual),Button("清空位置",ClearSeat),Button("恢复禁用",ClearBans)]);
        viewSeat.SelectedIndexChanged+=(_,_)=>{revision++;draft.ViewSeat=viewSeat.SelectedIndex==draft.Me?null:viewSeat.SelectedIndex;seat.SelectedIndex=draft.TargetSeat;RefreshState();};
        me.SelectedIndexChanged+=(_,_)=>{revision++;draft.Me=me.SelectedIndex;draft.ViewSeat=null;SelectViewSeat(draft.Me);settings.MySeat=draft.Me;UserSettings.Save(settings);RefreshState();};
        preview.CheckedChanged+=(_,_)=>{board.ShowFrame=preview.Checked;board.Invalidate();};
        board.Catalog=catalog;board.Draft=draft;board.Engine=engine;board.ItemClicked+=ShowItemMenu;board.SeatClicked+=SelectViewSeat;
        Controls.Add(board);Controls.Add(status);Controls.Add(edit);Controls.Add(actions);Controls.Add(title);
        overlay=new(engine,()=> (draft,recommendations,matches,recognitionStatus));
        timer.Interval=Math.Clamp(settings.IntervalMs,600,10000);timer.Tick+=async(_,_)=>await TickLive();timer.Start();
        Shown+=(_,_)=>
        {
            playerClicks=new PlayerClickListener();
            playerClicks.Clicked+=point=>
            {
                if(!overlayAttached||!overlay.Visible||IsDisposed||Disposing)return;
                if(trackedGame==IntPtr.Zero||Native.GetForegroundWindow()!=trackedGame)return;
                int target=PlayerAreas.HitScreen(point,gameBounds);
                if(target>=0)BeginInvoke(()=>{if(!IsDisposed&&!Disposing)SelectViewSeat(target);});
            };
            var missing=new List<string>();
            foreach(var(id,key)in new[]{(1,Keys.F8),(2,Keys.F9),(3,Keys.F10)})if(!Native.RegisterHotKey(Handle,id,0x4000,(uint)key))missing.Add(key.ToString());
            for(int i=0;i<10;i++)if(!Native.RegisterHotKey(Handle,100+i,SeatShortcuts.Modifiers,(uint)SeatShortcuts.Key(i)))missing.Add(SeatShortcuts.Label(i));
            if(!Native.RegisterHotKey(Handle,110,SeatShortcuts.Modifiers,(uint)Keys.M))missing.Add("Ctrl+Alt+M");
            if(missing.Count>0)status.Text="热键被占用："+string.Join(", ",missing)+"；可使用控制台按钮。";
            if(!playerClicks.Installed)status.Text+=" · 点击切换不可用，请使用控制台或原热键。";
        };
        FormClosing+=(_,_)=>{playerClicks?.Dispose();timer.Stop();overlay.Close();for(int i=1;i<=3;i++)Native.UnregisterHotKey(Handle,i);for(int i=100;i<=110;i++)Native.UnregisterHotKey(Handle,i);frame?.Dispose();recognizer.Dispose();};
        RefreshState();
    }
    static Label Label(string text)=>new(){Text=text,AutoSize=true,Padding=new Padding(0,6,0,0)};
    static Button Button(string text,Action action)
    {
        var b=new Button{Text=text,AutoSize=true,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Art.Panel,ForeColor=Art.Ink,Padding=new Padding(5,0,5,0)};
        b.FlatAppearance.BorderColor=Color.FromArgb(44,62,78);b.Click+=(_,_)=>action();return b;
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0312)
        {
            int requested=SeatShortcuts.Seat(m.WParam.ToInt32());
            if(requested>=0)SelectViewSeat(requested);
            else if(m.WParam.ToInt32()==110)SelectViewSeat(draft.Me);
        }
        if(m.Msg==0x0312)switch(m.WParam.ToInt32())
        {case 1:overlayEnabled=!overlayEnabled;UpdateOverlayVisibility();break;case 2:ToggleLive();break;case 3:overlay.Hide();Show();WindowState=FormWindowState.Normal;Activate();break;}
        base.WndProc(ref m);
    }
    internal void SelectViewSeat(int target){if(target is >=0 and <10)viewSeat.SelectedIndex=target;}
    internal (int Me,int Target) CurrentSeats=>(draft.Me,draft.TargetSeat);
    internal void ShowConsole()
    {
        if(IsDisposed||Disposing)return;
        overlay.Hide();
        Show();
        WindowState=FormWindowState.Normal;
        BringToFront();
        Activate();
    }
    void Remember(){revision++;undo.Push(JsonSerializer.Serialize(draft,Store.Json));}
    void Undo(){revision++;if(undo.TryPop(out var s)){draft=JsonSerializer.Deserialize<Draft>(s,Store.Json)!;tracker.Reset();RefreshState();}}
    void RefreshState()
    {
        board.Draft=draft;board.Recommendations=recommendations=engine.RankOptions(draft);board.Matches=matches;board.Frame=frame;board.Invalidate();overlay?.RefreshFrame();
        var fetched=statistics.FetchedAt.Length>=10?statistics.FetchedAt[..10]:"未知";
        status.Text=$"{recognitionStatus}{CalibrationNotice()}  |  统计快照获取 {fetched} · 游戏版本 {statistics.Patch} · 组合评分含规则";
    }
    void NewDraft()
    {Remember();demoMode=false;draft=new Draft{Me=me.SelectedIndex};SelectViewSeat(draft.Me);tracker.Reset();matches=[];recognitionStatus="已开始新的一局，请读取选技界面";RefreshState();}
    public void Demo()
    {
        live=false;overlayAttached=false;demoMode=true;overlay.Hide();Remember();draft=new Draft{Me=me.SelectedIndex};SelectViewSeat(draft.Me);tracker.Reset();matches=[];
        string[] heroes=["lina","nevermore","windrunner","antimage","zuus","batrider","pudge","sven","luna","slardar","weaver","storm_spirit"];
        var bodies=heroes.Select(h=>"npc_dota_hero_"+h).Where(catalog.All.ContainsKey).ToList();
        var pool=new HashSet<string>(bodies);
        using var doc=JsonDocument.Parse(File.ReadAllText(Store.Data("draft_abilities.json")));
        foreach(var h in doc.RootElement.GetProperty("data").EnumerateArray())if(bodies.Contains(h.GetProperty("id").GetString()!))
            foreach(var group in new[]{"basic_abilities","ultimate_abilities"})foreach(var a in h.GetProperty(group).EnumerateArray())
                if(catalog.All.ContainsKey(a.GetProperty("id").GetString()!))pool.Add(a.GetProperty("id").GetString()!);
        draft.Pool=pool;draft.Seats[draft.Me].Skills=["earthshaker_aftershock"];
        int enemy=draft.Me<5?5:0;draft.Seats[enemy].Hero="npc_dota_hero_batrider";draft.Seats[enemy].Skills=["batrider_sticky_napalm"];
        draft.History=[.5,engine.Estimate(draft)];preview.Checked=false;recognitionStatus="英雄与技能混选 · Ctrl+Alt+1…0 查看各位置，Ctrl+Alt+M 回到我";RefreshState();
    }
    Item? SelectedItem()=>item.SelectedItem as Item??catalog.All.Values.FirstOrDefault(a=>a.Name==item.Text||a.Code==item.Text);
    void AddPool(){var a=SelectedItem();if(a==null)return;Remember();draft.Pool.Add(a.Code);draft.ManualPool.Add(a.Code);draft.Banned.Remove(a.Code);draft.Unavailable.Remove(a.Code);RefreshState();}
    void PickManual(){var a=SelectedItem();if(a==null)return;Pick(seat.SelectedIndex,a.Code);}
    void Pick(int target,string key)
    {
        Remember();draft.Pool.Add(key);
        draft.Unavailable.Remove(key); // Explicit manual correction; global ownership still applies.
        // An explicit console pick may supply the identity of an OCR-unknown occupied slot.
        // A seat with an already-known hero still cannot select a second model.
        if(catalog.All.TryGetValue(key,out var manual)&&manual.Hero&&draft.Seats[target].Hero is null)draft.Seats[target].HeroSlot=HeroSlotState.Empty;
        if(!engine.Pick(draft,target,key,out var error)){Undo();MessageBox.Show(this,error,"无法选取");return;}
        RefreshState();
    }
    void ClearSeat(){Remember();var s=draft.Seats[seat.SelectedIndex];foreach(var k in s.Skills){draft.Pool.Add(k);draft.Unavailable.Remove(k);}if(s.Hero!=null){draft.Pool.Add(s.Hero);draft.Unavailable.Remove(s.Hero);}draft.Seats[seat.SelectedIndex]=new();tracker.Reset();draft.History.Add(engine.Estimate(draft));RefreshState();}
    void ClearBans(){Remember();draft.Banned.Clear();RefreshState();}
    void ShowItemMenu(string key)
    {
        var a=catalog.All[key];var menu=new ContextMenuStrip();
        menu.Items.Add($"{a.Name} → {seat.SelectedItem}",null,(_,_)=>Pick(seat.SelectedIndex,key));
        menu.Items.Add("标为不可选 / 排除误识别",null,(_,_)=>{Remember();draft.Banned.Add(key);RefreshState();});
        menu.Items.Add("查看推荐依据",null,(_,_)=>{var r=recommendations.FirstOrDefault(r=>r.Item.Code==key);MessageBox.Show(this,r!=null?$"给当前查看位置选取后：天辉 {r.RadiantAfter:P1} / 夜魇 {1-r.RadiantAfter:P1}\n该阵营增益 +{r.GainPp:0.00} 个百分点（估算）\n\n"+string.Join("\n",r.Reasons.Concat(r.Combos.Select(c=>(c.AlreadyPicked?"已选联动：":"池内可追：")+c.Name+" · "+c.Reason))):statistics.Abilities.TryGetValue(key,out var s)?$"单技能统计胜率：{s.WinRate:P2}\n样本：{s.Samples:N0}\n平均顺位：{s.AvgPick:0.0}":"暂无统计。",a.Name);});
        menu.Show(Cursor.Position);
    }
    async Task ImportScreenshot()
    {
        if(busy)return;
        using var dialog=new OpenFileDialog{Filter="截图|*.png;*.jpg;*.jpeg;*.bmp"};if(dialog.ShowDialog()!=DialogResult.OK)return;
        if(demoMode)NewDraft();
        live=false;overlayAttached=false;overlay.Hide();using var raw=new Bitmap(dialog.FileName);var crop=Recognizer.FindViewport(raw);SetFrame(raw.Clone(crop,raw.PixelFormat));
        Remember();tracker.Reset();PrepareHeroScan();recognitionStatus="正在识别截图…";RefreshState();
        busy=true;int version=revision;
        try{using var scanFrame=new Bitmap(frame!);var result=await Task.Run(()=>recognizer.Scan(scanFrame,settings));if(version!=revision)return;matches=result;recognitionStatus="截图 · "+tracker.Apply(draft,matches,true);preview.Checked=true;SaveReport();}
        catch(Exception ex){recognitionStatus="识别失败："+ex.Message;}
        finally{busy=false;}
        RefreshState();
    }
    void SetFrame(Bitmap value){var old=frame;frame=value;settings.Activate(value.Size);board.Frame=frame;old?.Dispose();}
    string CalibrationNotice()
    {
        if(frame is null||settings.CalibrationWidth<=0||settings.CalibrationHeight<=0)return "";
        double oldRatio=(double)settings.CalibrationWidth/settings.CalibrationHeight,newRatio=(double)frame.Width/frame.Height;
        return $" · 布局：{settings.ActiveLayout}"+(Math.Abs(oldRatio-newRatio)>.03?"（比例差异较大，可手工校准一次）":"");
    }
    void Calibrate()
    {
        if(busy)return;
        live=false;overlayAttached=false;overlay.Hide();if(frame==null){MessageBox.Show(this,"先导入一张选技截图；或连接游戏采集一帧后按 F10 返回。","需要参考画面");return;}
        using var dialog=new CalibrationForm(frame,settings);dialog.ShowDialog(this);tracker.Reset();
        matches=recognizer.Scan(frame,settings);recognitionStatus=tracker.Apply(draft,matches,true);SaveReport();RefreshState();
    }
    internal void StartLive()
    {
        if(Native.FindDota()==IntPtr.Zero){recognitionStatus="未找到 Dota 2 窗口，请先启动游戏（无边框窗口）";RefreshState();return;}
        if(demoMode)NewDraft();
        tracker.Reset();PrepareHeroScan();live=true;overlayAttached=true;WindowState=FormWindowState.Minimized;recognitionStatus="等待 Dota 2 前台画面";RefreshState();
    }
    void PrepareHeroScan(){foreach(var seat in draft.Seats)if(!seat.HasHero)seat.HeroSlot=HeroSlotState.Unknown;}
    void ToggleLive(){if(!live&&demoMode)NewDraft();live=!live;if(live){overlayAttached=true;PrepareHeroScan();}recognitionStatus=live?"识别已开启，切回 Dota 2":"已暂停识别 · 保留上次结果";RefreshState();UpdateOverlayVisibility();}
    IntPtr UpdateOverlayVisibility()
    {
        var hwnd=trackedGame=Native.FindDota();
        if(!overlayAttached||!overlayEnabled||hwnd==IntPtr.Zero||Native.IsIconic(hwnd)||Native.GetForegroundWindow()!=hwnd)
        {if(overlay.Visible)overlay.Hide();return hwnd;}
        var bounds=Native.Bounds(hwnd);
        if(bounds.Width<640||bounds.Height<480){if(overlay.Visible)overlay.Hide();return hwnd;}
        gameBounds=new Rectangle(bounds.X+(int)(bounds.Width*viewport.X),bounds.Y+(int)(bounds.Height*viewport.Y),
            (int)(bounds.Width*viewport.Width),(int)(bounds.Height*viewport.Height));
        overlay.Present(gameBounds);
        return hwnd;
    }
    async Task TickLive()
    {
        // Window visibility is independent of capture, confidence and pause state.
        var hwnd=UpdateOverlayVisibility();
        if(busy)return;
        if(!live||hwnd==IntPtr.Zero||Native.IsIconic(hwnd)||Native.GetForegroundWindow()!=hwnd)return;
        var bounds=Native.Bounds(hwnd);if(bounds.Width<640||bounds.Height<480)return;
        // Exclude the overlay from Windows capture rather than hiding it for each frame.
        // If exclusion is unsupported, freeze the results and explain instead of flashing.
        _=overlay.Handle;
        if(!overlay.CaptureExclusionAvailable)
        {live=false;recognitionStatus="截图排除不可用 · 已暂停识别并保留结果，可导入截图";RefreshState();return;}
        busy=true;
        try
        {
            using var raw=Native.Capture(bounds);var crop=Recognizer.FindViewport(raw);SetFrame(raw.Clone(crop,raw.PixelFormat));
            viewport=new RectangleF((float)crop.X/bounds.Width,(float)crop.Y/bounds.Height,(float)crop.Width/bounds.Width,(float)crop.Height/bounds.Height);
            UpdateOverlayVisibility();
            using var scanFrame=new Bitmap(frame!);
            int version=revision;var result=await Task.Run(()=>recognizer.Scan(scanFrame,settings));
            if(IsDisposed||Disposing||version!=revision||!live)return;
            recognitionStatus=tracker.Apply(draft,result);
            if(recognizer.OcrError is string error)recognitionStatus+=" · 英雄 OCR："+error;
            if(tracker.LastFrameCommitted)matches=result;
            else recognitionStatus+=" · 保留上次结果";
            RefreshState();
            UpdateOverlayVisibility();
        }
        catch(Exception ex){if(!IsDisposed&&!Disposing){recognitionStatus="本轮采集失败 · 保留上次结果："+ex.Message;RefreshState();}}
        finally{busy=false;}
    }
    void SaveReport()=>Store.Save("recognition-report.json",new{ocrError=recognizer.OcrError,heroText=recognizer.OcrText,matches=matches.Select(r=>new{r.Index,r.Region.Role,r.Region.Seat,r.Key,r.Confidence,r.Margin})});
    async Task SyncStatistics()
    {
        if(busy)return;busy=true;live=false;recognitionStatus="准备连接 Windrun…";RefreshState();
        try
        {
            var progress=new Progress<SyncProgress>(p=>{recognitionStatus=p.Message;status.Text=p.Message+" · 下载期间继续保留旧统计";});
            var updated=await WindrunSync.Download(progress);
            Store.Save("statistics.json",updated);statistics.ReplaceWith(updated);
            engine.RebuildIndexes();
            foreach(var a in catalog.Abilities)if(statistics.Abilities.TryGetValue(a.Code,out var s))a.Ultimate=s.Ultimate;
            recognitionStatus=$"Windrun 更新完成：{statistics.Abilities.Count} 技能 / {statistics.Heroes.Count} 英雄 / {statistics.Pairs.Count} 二元组合 / {statistics.Triplets.Count} 三元组合";
            RefreshState();MessageBox.Show(this,recognitionStatus+"\n数据已原子保存并立即生效。","完整统计已接通");
        }
        catch(Exception ex){recognitionStatus="统计更新失败，继续使用旧库";RefreshState();MessageBox.Show(this,ex.Message,"Windrun 同步失败");}
        finally{busy=false;}
    }
    void ShowSources()=>MessageBox.Show(this,
        $"数据来源\n{statistics.Source}\n{statistics.Attribution}\n获取时间：{statistics.FetchedAt}\n统计所属补丁：{statistics.Patch}\n\n"+
        $"现有 {statistics.Abilities.Count} 个技能统计、{statistics.Heroes.Count} 个英雄、{statistics.Pairs.Count} 组二元组合、{statistics.Triplets.Count} 组三元组合。\n"+
        "高亮 = 给“查看推荐”位置逐个模拟选取英雄或技能，按该阵营估算胜率增益排序的前 10 项；每手只能选一个，已有英雄不再推荐英雄。\n"+
        "pp 表示百分点，不是相对百分比。相同增益时用原推荐分排序。\n"+
        "红 1 / 橙 2 / 紫 3–4 / 蓝 5–7 / 绿 8–10；彩虹独立显示合法联动候选，包括前十之外的选项。英雄适配会单独标注，不代表专属触发。\n"+
        "没有实证数据的组合只使用少量显式规则，不代表统计胜率提升。英雄本体数据来自 api.windrun.io/ability-by-hero 搜索缓存；版本、样本和统计时间窗未知，缺数据保持中性。\n\n"+
        "双方胜率是阵容强度的逻辑函数折算，未经历史对局训练或校准；早期信息不全时会接近 50%。\n"+
        "本机截屏、图标匹配 + 玩家 ID 上方英雄名 OCR、独立透明窗口；需要无边框窗口。F8 显隐 / F9 暂停 / F10 控制台。\n"+
        "直接点击游戏两侧玩家的头像或名字切换推荐视角；控制台和截图预览也可点击。原 Ctrl+Alt 数字热键仍可用，不会改动保存的我的位置。\n"+
        "当前不会自动判断轮到谁，请设置“我的位置”。每局先点“新的一局”。标准模式无独立 ban 阶段，排除功能用于不可选项和纠错。",
        "OMG AD · 数据与原型边界");
}
