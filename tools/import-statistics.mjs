// Optional normalized Windrun export. Input files are data, never executable code.
import fs from 'node:fs';
const file=process.argv[2];
if(!file)throw new Error('Usage: node tools/import-statistics.mjs <normalized-json>');
const x=JSON.parse(fs.readFileSync(file,'utf8'));
if(!x.source||!x.fetchedAt||!x.abilities||Object.keys(x.abilities).length<1)throw new Error('Missing provenance or statistics');
const catalog=JSON.parse(fs.readFileSync('data/catalog.json','utf8'));
const skills=new Set(catalog.abilities.map(a=>a.code));
const bodies=new Set(catalog.heroes.map(a=>a.code));
for(const [key,a] of Object.entries(x.abilities))
  if(!skills.has(key)||!Number.isFinite(a.winRate)||a.winRate<=0||a.winRate>=1||!Number.isInteger(a.samples)||a.samples<1||typeof a.ultimate!=='boolean')throw new Error('Invalid ability: '+key);
for(const [key,h] of Object.entries(x.heroes||{}))
  if(!bodies.has(key)||(h.winRate!=null&&(!Number.isFinite(h.winRate)||h.winRate<=0||h.winRate>=1)))throw new Error('Invalid hero: '+key);
const pairs=new Set();
for(const p of x.pairs||[]){
  const key=[p.a,p.b].sort().join('|');
  if(!(skills.has(p.a)||bodies.has(p.a))||!(skills.has(p.b)||bodies.has(p.b))||(bodies.has(p.a)&&bodies.has(p.b))||p.a===p.b||pairs.has(key)||!Number.isFinite(p.synergyPp)||Math.abs(p.synergyPp)>100||!Number.isInteger(p.samples)||p.samples<1||!p.source)throw new Error('Invalid pair: '+key);
  pairs.add(key);
}
fs.writeFileSync('data/statistics.json.tmp',JSON.stringify(x,null,2));
fs.renameSync('data/statistics.json.tmp','data/statistics.json');
console.log('Imported validated statistics. Rebuild/restart to load them.');
