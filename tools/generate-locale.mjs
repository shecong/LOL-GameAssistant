import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve('LOL-GameAssistant');
const files = [];
function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory() && !['bin', 'obj', 'Resources'].includes(entry.name)) walk(path.join(dir, entry.name));
    else if (entry.isFile() && entry.name.endsWith('.cs')) files.push(path.join(dir, entry.name));
  }
}
walk(root);
const strings = new Set();
for (const file of files) {
  const source = fs.readFileSync(file, 'utf8');
  for (const match of source.matchAll(/"((?:\\.|[^"\\])*)"/g)) {
    const value = match[1].replace(/\\n/g, '\n').replace(/\\r/g, '\r').replace(/\\"/g, '"');
    if (/[\u3400-\u9fff]/u.test(value) && value.length < 500) strings.add(value);
  }
}
const entries = [...strings].sort((a, b) => a.localeCompare(b, 'zh'));
const result = {};
let next = 0;
async function worker() {
  while (next < entries.length) {
    const slice = entries.slice(next, next + 12); next += 12;
    const query = slice.map(value => value.replaceAll('\n', '⟪NL⟫').replaceAll('\r', '')).join('\n');
    let translated;
    for (let attempt = 0; attempt < 3; attempt++) {
      try {
        const url = 'https://translate.googleapis.com/translate_a/single?client=gtx&sl=zh-CN&tl=en&dt=t&q=' + encodeURIComponent(query);
        const response = await fetch(url, { signal: AbortSignal.timeout(20000) });
        if (!response.ok) throw new Error(String(response.status));
        const json = await response.json();
        translated = json[0].map(segment => segment[0]).join('').trimEnd().split('\n')
          .map(value => value.replaceAll('⟪NL⟫', '\n'));
        if (translated.length !== slice.length) throw new Error(`count ${translated.length} != ${slice.length}`);
        break;
      } catch (error) {
        if (attempt === 2) console.error('Failed:', error.message, slice[0]);
        else await new Promise(resolve => setTimeout(resolve, 1000 * (attempt + 1)));
      }
    }
    if (translated) slice.forEach((source, index) => { result[source] = translated[index].trim(); });
  }
}
await Promise.all(Array.from({ length: 8 }, worker));
const augments = JSON.parse(fs.readFileSync(path.join(root, 'Resources', 'augment-names.json'), 'utf8'));
for (const entry of Object.values(augments))
  if (entry.name && entry.englishName) result[entry.name] = entry.englishName;
const out = path.join(root, 'Resources', 'locale-en.json');
fs.writeFileSync(out, JSON.stringify(Object.fromEntries(Object.entries(result).sort((a,b) => a[0].localeCompare(b[0], 'zh'))), null, 2) + '\n');
console.log(`Translated ${Object.keys(result).length}/${entries.length} entries to ${out}`);
