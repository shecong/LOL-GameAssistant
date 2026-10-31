import fs from 'node:fs';
import assert from 'node:assert/strict';

// Run from the repository root. --check reports differences without writing files.
const source = 'https://hextech.dtodo.cn/data/aram-mayhem-augments.zh_cn.json';
const namesFile = 'LOL-GameAssistant/Resources/augment-names.json';
const descriptionsFile = 'LOL-GameAssistant/Resources/augment-descriptions.json';
const names = JSON.parse(fs.readFileSync(namesFile, 'utf8'));
const descriptions = JSON.parse(fs.readFileSync(descriptionsFile, 'utf8'));
const response = await fetch(source, { signal: AbortSignal.timeout(30_000) });
if (!response.ok) throw new Error(`Mayhem catalog: HTTP ${response.status}`);
const incoming = await response.json();
assert(incoming && typeof incoming === 'object' && !Array.isArray(incoming), 'Expected an ID-indexed catalog');
const entries = Object.entries(incoming);
assert(entries.length >= 200, 'Incomplete Mayhem catalog; refusing to update');
// Validate the entire response before modifying either local table.
for (const [key, entry] of entries) {
  assert(Number.isInteger(entry.id) && entry.id >= 1000 && String(entry.id) === key, `Invalid ID: ${key}`);
  assert(typeof entry.displayName === 'string' && entry.displayName.trim(), `Missing name: ${key}`);
  assert(Number.isInteger(entry.rarity) && entry.rarity >= 0 && entry.rarity <= 2, `Invalid rarity: ${key}`);
  assert(typeof entry.description === 'string' && entry.description.trim(), `Missing description: ${key}`);
}
let added = 0, changed = 0, descriptionChanges = 0;
for (const [key, entry] of entries) {
  const existing = names[key];
  if (!existing) {
    added++;
    console.log(`+ ${key} ${entry.displayName}`);
  } else if (existing.name !== entry.displayName || existing.rarity !== entry.rarity) {
    changed++;
    console.log(`~ ${key} ${existing.name} -> ${entry.displayName} (rarity ${existing.rarity} -> ${entry.rarity})`);
  }
  // Preserve existing English names, verified icon URLs, Arena and historical IDs.
  // The feed's icon filenames and internal names are not URLs or English titles.
  names[key] = { ...existing, name: entry.displayName, rarity: entry.rarity };
  if (descriptions[key] !== entry.description) descriptionChanges++;
  descriptions[key] = entry.description;
}
console.log(`Mayhem: ${entries.length}; added: ${added}; changed: ${changed}; descriptions: ${descriptionChanges}`);
if (process.argv.includes('--check')) {
  process.exitCode = added || changed || descriptionChanges ? 1 : 0;
} else {
  fs.writeFileSync(namesFile, JSON.stringify(names, null, 2) + '\n');
  fs.writeFileSync(descriptionsFile, JSON.stringify(descriptions, null, 2) + '\n');
}
