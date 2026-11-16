// Run against PublicPresentationHost only. No SQL, operational data or screenshots.
import {pathToFileURL,fileURLToPath} from 'node:url';
import {resolve,dirname} from 'node:path';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {execFileSync} from 'node:child_process';
import assert from 'node:assert/strict';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const {chromium}=await import(pathToFileURL(process.env.CMS_PLAYWRIGHT_MODULE||resolve(root,'tests/browser/node_modules/playwright/index.mjs')));
const base=process.argv[2]||'http://127.0.0.1:5412';
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(base),'Isolated loopback fixture only');
const baselineRef=process.argv[3]||'d98d6898c0619e4660a88064a193c5bbc6832e7a';
assert(/^[a-f0-9]{40}$/.test(baselineRef),'Exact baseline commit required');
const scripts={before:execFileSync('git',['show',`${baselineRef}:src/Web/wwwroot/js/site.js`],{cwd:root,encoding:'utf8'}),after:await readFile(resolve(root,'src/Web/wwwroot/js/site.js'),'utf8')};
const results=[];let checks=0;
const check=(condition,message)=>{assert(condition,message);checks++;};
const browser=await chromium.launch({channel:'msedge',headless:true});
try {
 // Warm the compiled view before both arms; each navigation then has a fresh cache.
 const warm=await browser.newPage();await warm.goto(base+'/?news=12&videos=3&banners=3');await warm.close();
 for(const width of [1366,390])for(let run=0;run<3;run++)for(const mode of ['before','after']){
  const context=await browser.newContext({viewport:{width,height:768}});const page=await context.newPage();
  await page.route('**/js/site.js?*',route=>route.fulfill({status:200,contentType:'application/javascript',body:scripts[mode]}));
  await page.addInitScript(()=>{window.measures={longTasks:[],shifts:[],lcp:[]};new PerformanceObserver(l=>window.measures.longTasks.push(...l.getEntries().map(e=>e.duration))).observe({type:'longtask',buffered:true});new PerformanceObserver(l=>window.measures.shifts.push(...l.getEntries().filter(e=>!e.hadRecentInput).map(e=>e.value))).observe({type:'layout-shift',buffered:true});new PerformanceObserver(l=>window.measures.lcp.push(...l.getEntries().map(e=>e.startTime))).observe({type:'largest-contentful-paint',buffered:true});});
  check((await page.goto(base+'/?news=12&videos=3&banners=3')).status()===200,'Fixture loads');await page.waitForTimeout(1500);
  results.push(await page.evaluate(({width,run,mode})=>{const n=performance.getEntriesByType('navigation')[0],r=performance.getEntriesByType('resource');return {width,run,mode,ttfb:n.responseStart-n.requestStart,dcl:n.domContentLoadedEventEnd,load:n.loadEventEnd,lcp:window.measures.lcp.at(-1),cls:window.measures.shifts.reduce((a,b)=>a+b,0),longTasks:window.measures.longTasks,requests:r.length,bytes:r.reduce((a,b)=>a+b.transferSize,0),bannerRequests:r.filter(x=>x.name.includes('/fixture/banner/')).length};},{width,run,mode}));await context.close();
 }
 check(results.filter(x=>x.width===390&&x.mode==='before').every(x=>x.bannerRequests===3),'Baseline mobile eagerly requests all offscreen banners');
 check(results.filter(x=>x.width===390&&x.mode==='after').every(x=>x.bannerRequests===0),'Updated mobile defers all offscreen banners');
 const page=await browser.newPage({viewport:{width:390,height:768}});await page.clock.install();
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(base+'/?news=12&videos=0&banners=3');await page.waitForTimeout(100);
 await page.clock.fastForward(16000);
 check(await page.locator('[data-event-dot="0"]').getAttribute('aria-current')==='true','No offscreen autoplay');
 await page.locator('[data-event-carousel]').scrollIntoViewIfNeeded();await page.mouse.move(0,0);await page.waitForTimeout(100);
 await page.clock.fastForward(4000);check(await page.locator('[data-event-dot="0"]').getAttribute('aria-current')==='true','Does not rotate before five seconds');
 await page.clock.fastForward(1100);check(await page.locator('[data-event-dot="1"]').getAttribute('aria-current')==='true','Visible carousel autoplays after five seconds');
 await page.locator('[data-event-dot="2"]').click();check(await page.locator('[data-event-dot="2"]').getAttribute('aria-current')==='true','Dot selects third slide');
 await page.locator('[data-event-dot="2"]').evaluate(e=>e.blur());await page.mouse.move(0,0);await page.waitForTimeout(100);await page.clock.fastForward(5100);
 check(await page.locator('[data-event-dot="0"]').getAttribute('aria-current')==='true','Carousel wraps');
 await page.locator('[data-event-carousel]').hover();await page.clock.fastForward(11000);check(await page.locator('[data-event-dot="0"]').getAttribute('aria-current')==='true','Hover pauses');
 await page.mouse.move(0,0);await page.waitForTimeout(100);await page.clock.fastForward(5100);check(await page.locator('[data-event-dot="1"]').getAttribute('aria-current')==='true','Leave resumes');
 await page.locator('.event-slides').dispatchEvent('pointerdown',{pointerType:'touch',clientX:200,clientY:100});await page.locator('.event-slides').dispatchEvent('pointerup',{pointerType:'touch',clientX:100,clientY:100});
 check(await page.locator('[data-event-dot="2"]').getAttribute('aria-current')==='true','Touch swipe still navigates');
 await page.waitForFunction(()=>document.querySelector('.event-slide.is-active img').naturalWidth>0);checks++;
 check(await page.locator('.event-slide.is-active a').getAttribute('href')==='/tin-tuc','CMS link retained');
 await page.emulateMedia({reducedMotion:'reduce'});await page.waitForTimeout(100);await page.clock.fastForward(11000);check(await page.locator('[data-event-dot="2"]').getAttribute('aria-current')==='true','Reduced motion stops autoplay');
 await page.locator('[data-event-dot="0"]').click();check(await page.locator('[data-event-dot="0"]').getAttribute('aria-current')==='true','Reduced motion keeps manual dots');
 await page.goto(base+'/?news=12&banners=1');check(await page.locator('[data-event-dot]').count()===0,'Single banner has no controls');
 await page.goto(base+'/?news=12&banners=0');check(await page.locator('[data-event-carousel]').count()===0,'Empty banner creates no block');
 check(errors.length===0,'No JavaScript errors');await page.close();
 const out=resolve(root,'.local/tests/public-performance');await mkdir(out,{recursive:true});await writeFile(resolve(out,'comparison.json'),JSON.stringify({result:'PASS',checks,baselineRef,scope:'Same compiled Razor fixture, interleaved JS before/after; synthetic SVGs; no SQL/IIS',results},null,2));
 console.log(JSON.stringify({result:'PASS',checks,results}));
}finally{await browser.close();}
