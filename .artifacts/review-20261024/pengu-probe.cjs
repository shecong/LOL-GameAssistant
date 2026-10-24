const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const pending = [];
const ctx = vm.createContext({
  fetch: url => new Promise(resolve => pending.push({url, resolve})),
  document: {querySelectorAll: () => []},
});
vm.runInContext(fs.readFileSync('PenguPlugin/index.js','utf8').replaceAll('export function ', 'function '), ctx);
(async () => {
  const ranked = vm.runInContext("fetchTierData('ranked')",ctx);
  const aram = vm.runInContext("fetchTierData('aram')",ctx);
  pending[1].resolve({ok:true,json:async()=>({data:[{id:1,average_stats:{tier:1}}]})});
  await aram;
  pending[0].resolve({ok:true,json:async()=>({data:[{id:1,positions:[{stats:{tier_data:{tier:5}}}]}]})});
  await ranked;
  console.log('F07 current mode/cache:', vm.runInContext("tierRequestMode + '/' + tierCache.get(1)",ctx));
  assert.equal(vm.runInContext('tierRequestMode',ctx),'aram');
  assert.equal(vm.runInContext('tierCache.get(1)',ctx),'T5');
  await vm.runInContext("fetchTierData('aram')",ctx);
  assert.equal(pending.length,2);
  console.log('F07 re-request aram makes no fetch and retains wrong ranked tier: reproduced');
})();
