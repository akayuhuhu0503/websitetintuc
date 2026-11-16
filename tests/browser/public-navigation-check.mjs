import {pathToFileURL} from 'node:url';
import {writeFile} from 'node:fs/promises';
import {join} from 'node:path';
import assert from 'node:assert/strict';
const [base,out,article]=process.argv.slice(2);
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(base));
const modulePath=process.env.CMS_PLAYWRIGHT_MODULE;
const {chromium}=await import(modulePath.startsWith('file:')?modulePath:pathToFileURL(modulePath));
const browser=await chromium.launch({channel:'msedge',headless:true});
const results=[];
try{
 for(const route of ['/', '/tin-tuc','/chuyen-muc/fixture-1','/bai-viet/'+article]){
  const ctx=await browser.newContext({viewport:{width:1366,height:900},userAgent:'Mozilla/5.0 GDLA Navigation Fixture'});
  await ctx.addInitScript(()=>{window.navMeasures={lcp:0,cls:0,long:[]};new PerformanceObserver(l=>{for(const x of l.getEntries())window.navMeasures.lcp=x.startTime;}).observe({type:'largest-contentful-paint',buffered:true});new PerformanceObserver(l=>{for(const x of l.getEntries())if(!x.hadRecentInput)window.navMeasures.cls+=x.value;}).observe({type:'layout-shift',buffered:true});new PerformanceObserver(l=>window.navMeasures.long.push(...l.getEntries().map(x=>x.duration))).observe({type:'longtask',buffered:true});document.addEventListener('click',e=>{if(e.target.closest('a[href]'))sessionStorage.setItem('nav-click',String(performance.timeOrigin+performance.now()));});});
  const page=await ctx.newPage();
  for(let iteration=0;iteration<4;iteration++){
   let response;
   if(iteration===0)response=await page.goto(base+route);
   else{
    // Actual anchor activation without prefetch or a synthetic SPA.
    await page.evaluate(url=>{const a=document.createElement('a');a.href=url;a.id='fixture-nav';a.textContent='Mở trang kiểm thử';document.body.prepend(a);},route);
    [response]=await Promise.all([page.waitForNavigation(),page.locator('#fixture-nav').click()]);
   }
   assert.equal(response.status(),200);await page.waitForTimeout(500);
   const sample=await response.headerValue('x-navigation-sample');
   results.push(await page.evaluate(({route,iteration,sample})=>{const n=performance.getEntriesByType('navigation')[0],resources=performance.getEntriesByType('resource'),fcp=performance.getEntriesByName('first-contentful-paint')[0]?.startTime??null,click=Number(sessionStorage.getItem('nav-click'));return {sample,route,iteration,ttfb:n.responseStart-n.requestStart,connect:n.connectEnd-n.connectStart,dns:n.domainLookupEnd-n.domainLookupStart,response:n.responseEnd-n.responseStart,fcp,clickToFcp:click&&fcp?performance.timeOrigin+fcp-click:null,dom:n.domContentLoadedEventEnd,resources:resources.length,resourceBytes:resources.reduce((a,r)=>a+r.transferSize,0),resourceDetails:resources.map(r=>({path:new URL(r.name).pathname,ms:r.duration,bytes:r.transferSize})),serverTiming:n.serverTiming,...window.navMeasures};},{route,iteration,sample}));
  }
  await ctx.close();
 }
 await writeFile(join(out,'browser.json'),JSON.stringify({results},null,2));console.log('BROWSER_NAVIGATION_PASS 16 navigations');
}finally{await browser.close();}
