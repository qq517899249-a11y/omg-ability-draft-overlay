using System.Diagnostics;
using System.Text.Json;

namespace OmgAd;
public static class SelfTest
{
    public static int Run(string[] args)
    {
        var output=Path.GetFullPath(args.SkipWhile(a=>a!="--out").Skip(1).FirstOrDefault()??"artifacts");Directory.CreateDirectory(output);
        var log=new List<string>();int fail=0;
        void Check(string name,bool ok){log.Add($"{(ok?"PASS":"FAIL")} {name}");if(!ok)fail++;}
        try
        {
            var c=Catalog.Load();var stats=Statistics.Load();var engine=new Engine(c,stats);using var vision=new Recognizer(c);
            var autoLayout=Settings.Reference();autoLayout.Activate(new Size(1920,1080));Check("resolution profile auto-scales same-aspect viewport",autoLayout.Regions.Count==110&&autoLayout.ActiveLayout.Contains("自动缩放"));
            autoLayout.Regions[0].X=.123f;autoLayout.SaveProfile(new Size(1920,1080));autoLayout.Activate(new Size(1920,1080));Check("exact resolution calibration restores automatically",Math.Abs(autoLayout.Regions[0].X-.123f)<1e-6&&autoLayout.ActiveLayout.Contains("精确校准"));
            Check("catalog and local assets",c.Abilities.Count==513&&c.Heroes.Count==127&&c.All.Values.All(a=>a.Image!=null));
            Check("statistics are bounded and have provenance",stats.Abilities.Count>500&&stats.Abilities.Values.All(a=>a.WinRate>0&&a.WinRate<1&&a.Samples>0)&&stats.Source.StartsWith("http"));
            var keys=new[]{"storm_spirit_ball_lightning","earthshaker_aftershock","lina_dragon_slave","lina_light_strike_array","lina_fiery_soul","zuus_arc_lightning","luna_eclipse","npc_dota_hero_lina"};
            Draft Fresh()=>new(){Pool=keys.ToHashSet(),Me=0};
            var d=Fresh();d.Seats[0].Skills.Add("earthshaker_aftershock");
            var rec=engine.Recommend(d,50);var ball=rec.Single(r=>r.Item.Code=="storm_spirit_ball_lightning");
            var neutral=engine.Recommend(Fresh(),50).Single(r=>r.Item.Code=="storm_spirit_ball_lightning");
            Check("selected ability changes combo score",Math.Abs(ball.Score-neutral.Score)>.01);
            Check("picked abilities excluded",rec.All(r=>r.Item.Code!="earthshaker_aftershock"));
            d.Banned.Add("storm_spirit_ball_lightning");Check("excluded abilities never recommended",engine.Recommend(d,50).All(r=>r.Item.Code!="storm_spirit_ball_lightning"));
            d=Fresh();d.Seats[0].Skills=["luna_eclipse"];Check("one ultimate limit",!engine.Legal(d,d.Seats[0],c.All["storm_spirit_ball_lightning"]));
            d=Fresh();d.Seats[0].Skills=["lina_dragon_slave","lina_light_strike_array","lina_fiery_soul"];Check("reserve ultimate slot",!engine.Legal(d,d.Seats[0],c.All["zuus_arc_lightning"])&&engine.Legal(d,d.Seats[0],c.All["luna_eclipse"]));
            d=Fresh();d.Seats[1].Skills=["zuus_arc_lightning"];Check("global uniqueness",!engine.Pick(d,0,"zuus_arc_lightning",out _));
            d=Fresh();d.Pool.UnionWith(["jakiro_liquid_fire","jakiro_liquid_ice"]);d.Seats[0].Skills=["jakiro_liquid_fire"];Check("mutually exclusive abilities",!engine.Legal(d,d.Seats[0],c.All["jakiro_liquid_ice"]));
            d=Fresh();Check("neutral incomplete draft estimate",Math.Abs(engine.Estimate(d)-.5)<1e-8);
            d.Seats[0].Skills=["earthshaker_aftershock"];double p=engine.Estimate(d);(d.Seats[0],d.Seats[5])=(d.Seats[5],d.Seats[0]);Check("team symmetry",Math.Abs(engine.Estimate(d)-(1-p))<1e-8);
            Check("top 3 deterministic",engine.Recommend(Fresh()).Select(r=>r.Item.Code).SequenceEqual(engine.Recommend(Fresh()).Select(r=>r.Item.Code))&&engine.Recommend(Fresh()).Count==3);
            d=Fresh();d.Seats[5].Skills=["earthshaker_aftershock"];
            Check("opponent picks change denial score",engine.Recommend(d,50).Single(r=>r.Item.Code=="storm_spirit_ball_lightning").Score>neutral.Score);
            d=Fresh();d.Pool.Add("ursa_fury_swipes");d.Seats[0].Hero="npc_dota_hero_lina";
            var ranged=engine.Recommend(d,50).Single(r=>r.Item.Code=="ursa_fury_swipes").Score;
            d.Seats[0].Hero="npc_dota_hero_sven";Check("hero type changes fit score",ranged>engine.Recommend(d,50).Single(r=>r.Item.Code=="ursa_fury_swipes").Score);
            var unknown=new Statistics();var emptyEngine=new Engine(c,unknown);Check("missing stats remain neutral and explicitly unknown",emptyEngine.Recommend(Fresh(),50).All(r=>r.WinRate==null));
            d=new Draft{Pool=c.All.Keys.ToHashSet(),Me=0};
            var serialized=JsonSerializer.Serialize(d,Store.Json);var ranked=engine.RankOptions(d);
            Check("top ten mixes legal positive-gain heroes and skills",ranked.Count==10&&ranked.Any(r=>r.Item.Hero)&&ranked.Any(r=>!r.Item.Hero)&&ranked.All(r=>r.GainPp>0&&engine.Legal(d,d.Seats[0],r.Item)));
            Check("top ten is ordered by projected own-team gain",ranked.Zip(ranked.Skip(1)).All(p=>p.First.GainPp>=p.Second.GainPp));
            Check("forecast does not mutate draft or history",JsonSerializer.Serialize(d,Store.Json)==serialized);
            var forecast=ranked[0];engine.Pick(d,0,forecast.Item.Code,out _);
            Check("projected probability equals probability after actual pick",Math.Abs(engine.Estimate(d)-forecast.RadiantAfter)<1e-10);
            d=new Draft{Pool=c.All.Keys.ToHashSet(),Me=5};var dire=engine.RankOptions(d)[0];
            Check("dire gain uses dire probability and both teams sum to one",dire.RadiantAfter<.5&&Math.Abs(dire.GainPp-(.5-dire.RadiantAfter)*100)<1e-10);
            d=Fresh();d.Seats[0].Skills=["earthshaker_aftershock"];
            var combo=engine.RankOptions(d).Single(r=>r.Item.Code=="storm_spirit_ball_lightning");
            Check("already selected synergy produces ready combo",combo.Combos.Any(c=>c.Partner=="earthshaker_aftershock"&&c.AlreadyPicked));
            d=Fresh();var chase=engine.RankOptions(d).Single(r=>r.Item.Code=="storm_spirit_ball_lightning");
            Check("available legal partner produces chase combo",chase.Combos.Any(c=>c.Partner=="earthshaker_aftershock"&&!c.AlreadyPicked));
            d.Banned.Add("earthshaker_aftershock");
            Check("unavailable partner never receives combo reminder",engine.RankOptions(d).All(r=>r.Combos.All(c=>c.Partner!="earthshaker_aftershock")));
            Check("no positive evidence yields no highlighted skills",emptyEngine.RankOptions(Fresh()).Count==0);
            d=new Draft{Pool=["npc_dota_hero_lina","npc_dota_hero_nevermore","lina_light_strike_array","lina_dragon_slave"],Me=0};
            var opener=engine.RankOptions(d);Check("Lina and Shadow Fiend compete for the first pick",opener.Count>=2&&opener.Take(2).All(r=>r.Item.Hero));
            int actions=d.Seats[0].Skills.Count+(d.Seats[0].Hero==null?0:1);engine.Pick(d,0,opener[0].Item.Code,out _);
            Check("one action picks one hero without granting its spells",d.Seats[0].Skills.Count+(d.Seats[0].Hero==null?0:1)==actions+1&&d.Seats[0].Skills.Count==0);
            Check("owned hero excludes all other hero candidates",engine.RankOptions(d).All(r=>!r.Item.Hero));
            var heroTracker=new StateTracker(c,engine);d=Fresh();
            Recognition HeroFrame(string? key,HeroSlotState slot)=>new(900,new Region{Role="hero-name",Seat=0},key,1,1){HeroSlot=slot};
            heroTracker.Apply(d,[HeroFrame(null,HeroSlotState.Unknown)]);
            Check("unreadable hero slot suppresses all hero recommendations",!d.Seats[0].HasHero&&engine.Recommend(d,100).All(r=>!r.Item.Hero)&&engine.RankOptions(d).All(r=>!r.Item.Hero));
            Check("unknown hero slot still permits legal skills",engine.Legal(d,d.Seats[0],c.All["lina_dragon_slave"]));
            heroTracker.Apply(d,[HeroFrame(null,HeroSlotState.Empty)]);
            Check("one empty OCR frame cannot reopen hero recommendations",!engine.Legal(d,d.Seats[0],c.All["npc_dota_hero_lina"]));
            heroTracker.Apply(d,[HeroFrame(null,HeroSlotState.Empty)]);
            Check("confirmed empty slot permits first hero pick",engine.Legal(d,d.Seats[0],c.All["npc_dota_hero_lina"]));
            heroTracker.Apply(d,[HeroFrame("npc_dota_hero_nevermore",HeroSlotState.Occupied)]);
            Check("hero detection immediately locks model before identity stabilizes",d.Seats[0].HasHero&&d.Seats[0].Hero==null&&engine.Recommend(d,100).All(r=>!r.Item.Hero));
            Check("occupied unknown identity cannot produce future hero combo hints",engine.ComboOptions(d).All(r=>!r.Item.Hero&&r.Combos.All(h=>!c.All[h.Partner].Hero)));
            heroTracker.Apply(d,[HeroFrame("npc_dota_hero_nevermore",HeroSlotState.Occupied)]);
            Check("single hero observation commits without six pool cards",d.Seats[0].Hero=="npc_dota_hero_nevermore");
            heroTracker.Apply(d,[HeroFrame(null,HeroSlotState.Unknown)]);
            heroTracker.Apply(d,[HeroFrame(null,HeroSlotState.Empty)]);heroTracker.Apply(d,[HeroFrame(null,HeroSlotState.Empty)]);
            Check("OCR failure and stale empty labels never unlock a picked hero",d.Seats[0].HasHero&&d.Seats[0].Hero=="npc_dota_hero_nevermore"&&!engine.Pick(d,0,"npc_dota_hero_lina",out _));
            d.ViewSeat=1;Check("switching to an unpicked player still allows that player's first hero",engine.Recommend(d,100).Any(r=>r.Item.Hero));
            d.ViewSeat=0;Check("switching back retains occupied hero lock",engine.Recommend(d,100).All(r=>!r.Item.Hero));
            var layout=Settings.Reference();var design=new Size(1676,942);
            Check("side combo panels avoid cards players and chat",Enumerable.Range(0,2).All(side=>
            {var box=Art.ComboPanel(side);return box.Bottom<300&&!layout.Regions.Where(r=>r.Role=="pool").Any(r=>box.IntersectsWith(r.Rect(design)))&&!Enumerable.Range(0,10).Any(i=>box.IntersectsWith(PlayerAreas.Card(i,design)));}));
            d=Fresh();d.ViewSeat=5;var teammate=engine.RankOptions(d);Check("viewing dire changes forecast direction without changing my seat",d.Me==0&&teammate.All(r=>r.RadiantAfter<.5));
            Check("ten shortcut keys map uniquely to all ten seats",Enumerable.Range(0,10).Select(SeatShortcuts.Key).Distinct().Count()==10&&Enumerable.Range(0,10).All(i=>SeatShortcuts.Seat(100+i)==i)&&SeatShortcuts.Key(9)==Keys.D0);
            foreach(var size in new[]{new Size(1920,1080),new Size(2560,1440)})
            {
                bool all=Enumerable.Range(0,10).All(i=>{var r=PlayerAreas.Card(i,size);return PlayerAreas.Hit(new Point((int)(r.X+r.Width/2),(int)(r.Y+20)),size)==i;});
                Check($"click maps all ten player names at {size.Width}",all);
                var view=new Rectangle(230,17,size.Width,size.Height);var card=PlayerAreas.Card(7,size);
                Check($"click mapping accounts for viewport offset at {size.Width}",PlayerAreas.HitScreen(new Point(view.X+(int)card.X+30,view.Y+(int)card.Y+20),view)==7);
                Check($"pool and drafted skill clicks do not switch view at {size.Width}",PlayerAreas.Hit(new Point(size.Width/2,size.Height/2),size)==-1&&PlayerAreas.Hit(new Point((int)(200f/1676*size.Width),(int)(220f/942*size.Height)),size)==-1);
            }
            using(var clicks=new PlayerClickListener())Check("native non-consuming click listener installs",clicks.Installed);
            d=new Draft{Me=0,Pool=["npc_dota_hero_lina","ursa_fury_swipes"]};d.Seats[0].Skills=["ursa_fury_swipes"];
            Check("hero candidate displays existing skill fit",engine.ComboOptions(d).Single().Combos.Any(h=>h.Kind=="适配"&&h.AlreadyPicked));
            d=new Draft{Me=0,Pool=["npc_dota_hero_lina","ursa_fury_swipes"]};d.Seats[0].Hero="npc_dota_hero_lina";
            Check("skill candidate displays existing hero fit",engine.CombosFor(d,c.All["ursa_fury_swipes"]).Any(h=>h.Partner=="npc_dota_hero_lina"&&h.Kind=="适配"&&h.AlreadyPicked));
            d=new Draft{Me=0,Pool=["npc_dota_hero_lina","ursa_fury_swipes"]};
            Check("unselected hero fit is labeled future only",engine.ComboOptions(d).All(r=>r.Combos.All(h=>!h.AlreadyPicked)));
            d.Seats[0].Hero="npc_dota_hero_sven";
            Check("owned hero prevents future second-hero hints",engine.ComboOptions(d).All(r=>r.Combos.All(h=>h.Partner!="npc_dota_hero_lina")));
            var hintStats=new Statistics{Abilities=new(){["storm_spirit_ball_lightning"]=new(){WinRate=.2,Samples=10000}}};var hintEngine=new Engine(c,hintStats);
            d=new Draft{Me=0,Pool=["storm_spirit_ball_lightning"]};d.Seats[0].Skills=["earthshaker_aftershock"];
            Check("combo outside positive top ten remains visible as an unranked hint",hintEngine.RankOptions(d).Count==0&&hintEngine.ComboOptions(d).Single().Combos.Any(h=>h.AlreadyPicked));
            d.Banned.Add("storm_spirit_ball_lightning");Check("banned combo candidates stay hidden",hintEngine.ComboOptions(d).Count==0);
            // Regression: Leshrac is already picked, Rubick has strong synergy but is unavailable.
            const string lesh="npc_dota_hero_leshrac",rubick="npc_dota_hero_rubick",spell="lina_dragon_slave";
            var comboStats=new Statistics{Pairs=[new(){A=rubick,B=spell,SynergyPp=90,Samples=100000,Source="test fixture"},new(){A=lesh,B=spell,SynergyPp=8,Samples=10000,Source="test fixture"}]};
            var strict=new Engine(c,comboStats);d=new Draft{Me=0,Pool=[rubick,spell]};
            var staleOptions=strict.ComboOptions(d);
            Check("Rubick synergy fixture has hero and future-hero hints before picks",staleOptions.Any(r=>r.Item.Code==rubick)&&staleOptions.Any(r=>r.Combos.Any(h=>h.Partner==rubick)));
            d.Seats[0].Hero=lesh;d.Seats[0].HeroSlot=HeroSlotState.Occupied;d.Seats[5].Hero=rubick;
            var strictOptions=strict.ComboOptions(d);var display=strict.SelectableDisplay(d,staleOptions);
            Check("Leshrac pick overrides strong Rubick synergy in every output",strict.Recommend(d,100).All(r=>!r.Item.Hero)&&strict.RankOptions(d).All(r=>!r.Item.Hero)&&strictOptions.All(r=>!r.Item.Hero&&r.Combos.All(h=>h.Partner!=rubick)));
            Check("stale rainbow and cached combo text cannot revive Rubick",display.All(r=>r.Item.Code!=rubick&&r.Combos.All(h=>h.Partner!=rubick)));
            Check("own selected Leshrac remains context for a selectable skill",strictOptions.Single(r=>r.Item.Code==spell).Combos.Any(h=>h.Partner==lesh&&h.AlreadyPicked));
            d.Seats[6].Skills=[spell];Check("skill picked by another player vanishes from combos and cache",strict.ComboOptions(d).Count==0&&strict.SelectableDisplay(d,strictOptions).Count==0);
            d=new Draft{Me=0,Pool=[rubick,spell]};d.Seats[5].Hero=rubick;
            Check("unpicked player cannot chase a hero already owned by someone else",strict.ComboOptions(d).All(r=>r.Item.Code!=rubick&&r.Combos.All(h=>h.Partner!=rubick)));
            var availabilityTracker=new StateTracker(c,strict);d=new Draft{Me=0,Pool=[rubick,spell],ManualPool=[rubick]};
            var initialPool=new List<Recognition>{new(0,new Region{Role="pool"},rubick,1,1),new(1,new Region{Role="pool"},spell,1,1),new(2,new Region{Role="pool"},"lina_light_strike_array",1,1)};
            availabilityTracker.Apply(d,initialPool,true);
            var dimmed=initialPool.Select(m=>m.Index==0?m with{Key=null,Candidate=null}:m).ToList();availabilityTracker.Apply(d,dimmed);
            Check("dimmed card is withdrawn even below six cards and despite manual pool",!d.Available(rubick)&&strict.ComboOptions(d).All(r=>r.Item.Code!=rubick&&r.Combos.All(h=>h.Partner!=rubick)));
            availabilityTracker.Apply(d,initialPool);
            Check("uncertain visual exclusion recovers when card is clearly available again",d.Available(rubick));
            availabilityTracker.Apply(d,[new(901,new Region{Role="hero-name",Seat=5},rubick,1,1){HeroSlot=HeroSlotState.Occupied}]);
            Check("first observed ownership excludes hero before confirmation delay",!d.Available(rubick)&&strict.ComboOptions(d).All(r=>r.Item.Code!=rubick&&r.Combos.All(h=>h.Partner!=rubick)));
            d.Seats[0].Hero=lesh;d.Seats[0].HeroSlot=HeroSlotState.Occupied;
            availabilityTracker.Apply(d,[new(902,new Region{Role="hero-name",Seat=0},rubick,1,1){HeroSlot=HeroSlotState.Occupied}],true);
            Check("later OCR cannot replace a confirmed Leshrac with Rubick",d.Seats[0].Hero==lesh);
            // Exact template fixture exercises actual image matching, not only state mocks.
            using var fixture=new Bitmap(800,100);using(var g=Graphics.FromImage(fixture))
            {g.Clear(Color.Black);for(int i=0;i<keys.Length;i++)g.DrawImage(c.All[keys[i]].Image!,new Rectangle(i*100,0,90,90));}
            var config=new Settings();for(int i=0;i<keys.Length;i++)config.Regions.Add(new Region{X=i/8f,Y=0,W=90/800f,H=.9f});
            var found=vision.Scan(fixture,config);Check("template recognition fixture",found.Select(r=>r.Key).SequenceEqual(keys));
            using var blank=new Bitmap(800,100);Check("blank frames rejected",vision.Scan(blank,config).All(r=>r.Key==null));
            var tracker=new StateTracker(c,engine);d=new();tracker.Apply(d,found);Check("first frame not committed",d.Pool.Count==0);tracker.Apply(d,found);Check("stable second frame committed",d.Pool.Count==keys.Length);
            tracker.Apply(d,vision.Scan(blank,config));Check("blank frame preserves draft",d.Pool.Count==keys.Length);
            Check("unusable frame is not committed to displayed highlights",!tracker.LastFrameCommitted);
            var choices=new List<Recognition>(found){new(100,new Region{Role="pick",Seat=0,Slot=0},"earthshaker_aftershock",.99,.3)};
            tracker.Apply(d,choices,true);Check("player slot updates ownership",d.Seats[0].Skills.Contains("earthshaker_aftershock")&&!d.Available("earthshaker_aftershock"));
            d.ManualPool.Add("ursa_fury_swipes");tracker.Apply(d,found,true);Check("manual missing icon persists through scans",d.Pool.Contains("ursa_fury_swipes"));
            Check("stable usable frame can update displayed highlights",tracker.LastFrameCommitted);
            // Exercise a real layered HWND off-screen, keeping the user's desktop untouched.
            using(var resident=new OverlayForm(engine,()=> (d,engine.RankOptions(d),found,"常驻覆盖层回归验证")))
            {
                var bounds=new Rectangle(-20000,-20000,1280,720);int visibilityChanges=0;
                resident.VisibleChanged+=(_,_)=>visibilityChanges++;
                resident.Present(bounds);var handle=resident.Handle;
                for(int i=0;i<12;i++){resident.Present(bounds);resident.RefreshFrame();Application.DoEvents();}
                Check("overlay refresh keeps same visible window without hide/show",resident.Visible&&resident.Handle==handle&&visibilityChanges==1);
                Check("per-pixel alpha overlay is accepted by Windows compositor",resident.LastLayeredRenderSucceeded);
                log.Add($"LAYERED COMPOSITOR ERROR: {resident.LastLayeredError}");
                log.Add($"CAPTURE EXCLUSION: {resident.CaptureExclusionAvailable}");
                resident.Hide();resident.Present(bounds);
                Check("explicit overlay toggle still works",resident.Visible&&visibilityChanges==3);
                resident.Hide();
            }
            var screenshot=args.SkipWhile(a=>a!="--image").Skip(1).FirstOrDefault();
            if(screenshot!=null)
            {
                using var raw=new Bitmap(screenshot);var crop=Recognizer.FindViewport(raw);using var image=raw.Clone(crop,raw.PixelFormat);
                var settings=Settings.Reference();var watch=Stopwatch.StartNew();var matches=vision.Scan(image,settings);watch.Stop();
                using(var contact=new Bitmap(1200,600))
                {
                    using var cg=Graphics.FromImage(contact);cg.Clear(Art.Bg);
                    for(int i=0;i<60;i++)
                    {
                        var m=matches[i];int x=(i%12)*100,y=(i/12)*120;cg.DrawImage(image,new Rectangle(x,y,90,76),m.Region.Rect(image.Size),GraphicsUnit.Pixel);
                        Art.Text(cg,$"{i} / {m.Confidence:0.00}/{m.Margin:0.00}",x,y+78,8);Art.Text(cg,m.Candidate==null?"?":c.All[m.Candidate].Name,x,y+96,8,m.Key==null?Color.Orange:Art.Rank[0]);
                    }
                    contact.Save(Path.Combine(output,"recognition-crops.png"));
                }
                var state=new Draft();var tracker2=new StateTracker(c,engine);string result=tracker2.Apply(state,matches,true);
                log.Add($"REFERENCE {image.Width}x{image.Height}; {result}; scan={watch.ElapsedMilliseconds}ms");
                File.WriteAllText(Path.Combine(output,"reference-recognition.json"),JsonSerializer.Serialize(matches.Select(r=>new{r.Index,r.Region.Role,r.Key,Name=r.Key==null?null:c.All[r.Key].Name,r.Confidence,r.Margin}),Store.Json));
                using var rendered=new Bitmap(image);using(var g=Graphics.FromImage(rendered))
                {
                    Art.Overlay(g,image.Size,state,engine.RankOptions(state),matches,engine,"截图识别验证 · 统计快照 + 规则评分");
                }
                rendered.Save(Path.Combine(output,"reference-overlay.png"));
                state.Seats[state.Me].Skills=["earthshaker_aftershock"];
                using(var comboPreview=new Bitmap(image))
                {
                    using(var g=Graphics.FromImage(comboPreview))Art.Overlay(g,image.Size,state,engine.RankOptions(state),matches,engine,"联动样式演示：假设我方已选余震（非实战状态）");
                    comboPreview.Save(Path.Combine(output,"combo-overlay-preview.png"));
                }
                Check("reference finds enough pool icons for a draft",matches.Count(r=>r.Region.Role=="pool"&&r.Key!=null)>=40);
                var landmarks=new Dictionary<int,string>{{0,"luna_eclipse"},{1,"sniper_assassinate"},{3,"storm_spirit_ball_lightning"},{10,"alchemist_chemical_rage"},{12,"npc_dota_hero_luna"},{13,"luna_lucent_beam"},{20,"npc_dota_hero_sniper"},{31,"tidehunter_anchor_smash"},{53,"lina_dragon_slave"},{54,"lina_light_strike_array"}};
                Check("ten manually inspected reference landmarks",landmarks.All(p=>matches[p.Key].Key==p.Value));
                Check("reference empty player slots stay unknown",matches.Where(r=>r.Region.Role!="pool").All(r=>r.Key==null));
            }
            var lateScreenshot=args.SkipWhile(a=>a!="--late-image").Skip(1).FirstOrDefault();
            if(lateScreenshot!=null)
            {
                using var late=new Bitmap(lateScreenshot);var configLate=Settings.Reference();var match=vision.Scan(late,configLate);
                log.Add("OCR ERROR: "+(vision.OcrError??"none"));
                foreach(var pair in vision.OcrText)log.Add($"OCR seat {pair.Key}: {pair.Value}");
                File.WriteAllText(Path.Combine(output,"late-recognition.json"),JsonSerializer.Serialize(match.Select(r=>new{r.Index,r.Region.Role,r.Region.Seat,r.Region.Slot,r.Key,r.Candidate,r.Confidence}),Store.Json));
                string[] expected=["nevermore","death_prophet","shredder","bristleback","spectre","brewmaster","naga_siren","sand_king","leshrac","clinkz"];
                Check("ten real hero names recognized above player IDs",Enumerable.Range(0,10).All(i=>match.Any(r=>r.Region.Role=="hero-name"&&r.Region.Seat==i&&r.Key=="npc_dota_hero_"+expected[i])));
                var state=new Draft();var trackerLate=new StateTracker(c,engine);log.Add("LATE "+trackerLate.Apply(state,match,true));
                Check("late draft updates all ten hero identities",Enumerable.Range(0,10).All(i=>state.Seats[i].Hero=="npc_dota_hero_"+expected[i]));
                Check("late draft records four skills at all ten seats",state.Seats.All(s=>s.Skills.Count==4));
                Check("dimmed picked pool cards are excluded",match.Count(r=>r.Region.Role=="pool"&&r.Key!=null)<20);
                log.Add("LATE SKILLS "+string.Join(",",state.Seats.Select(s=>s.Skills.Count)));
                using var annotated=new Bitmap(late);using(var g=Graphics.FromImage(annotated))Art.Overlay(g,late.Size,state,engine.RankOptions(state),match,engine,"用户截图验证 · 英雄名 OCR");annotated.Save(Path.Combine(output,"late-overlay.png"));
                var calibration=args.SkipWhile(a=>a!="--settings").Skip(1).FirstOrDefault();
                if(calibration!=null)
                {
                    var custom=JsonSerializer.Deserialize<Settings>(File.ReadAllText(calibration),Store.Json)!;custom.EnsureHeroNameRegions();
                    var customMatch=vision.Scan(late,custom);var customState=new Draft();log.Add("SAVED CALIBRATION "+new StateTracker(c,engine).Apply(customState,customMatch,true));
                    Check("saved calibration recognizes all ten hero names",Enumerable.Range(0,10).All(i=>customState.Seats[i].Hero=="npc_dota_hero_"+expected[i]));
                    log.Add("SAVED CALIBRATION SKILLS "+string.Join(",",customState.Seats.Select(s=>s.Skills.Count)));
                }
                using var board=new DraftBoard{Catalog=c,Draft=state,Engine=engine,Size=new Size(1360,740),Recommendations=engine.RankOptions(state)};
                using var roster=new Bitmap(1360,740);board.DrawToBitmap(roster,new Rectangle(Point.Empty,roster.Size));roster.Save(Path.Combine(output,"recognized-roster.png"));
            }
            using var app=new MainForm(c,stats);app.Demo();app.Opacity=0;app.ShowInTaskbar=false;app.Show();app.PerformLayout();Application.DoEvents();
            int own=app.CurrentSeats.Me;app.SelectViewSeat((own+5)%10);Check("position switch preserves my seat",app.CurrentSeats.Me==own&&app.CurrentSeats.Target==(own+5)%10);app.SelectViewSeat(own);
            using var dashboard=new Bitmap(app.Width,app.Height);app.DrawToBitmap(dashboard,new Rectangle(Point.Empty,app.Size));dashboard.Save(Path.Combine(output,"dashboard-preview.png"));
            log.Add("Rendered dashboard-preview.png and reference-overlay.png.");
        }
        catch(Exception ex){log.Add(ex.ToString());fail++;}
        log.Add($"RESULT: {fail} failures");File.WriteAllLines(Path.Combine(output,"test-results.txt"),log);return fail>0?1:0;
    }
}
