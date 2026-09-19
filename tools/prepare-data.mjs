// Parse only JSON assignments. Never execute downloaded JavaScript.
import fs from 'node:fs';
import crypto from 'node:crypto';
const path = process.argv[2] || 'research/ad_data.js';
const raw = fs.readFileSync(path, 'utf8');
function read(name) {
  const m = raw.match(new RegExp('window\\.' + name + ' = ([^\\r\\n]+);'));
  if (!m) throw new Error('Missing JSON assignment: ' + name);
  return JSON.parse(m[1]);
}
const abilities = read('AD_ABILITIES'), heroes = read('AD_HEROES');
if (Object.keys(abilities).length < 100) throw new Error('Incomplete source');
const stats = Object.fromEntries(Object.entries(abilities).filter(([k,a]) =>
  a.key === k && Number.isFinite(a.wr) && a.wr > 0 && a.wr < 1 && a.picks > 0
).map(([k,a]) => [k, {winRate:a.wr, samples:a.picks, avgPick:a.pos, ultimate:a.ult}]));
if(Object.keys(stats).length<400 || !Array.isArray(heroes) || heroes.length<100)throw new Error('Insufficient validated source rows');
fs.writeFileSync('data/statistics.json.tmp', JSON.stringify({
  source:'http://43.130.62.185/ad/ad_data.js',
  attribution:'参考网站公开快照；该网站注明统计来自 Windrun.io。未独立核验原始比赛。',
  fetchedAt:new Date().toISOString(), patch:'未知', dataUpdatedAt:null,
  sha256:crypto.createHash('sha256').update(raw).digest('hex'), abilities:stats,
  heroes:Object.fromEntries(heroes.map(h => [h.key,{attribute:h.attr,attack:h.atk}])),
  exclusive:read('AD_EXCLUSIVE'), pairs:[]
},null,2));
fs.renameSync('data/statistics.json.tmp','data/statistics.json');
console.log(`Prepared ${Object.keys(stats).length} ability statistics and ${heroes.length} hero attributes.`);
