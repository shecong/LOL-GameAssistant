import fs from 'node:fs';

const file = 'LOL-GameAssistant/Resources/augment-names.json';
const catalog = JSON.parse(fs.readFileSync(file, 'utf8'));
const response = await fetch('https://raw.communitydragon.org/latest/cdragon/arena/en_us.json');
if (!response.ok) throw new Error(`CommunityDragon: ${response.status}`);
const data = await response.json();
let updated = 0;
for (const augment of data.augments ?? []) {
  if (catalog[augment.id] && augment.name) {
    catalog[augment.id].englishName = augment.name;
    updated++;
  }
}
const mayhemResponse = await fetch('https://raw.githubusercontent.com/zp96-cmd/mayhem-overlay/main/data/augments.json');
if (!mayhemResponse.ok) throw new Error(`Mayhem catalog: ${mayhemResponse.status}`);
const mayhem = await mayhemResponse.json();
for (const augment of mayhem.augments ?? []) {
  if (catalog[augment.id] && augment.name) {
    catalog[augment.id].englishName = augment.name;
    updated++;
  }
}
fs.writeFileSync(file, JSON.stringify(catalog, null, 2) + '\n');
console.log(`Added ${updated} English augment names`);
