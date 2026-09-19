import json,sqlite3,datetime,os
db=sqlite3.connect('research/dota_ad_data.db')
with open('data/statistics.json',encoding='utf-8') as f: old=json.load(f)
with open('data/catalog.json',encoding='utf-8') as f: catalog=json.load(f)
hero_codes={h['code'][len('npc_dota_hero_'):].replace('_','').lower():h['code'] for h in catalog['heroes']}
abilities={r[0]:r for r in db.execute('select ability_id,name,winrate,is_ultimate,pick_rate from Abilities')}
heroes={r[0]:r for r in db.execute('select hero_id,name,winrate from Heroes')}
out={'source':'https://github.com/Tiarin-Hino/ability-draft-plus/blob/main/resources/dota_ad_data.db',
     'attribution':'Ability Draft Plus 公开离线库（ISC）；上游统计来自 Windrun.io。二元组合快照未保留样本数，按未知样本做保守收缩。',
     'fetchedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),'patch':'未知（上游抓取 2026-07-31）',
     'abilities':{},'heroes':{},'exclusive':old.get('exclusive',[]),'pairs':[],'triplets':[]}
for _,name,wr,ult,pick in abilities.values():
    prev=old.get('abilities',{}).get(name,{})
    if wr is not None: out['abilities'][name]={'winRate':wr,'samples':prev.get('samples',1),'avgPick':pick or prev.get('avgPick',0),'ultimate':bool(ult)}
for hid,name,wr in heroes.values():
    code=hero_codes.get(name.replace('_','').lower())
    if code: out['heroes'][code]={'attribute':old.get('heroes',{}).get(code,{}).get('attribute',''),'attack':old.get('heroes',{}).get(code,{}).get('attack',''),'winRate':wr,'source':out['source']}
for a,b,inc in db.execute('select a.name,b.name,s.synergy_increase from AbilitySynergies s join Abilities a on a.ability_id=s.base_ability_id join Abilities b on b.ability_id=s.synergy_ability_id'):
    if inc is not None: out['pairs'].append({'a':a,'b':b,'synergyPp':inc*100,'samples':0,'source':out['source']})
for hn,an,inc in db.execute('select h.name,a.name,s.synergy_increase from HeroAbilitySynergies s join Heroes h on h.hero_id=s.hero_id join Abilities a on a.ability_id=s.ability_id'):
    code=hero_codes.get(hn.replace('_','').lower())
    if code and inc is not None: out['pairs'].append({'a':code,'b':an,'synergyPp':inc*100,'samples':0,'source':out['source']})
for a,b,c,inc,n in db.execute('select a.name,b.name,c.name,t.synergy_increase,t.num_picks from AbilityTriplets t join Abilities a on a.ability_id=t.ability_id_one join Abilities b on b.ability_id=t.ability_id_two join Abilities c on c.ability_id=t.ability_id_three'):
    if inc is not None: out['triplets'].append({'a':a,'b':b,'c':c,'synergyPp':inc*100,'samples':n or 0,'source':out['source']})
for hn,a,b,inc,n in db.execute('select h.name,a.name,b.name,t.synergy_increase,t.num_picks from HeroAbilityTriplets t join Heroes h on h.hero_id=t.hero_id join Abilities a on a.ability_id=t.ability_id_one join Abilities b on b.ability_id=t.ability_id_two'):
    code=hero_codes.get(hn.replace('_','').lower())
    if code and inc is not None: out['triplets'].append({'a':code,'b':a,'c':b,'synergyPp':inc*100,'samples':n or 0,'source':out['source']})
tmp='data/statistics.json.tmp'
with open(tmp,'w',encoding='utf-8') as f: json.dump(out,f,ensure_ascii=False,separators=(',',':'))
os.replace(tmp,'data/statistics.json')
print(len(out['abilities']),len(out['heroes']),len(out['pairs']),len(out['triplets']))
