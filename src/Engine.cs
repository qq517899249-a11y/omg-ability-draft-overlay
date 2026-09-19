namespace OmgAd;

public sealed class Engine
{
    readonly Catalog catalog;readonly Statistics stats;
    readonly Dictionary<string,PairStat> pairIndex;
    readonly Dictionary<string,List<TripletStat>> tripletIndex;
    static string PairKey(string a,string b)=>string.CompareOrdinal(a,b)<0?a+"\0"+b:b+"\0"+a;
    public Engine(Catalog catalog,Statistics stats)
    {
        this.catalog=catalog;this.stats=stats;
        pairIndex=stats.Pairs.GroupBy(p=>PairKey(p.A,p.B)).ToDictionary(g=>g.Key,g=>g.OrderByDescending(p=>p.Samples).First());
        tripletIndex=[];
        foreach(var t in stats.Triplets)foreach(var key in new[]{t.A,t.B,t.C})
        {if(!tripletIndex.TryGetValue(key,out var list))tripletIndex[key]=list=[];list.Add(t);}
    }
    public void RebuildIndexes()
    {
        pairIndex.Clear();foreach(var p in stats.Pairs)pairIndex.TryAdd(PairKey(p.A,p.B),p);
        tripletIndex.Clear();foreach(var t in stats.Triplets)foreach(var key in new[]{t.A,t.B,t.C}){if(!tripletIndex.TryGetValue(key,out var list))tripletIndex[key]=list=[];list.Add(t);}
    }
    // Small, explicit starter rules. These are heuristic score points, NEVER empirical win-rate deltas.
    public static readonly (string A,string B,double Points,string Why)[] Rules = [
        ("earthshaker_aftershock","storm_spirit_ball_lightning",6,"余震 + 球状闪电：频繁施法触发（规则）"),
        ("batrider_sticky_napalm","shadow_shaman_shackles",5,"叠油 + 枷锁：多次伤害配合（规则）"),
        ("batrider_sticky_napalm","pudge_rot",5,"叠油 + 腐烂：持续伤害配合（规则）"),
        ("luna_lucent_beam","luna_eclipse",7,"月光 + 月蚀：关联技能（规则）"),
        ("faceless_void_time_lock","windrunner_focusfire",4,"时间锁定 + 集中火力：攻击触发（规则）"),
        ("slardar_bash","windrunner_focusfire",4,"深海重击 + 集中火力：攻击触发（规则）"),
        ("tusk_walrus_punch","tidehunter_anchor_smash",3,"海象神拳 + 锚击：攻击联动候选（规则）"),
        ("lina_fiery_soul","bristleback_quill_spray",4,"炽魂 + 刺针扫射：高频施法（规则）"),
        ("lina_fiery_soul","zuus_arc_lightning",4,"炽魂 + 弧形闪电：高频施法（规则）"),
        ("phantom_assassin_coup_de_grace","sven_great_cleave",3,"恩赐解脱 + 巨力挥舞：物理输出（规则）")
    ];
    static readonly HashSet<string> OnHit = ["ursa_fury_swipes","slardar_bash","faceless_void_time_lock","antimage_mana_break","life_stealer_feast","weaver_geminate_attack"];

    public bool Legal(Draft draft, Seat seat, Item item)
    {
        if (!draft.Available(item.Code)) return false;
        if (item.Hero) return !seat.HasHero&&seat.HeroSlot==HeroSlotState.Empty;
        if (seat.Skills.Count>=4) return false;
        if (stats.Exclusive.Any(pair=>pair.Contains(item.Code) && pair.Any(seat.Skills.Contains))) return false;
        var ults=seat.Skills.Count(k=>catalog.All.TryGetValue(k,out var a)&&a.Ultimate);
        return item.Ultimate ? ults==0 : seat.Skills.Count-ults<3;
    }
    public (double Points,string? Reason) Pair(string a,string b)
    {
        pairIndex.TryGetValue(PairKey(a,b),out var observed);
        if(observed is not null&&observed.Samples is >0 and <100)observed=null;
        if(observed is not null)
        {
            double confidence=observed.Samples==0?.12:observed.Samples/(observed.Samples+1500.0);
            return (Math.Clamp(observed.SynergyPp,-15,15)*confidence*.5,$"组合统计 {observed.SynergyPp:+0.0;-0.0}pp / "+(observed.Samples==0?"样本数未随快照发布":$"n={observed.Samples}"));
        }
        foreach(var r in Rules) if(r.A==a&&r.B==b||r.A==b&&r.B==a) return(r.Points,r.Why);
        return(0,null);
    }
    (double Points,string? Reason) Triplet(IEnumerable<string> selected,string candidate)
    {
        var have=selected.ToHashSet();
        var t=(tripletIndex.GetValueOrDefault(candidate)??[]).Where(t=>t.Samples>=100&&new[]{t.A,t.B,t.C}.Where(x=>x!=candidate).All(have.Contains))
            .OrderByDescending(t=>t.SynergyPp*t.Samples/(t.Samples+2500.0)).FirstOrDefault();
        return t is null?(0,null):(Math.Clamp(t.SynergyPp,-20,20)*t.Samples/(t.Samples+2500.0)*.5,$"三项组合 {t.SynergyPp:+0.0;-0.0}pp / n={t.Samples}");
    }
    double BodyFit(string? hero,string skill)
    {
        if(hero is null||!stats.Heroes.TryGetValue(hero,out var h)) return 0;
        var observed=Pair(hero,skill);if(observed.Reason!=null)return observed.Points*.5;
        return OnHit.Contains(skill) ? (h.Attack=="Ranged"?1.5:0)+(h.Attribute=="agi"?.5:0) : 0;
    }
    (double Points,string? Reason,string Kind) Relationship(Item a,Item b)
    {
        var pair=Pair(a.Code,b.Code);
        if(pair.Reason!=null)return (pair.Points,pair.Reason,"联动");
        if(a.Hero!=b.Hero)
        {
            double fit=BodyFit(a.Hero?a.Code:b.Code,a.Hero?b.Code:a.Code);
            if(fit>0)return(fit,"远程 / 敏捷本体与攻击特效的适配规则；不是专属触发或实证组合胜率。","适配");
        }
        return (0,null,"联动");
    }
    public List<ComboHint> CombosFor(Draft draft,Item item)
    {
        var seat=draft.Seats[draft.TargetSeat];var result=new List<ComboHint>();
        if(!Legal(draft,seat,item))return result;
        void Add(string key,bool selected)
        {
            if(!catalog.All.TryGetValue(key,out var other))return;
            var link=Relationship(item,other);
            if(link.Points>0)result.Add(new(key,other.Name,selected,link.Reason!){Kind=link.Kind});
        }
        foreach(var key in seat.Skills)Add(key,true);
        if(seat.Hero is string hero)Add(hero,true);
        var next=new Seat{Hero=item.Hero?item.Code:seat.Hero,HeroSlot=item.Hero?HeroSlotState.Occupied:seat.HeroSlot,Skills=item.Hero?[..seat.Skills]:[..seat.Skills,item.Code]};
        foreach(var key in draft.Pool.Order(StringComparer.Ordinal))
            if(key!=item.Code&&catalog.All.TryGetValue(key,out var other)&&Legal(draft,next,other))Add(key,false);
        return result.Where(h=>ComboSelectable(draft,item,h)).OrderByDescending(c=>c.AlreadyPicked).ThenBy(c=>c.Kind=="适配").ToList();
    }
    // Hard constraints always run before synergy, including when rendering an older result.
    public bool ComboSelectable(Draft draft,Item candidate,ComboHint hint)
    {
        var seat=draft.Seats[draft.TargetSeat];
        if(!Legal(draft,seat,candidate)||!catalog.All.TryGetValue(hint.Partner,out var partner)||candidate.Code==partner.Code)return false;
        if(hint.AlreadyPicked)return partner.Hero?seat.Hero==partner.Code:seat.Skills.Contains(partner.Code);
        if(!Legal(draft,seat,partner))return false;
        var next=new Seat{Hero=candidate.Hero?candidate.Code:seat.Hero,HeroSlot=candidate.Hero?HeroSlotState.Occupied:seat.HeroSlot,
            Skills=candidate.Hero?[..seat.Skills]:[..seat.Skills,candidate.Code]};
        return Legal(draft,next,partner);
    }
    public List<Recommendation> SelectableDisplay(Draft draft,IEnumerable<Recommendation> options)=>options
        .Where(r=>Legal(draft,draft.Seats[draft.TargetSeat],r.Item))
        .Select(r=>r with{Combos=r.Combos.Where(h=>ComboSelectable(draft,r.Item,h)).ToList()}).ToList();
    public List<Recommendation> ComboOptions(Draft draft)=>Recommend(draft,int.MaxValue)
        .Select(r=>r with{Combos=CombosFor(draft,r.Item)}).Where(r=>r.Combos.Count>0)
        .OrderByDescending(r=>r.Combos.Any(c=>c.AlreadyPicked&&c.Kind=="联动"))
        .ThenByDescending(r=>r.Combos.Any(c=>c.AlreadyPicked)).ThenByDescending(r=>r.Score).ToList();
    double Base(string key)
    {
        if(stats.Abilities.TryGetValue(key,out var a)) return (a.WinRate-.5)*100*a.Samples/(a.Samples+1000.0);
        if(stats.Heroes.TryGetValue(key,out var h)&&h.WinRate is double wr) return(wr-.5)*100;
        return 0;
    }
    public List<Recommendation> Recommend(Draft draft, int count=3)
    {
        var seat=draft.Seats[draft.TargetSeat]; var enemy=draft.Seats.Where((_,i)=>(i<5)!=(draft.TargetSeat<5));
        return draft.Pool.Where(catalog.All.ContainsKey).Select(k=>catalog.All[k]).Where(a=>Legal(draft,seat,a)).Select(a=>
        {
            var reasons=new List<string>(); double score=50+Base(a.Code); double? wr=null;
            if(stats.Abilities.TryGetValue(a.Code,out var st)) {wr=st.WinRate;reasons.Add($"单技能 {wr:P1} · 样本 {st.Samples:N0}");}
            else if(a.Hero&&stats.Heroes.TryGetValue(a.Code,out var hs)&&hs.WinRate is double heroWr){wr=heroWr;reasons.Add($"英雄本体 {heroWr:P2} · Windrun 快照 / 样本量未知");}
            else reasons.Add(a.Hero?"英雄暂无胜率统计，仅按搭配规则评分":"无统计：中性基线");
            if(a.Hero)
            {
                var fit=seat.Skills.Sum(k=>BodyFit(a.Code,k));score+=fit;
                if(fit>0) reasons.Add($"英雄攻击类型适配 +{fit:0.0}分（规则）");
            }
            else
            {
                foreach(var selected in seat.Skills)
                {var p=Pair(a.Code,selected);score+=p.Points;if(p.Reason!=null)reasons.Add(p.Reason);}
                var tri=Triplet(seat.Skills.Concat(seat.Hero is null?[]:[seat.Hero]),a.Code);score+=tri.Points;if(tri.Reason!=null)reasons.Add(tri.Reason);
                var fit=BodyFit(seat.Hero,a.Code);score+=fit;if(fit>0)reasons.Add($"英雄适配 +{fit:0.0}分（规则）");
                // Complement value only if another slot remains after this pick and the pair can coexist.
                var future=0.0;
                if(seat.Skills.Count<3)
                {
                    var prospective=new Seat{Hero=seat.Hero,HeroSlot=seat.HeroSlot,Skills=[..seat.Skills,a.Code]};
                    future=draft.Pool.Where(k=>k!=a.Code&&catalog.All.TryGetValue(k,out var b)&&!b.Hero&&Legal(draft,prospective,b))
                        .Select(k=>Pair(a.Code,k).Points).DefaultIfEmpty(0).Max()*.15;
                }
                score+=future;if(future>0)reasons.Add($"池内仍有可搭配技能 +{future:0.0}分");
                var deny=enemy.Where(s=>Legal(draft,s,a)).Select(s=>s.Skills.Sum(k=>Math.Max(0,Pair(a.Code,k).Points))).DefaultIfEmpty(0).Max()*.25;
                score+=deny;if(deny>0)reasons.Add($"阻断对方组合 +{deny:0.0}分（规则）");
            }
            return new Recommendation(a,score,wr,reasons);
        }).OrderByDescending(r=>r.Score).ThenBy(r=>r.Item.Code,StringComparer.Ordinal).Take(count).ToList();
    }
    double SeatStrength(Seat s)
    {
        double v=s.Hero is null?0:Base(s.Hero);
        foreach(var a in s.Skills) v+=Base(a)+BodyFit(s.Hero,a);
        for(int i=0;i<s.Skills.Count;i++)for(int j=i+1;j<s.Skills.Count;j++)v+=Pair(s.Skills[i],s.Skills[j]).Points*.5;
        return v;
    }
    static double Probability(double strengthDifference)=>1/(1+Math.Exp(-Math.Clamp(strengthDifference/65,-1.5,1.5)));
    public double EstimateAfter(Draft draft,Item ability)
    {
        if(!Legal(draft,draft.Seats[draft.TargetSeat],ability))throw new ArgumentException("不可选的候选项");
        var seat=draft.Seats[draft.TargetSeat];
        var hypothetical=new Seat{Hero=ability.Hero?ability.Code:seat.Hero,HeroSlot=ability.Hero?HeroSlotState.Occupied:seat.HeroSlot,Skills=[..seat.Skills]};
        if(!ability.Hero)hypothetical.Skills.Add(ability.Code);
        var diff=draft.Seats.Take(5).Sum(SeatStrength)-draft.Seats.Skip(5).Sum(SeatStrength);
        var increase=SeatStrength(hypothetical)-SeatStrength(seat);
        return Probability(diff+(draft.TargetSeat<5?increase:-increase));
    }
    public List<Recommendation> RankOptions(Draft draft,int count=10)
    {
        var current=Estimate(draft);var seat=draft.Seats[draft.TargetSeat];
        return Recommend(draft,int.MaxValue).Select(r=>
        {
            double after=EstimateAfter(draft,r.Item);
            return r with{RadiantAfter=after,GainPp=(draft.TargetSeat<5?after-current:current-after)*100,Combos=CombosFor(draft,r.Item)};
        }).Where(r=>r.GainPp>1e-8).OrderByDescending(r=>r.GainPp).ThenByDescending(r=>r.Score)
          .ThenBy(r=>r.Item.Code,StringComparer.Ordinal).Take(Math.Clamp(count,0,10)).ToList();
    }
    public double Estimate(Draft d)
    {
        // An intentionally conservative uncalibrated strength transform, not a trained outcome model.
        var diff=d.Seats.Take(5).Sum(SeatStrength)-d.Seats.Skip(5).Sum(SeatStrength);
        return Probability(diff);
    }
    public bool Pick(Draft draft,int seatIndex,string key,out string error)
    {
        error="";
        if(!catalog.All.TryGetValue(key,out var a)||!Legal(draft,draft.Seats[seatIndex],a)) {error="该项已被选取、禁用，或超出 1 英雄 / 3 普通技能 / 1 大招限制。";return false;}
        if(a.Hero){draft.Seats[seatIndex].Hero=key;draft.Seats[seatIndex].HeroSlot=HeroSlotState.Occupied;}else draft.Seats[seatIndex].Skills.Add(key);
        draft.History.Add(Estimate(draft));return true;
    }
}
