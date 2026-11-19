// Render actual isolated SQL-host HTML; mock only transport/time, never production.
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {resolve,dirname,extname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const {chromium}=await import(pathToFileURL(process.argv[4]||process.env.CMS_PLAYWRIGHT_MODULE||resolve(root,'tests/browser/node_modules/playwright/index.mjs')));
const before=resolve(process.argv[2]),after=resolve(process.argv[3]);
for(const dir of [before,after])assert(dir.startsWith(resolve(root,'.local/tests')+'/')||dir.startsWith(resolve(root,'.local/tests')+'\\'));
const baseline=execFileSync('git',['show','bd1d53a1a9c5bd8c362508a04642d166a98a5805:src/Web/wwwroot/js/site.js'],{cwd:root,encoding:'utf8'});
const browser=await chromium.launch({channel:'msedge',headless:true});
const rows=[],checks=[],errors=[];const check=(ok,label)=>{assert(ok,label);checks.push(label);};
try{
 for(const width of [1920,1366,768,390])for(const mode of ['before','after']){
  const page=await browser.newPage({viewport:{width,height:900}});
  if(mode==='after'&&width===390)await page.clock.install({time:new Date('2040-01-01')});
  page.on('pageerror',e=>errors.push(e.message));let calls=0,fail=false,requests=0;let utc='2026-11-18T11:43:00.000Z';
  page.on('request',()=>requests++);
  await page.addInitScript(()=>{window.measure={lcp:0,cls:0,tasks:[]};new PerformanceObserver(l=>l.getEntries().forEach(e=>window.measure.lcp=e.startTime)).observe({type:'largest-contentful-paint',buffered:true});new PerformanceObserver(l=>l.getEntries().forEach(e=>{if(!e.hadRecentInput)window.measure.cls+=e.value;})).observe({type:'layout-shift',buffered:true});new PerformanceObserver(l=>l.getEntries().forEach(e=>window.measure.tasks.push(e.duration))).observe({type:'longtask',buffered:true});});
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());assert(url.hostname==='127.0.0.1','No external requests in fixture');
   if(url.pathname==='/')return route.fulfill({contentType:'text/html',body:await readFile(resolve(mode==='before'?before:after,'home.html'),'utf8')});
   if(url.pathname==='/time'){calls++;return route.fulfill({status:fail?503:200,contentType:'application/json',body:JSON.stringify({utc})});}
   if(url.pathname.startsWith('/js/')||url.pathname.startsWith('/css/')||url.pathname.startsWith('/fonts/')){
    try{const bytes=url.pathname==='/js/site.js'&&mode==='before'?baseline:await readFile(resolve(root,'src/Web/wwwroot','.'+url.pathname));return route.fulfill({contentType:({'.js':'application/javascript','.css':'text/css','.woff2':'font/woff2'})[extname(url.pathname)]||'application/octet-stream',body:bytes});}catch{}
   }
   return route.fulfill({status:404,body:''});
  });
  await page.goto('http://127.0.0.1:5499/');await page.waitForTimeout(1200);
  rows.push({...await page.evaluate(({width,mode})=>({width,mode,...window.measure,overflow:document.documentElement.scrollWidth>innerWidth+1}),{width,mode}),requests});
  if(width===1920){
   await page.evaluate(()=>document.addEventListener('pointerdown',()=>{const start=performance.now();requestAnimationFrame(()=>requestAnimationFrame(()=>window.measure.clickToPaint=performance.now()-start));},{once:true,capture:true}));
   await page.locator('input[type="search"]').first().click();await page.waitForFunction(()=>window.measure.clickToPaint>0);
   rows.at(-1).clickToPaintMs=await page.evaluate(()=>window.measure.clickToPaint);
  }
  check(!rows.at(-1).overflow,mode+' no overflow '+width);
  if(mode==='after'){
   check(await page.locator('[data-block-type=Category]').count()===14,'14 configured categories '+width);
   check(await page.getByText('Chưa có tin được công bố trong chuyên mục này.',{exact:true}).count()===1,'Empty category visible '+width);
   check(await page.locator('[data-vietnam-clock]').textContent().then(s=>s.includes('18:43:')&&s.includes('18/11/2026')),'Hanoi date/time from UTC '+width);
   const first=await page.locator('[data-vietnam-clock]').getAttribute('datetime');await page.waitForTimeout(1100);
   check(Date.parse(await page.locator('[data-vietnam-clock]').getAttribute('datetime'))>Date.parse(first),'Seconds advance '+width);
   if(width===390){
    const n=calls;await page.clock.fastForward(60000);check(calls===n,'No per-second network and visitor clock ignored');
    utc='2026-11-18T12:00:00Z';await page.clock.fastForward(300000);await page.waitForTimeout(50);check(calls===n+1,'One periodic refresh');
    await page.clock.fastForward(6000);utc='2026-11-18T12:05:00Z';await page.evaluate(()=>document.dispatchEvent(new Event('visibilitychange')));await page.waitForTimeout(50);check(calls===n+2,'Refresh on returning to tab');
    check((await page.locator('[data-vietnam-clock]').textContent()).includes('19:05:'),'Fresh anchor applied');
    fail=true;await page.clock.fastForward(300000);await page.waitForTimeout(50);check((await page.locator('[data-vietnam-clock]').textContent()).includes('18/11/2026'),'Failure keeps valid anchor, not visitor date');
   }
  }
  await page.close();
 }
 check(errors.length===0,'No JavaScript errors');
 await mkdir(resolve(root,'.local/tests/ux-browser'),{recursive:true});await writeFile(resolve(root,'.local/tests/ux-browser/result.json'),JSON.stringify({checks,rows,errors,scope:'SQL-generated HTML replay; browser timing excludes SQL/network; not IIS'},null,2));
 console.log(JSON.stringify({passed:checks.length,rows,errors},null,2));
}finally{await browser.close();}
