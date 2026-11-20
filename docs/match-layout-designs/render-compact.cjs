const fs=require('node:fs');
const path=require('node:path');
const output = path.resolve(__dirname, '../../tmp/match-layout-designs');
fs.mkdirSync(output, { recursive: true });
const {chromium}=require('../../PenguPlugin/node_modules/playwright');
(async()=>{
 const browser=await chromium.launch({headless:true});
 const page=await browser.newPage({viewport:{width:1600,height:1000},deviceScaleFactor:1});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.setContent('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><style>body{margin:0;padding:16px;background:#07101a}</style><body>'+fs.readFileSync(path.join(__dirname,'compact-match-layout-options.html'),'utf8')+'</body></html>');
 const results=[];
 for(const [index,type] of ['paired','dense','matrix'].entries()){
  await page.evaluate(i=>document.querySelectorAll('#compact-match-options > section').forEach((e,j)=>e.hidden=i!==j),index);
  const el=page.locator('#cm-'+type);
  const dimensions={design:type};
  for(const width of [1600,1024,736,320]){
   await page.setViewportSize({width,height:1000});
   const size=await el.evaluate(e=>({height:e.offsetHeight,overflow:e.scrollWidth-e.clientWidth,document:document.documentElement.scrollWidth-innerWidth}));
   if(size.overflow>1||size.document>1)throw Error(JSON.stringify({type,width,...size}));
   dimensions[width]=size.height;
   if(width===1600)await el.screenshot({path:path.join(output,'compact-'+type+'.png')});
  }
  await el.locator('[data-collapse]').click();
  if(await el.locator('.cm-body').isVisible())throw Error('collapse failed');
  await el.locator('[data-collapse]').click();
  if(!await el.locator('.cm-body').isVisible())throw Error('expand failed');
  results.push(dimensions);
 }
 await browser.close();if(errors.length)throw Error(errors.join('\n'));console.log(JSON.stringify(results));
})();
