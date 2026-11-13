// Real compiled Razor over loopback HTTP, real production CSS/JS/fonts; synthetic
// read models only. No operational SQL, credentials, scanner or publication writes.
import {pathToFileURL,fileURLToPath} from 'node:url';
import {resolve,dirname} from 'node:path';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import assert from 'node:assert/strict';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const modulePath=process.env.CMS_PLAYWRIGHT_MODULE||resolve(root,'tests/browser/node_modules/playwright/index.mjs');
const {chromium}=await import(pathToFileURL(modulePath));
const base=process.argv[2]||'http://127.0.0.1:5407';
if(!/^http:\/\/127\.0\.0\.1:\d+$/.test(base))throw Error('Isolated loopback host only.');
const evidence=resolve(root,'.local/tests/public-presentation');await mkdir(evidence,{recursive:true});
const startedUtc=new Date().toISOString();
const browser=await chromium.launch({channel:'msedge',headless:true});
const context=await browser.newContext();const page=await context.newPage();
const errors=[],networkErrors=[],external=[];let checks=0;const metrics=[];
page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.status()>=400)networkErrors.push(r.url()+':'+r.status());});
await page.route('**/*',r=>{if(!r.request().url().startsWith(base+'/')&&!r.request().url().startsWith('data:')){external.push(r.request().url());return r.abort();}return r.continue();});
function check(value,label){assert(value,label);checks++;console.log('PASS '+label);}
async function load(query=''){
  const response=await page.goto(base+'/'+query,{waitUntil:'networkidle'});assert.equal(response.status(),200);
  await page.evaluate(()=>document.fonts.ready);await page.waitForTimeout(100);
}
async function measure(width,kind){
  const m=await page.evaluate(()=>{
    const rect=s=>{const e=document.querySelector(s);if(!e)return null;const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height,right:r.right,bottom:r.bottom};};
    const overflow=[...document.querySelectorAll('.gdla-home article,.home-information-row>section,.home-top-utilities')].filter(e=>e.getBoundingClientRect().width>0&&e.scrollWidth>e.clientWidth+2).map(e=>e.className);
    return {viewport:innerWidth,scroll:document.documentElement.scrollWidth,main:rect('main'),header:rect('.masthead-inner'),footer:rect('.footer-main'),top:rect('.home-top-grid'),lead:rect('.reference-lead'),secondary:rect('.home-secondary-list'),utility:rect('.home-top-utilities'),info:rect('.home-information-row'),event:rect('.home-event-strip'),latest:rect('.home-latest'),category:rect('.category-sections'),video:rect('.home-video-section'),overflow,height:document.body.scrollHeight};
  });
  metrics.push({width,kind,...m});
  check(m.scroll<=width+1,`${kind} ${width}px no page horizontal overflow`);
  check(m.overflow.length===0,`${kind} ${width}px cards/long titles fit within panels`);
  check(m.main&&m.header&&m.footer&&Math.abs(m.main.x-m.header.x)<2&&Math.abs(m.main.w-m.header.w)<2&&Math.abs(m.main.x-m.footer.x)<2,`${kind} ${width}px header/content/footer share one frame`);
  check(m.height>1024,`${kind} ${width}px retained long-page content`);
  if(width>=1200){
    check(m.lead&&m.secondary&&m.utility&&Math.abs(m.lead.y-m.secondary.y)<3&&Math.abs(m.lead.y-m.utility.y)<3,`${kind} ${width}px three hero regions align`);
    check(m.lead.w>m.secondary.w&&m.secondary.w>=200&&m.utility.w>=200,`${kind} ${width}px lead remains largest and side columns readable`);
    check(m.video&&Math.abs(m.video.y-m.info.y)<3,`${kind} ${width}px video shares information row with documents/notices`);
  }
  if(m.lead&&m.secondary&&Math.abs(m.lead.y-m.secondary.y)<3)check(m.lead.right<=m.secondary.x+1,`${kind} ${width}px primary and secondary news do not overlap`);
  if(width<=768)check(m.lead&&m.utility&&m.utility.y>=m.lead.bottom-2,`${kind} ${width}px utilities stack after primary news`);
  if(m.event){check(Math.abs(m.event.h-m.event.w*10/117)<2,`${kind} ${width}px banner retains 117:10 without caption height`);}
}
try{
  for(const width of [1536,1366,768,390,320]){
    await page.setViewportSize({width,height:1024});
    for(const fallback of [false,true]){
      await load('?videos=1&banners=1'+(fallback?'&fallback=true':''));await measure(width,fallback?'fallback':'configured');
      check(await page.locator('.brand-logo,.footer-main img').count()===0,`${fallback?'fallback':'configured'} ${width}px no displayed logo`);
      check(await page.locator('.home-video-section').count()===1,`${fallback?'fallback':'configured'} ${width}px video rendered once`);
      check(await page.locator('.home-information-row .home-notices').count()===1&&await page.locator('.home-information-row .document-preview').count()===1,`${fallback?'fallback':'configured'} ${width}px documents/notices in information row`);
      check(!(await page.locator('body').innerText()).match(/@if\s*\(|@await\s|@foreach\s*\(/),`${fallback?'fallback':'configured'} ${width}px no literal Razor expressions visible`);
    }
  }
  await page.setViewportSize({width:1366,height:1024});
  for(const count of [0,1,3]){
    await load(`?videos=${count}&banners=${count}&sidebar=${count>0}`);
    check(await page.locator('.home-video-card').count()===count,`Video ${count} data items rendered without fabricated fallback`);
    check(await page.locator('[data-event-slide]').count()===count,`Banner ${count} data items retained`);
    check(await page.locator('[data-event-dot]').count()===(count>1?count:0),`Banner ${count} dots only when needed`);
    if(count===0){check(await page.locator('.home-video-section,.home-event-strip,.home-sidebar-banners').count()===0,'Absent video/banner leave no empty card');
      const widths=await page.locator('.home-information-row>section').evaluateAll(es=>es.map(e=>e.getBoundingClientRect().width));check(widths.length===2&&widths.every(x=>x>450),'Missing video redistributes information row');}
    if(count===3){await page.locator('[data-event-dot="2"]').click();await page.waitForTimeout(450);check(await page.locator('[data-event-dot="2"]').getAttribute('aria-current')==='true','Banner dots select requested CMS banner');}
  }
  await load('?hideFeatured=true&hideDocuments=true&hideNotices=true&hideVideo=true&banners=0');
  check(await page.locator('.reference-lead,.document-preview,.home-notices,.home-video-section').count()===0,'Disabled featured/documents/notices/video are not re-enabled by layout');
  check(await page.locator('.home-category').count()===4,'Enabled configured categories remain available');
  await load('?noImages=true&videos=0&banners=0');
  check(await page.locator('.home-news-cluster img').count()===0,'No-image news does not fabricate image placeholders');
  check(await page.locator('.reference-lead').evaluate(e=>getComputedStyle(e).color)!=='rgb(255, 255, 255)','No-image primary news remains readable on light background');
  const utilityLinks=await page.locator('.home-utility-grid>a').evaluateAll(es=>es.map(e=>e.getAttribute('href')));
  check(utilityLinks.length===6&&new Set(utilityLinks).size===6&&utilityLinks.every(h=>h&&h.startsWith('/')&&!h.startsWith('//')&&!h.includes('#')),'Six distinct utility links retain local destinations');
  const clock=await page.locator('[data-vietnam-clock]').innerText();
  check(/\d{2}:\d{2}/.test(clock)&&!/\d{2}:\d{2}:\d{2}/.test(clock),'Public clock displays Vietnam date/time at minute precision');
  await load('?statistics=true&videos=0&banners=0');
  check(await page.getByRole('heading',{name:'Thống kê diện tích đất',exact:true}).count()===1,'Existing statistics module remains in the long page when data is present');
  check(await page.locator('.popular-articles details').count()===2,'Popular articles retains both configured time windows');
  await page.locator('.popular-articles details').nth(1).locator('summary').click();
  check(await page.locator('.popular-articles details').nth(1).evaluate(e=>e.open),'Popular articles second time window remains interactive');
  await page.setViewportSize({width:390,height:844});await load('?videos=1&banners=1');
  const toggle=page.locator('.mobile-menu-toggle');await toggle.click();
  check(await toggle.getAttribute('aria-expanded')==='true','Mobile menu opens using existing handler');
  const summary=page.locator('.nav-dropdown summary').first();await summary.focus();await page.keyboard.press('Enter');
  check(await page.locator('.nav-dropdown').first().evaluate(e=>e.open),'Dropdown opens by keyboard');
  await page.keyboard.press('Escape');
  check(!await page.locator('.nav-dropdown').first().evaluate(e=>e.open),'Escape closes dropdown');
  await toggle.click();
  await page.locator('#header-query').fill('quản lý đất đai');await page.locator('.header-search button').click();
  check(new URL(page.url()).pathname==='/tim-kiem'&&new URL(page.url()).searchParams.get('q')==='quản lý đất đai','Search submits unchanged query to existing search route');
  await load('?videos=1&banners=0');
  const article=await page.locator('.reference-lead h2 a').getAttribute('href');await page.locator('.reference-lead h2 a').click();
  check(new URL(page.url()).pathname===article,'Lead article retains its identity-route link (fixture destination)');
  await load('?videos=1&banners=0');await page.locator('.home-video-poster').first().click();
  const video=page.locator('video');await video.evaluate(v=>v.play());await page.waitForFunction(()=>document.querySelector('video').currentTime>.1);
  check(await video.evaluate(v=>v.videoWidth)===320,'HTML5 test MP4 actually decodes and plays after video link');
  const range=await context.request.get(base+'/fixture/sample.mp4',{headers:{Range:'bytes=0-63'}});check(range.status()===206&&(await range.body()).length===64,'Test MP4 HTTP Range returns requested bytes');
  await page.emulateMedia({reducedMotion:'reduce'});await load('?videos=0&banners=3');await page.mouse.move(0,0);const before=await page.locator('[data-event-dot][aria-current="true"]').getAttribute('data-event-dot');await page.waitForTimeout(5200);
  check(await page.locator('[data-event-dot][aria-current="true"]').getAttribute('data-event-dot')===before,'Reduced-motion preference suppresses banner autoplay');
  const adminLayout=await readFile(resolve(root,'src/Web/Views/Shared/_AdminLayout.cshtml'),'utf8');
  check(!adminLayout.includes('public-design')&&!adminLayout.includes('public-shell'),'CMS layout does not load new public stylesheets');
  check(errors.length===0,'No browser JavaScript errors');check(networkErrors.length===0,'All requested local presentation assets return success');check(external.length===0,'No runtime external fonts/assets requested');
  await writeFile(resolve(evidence,'browser-result.json'),JSON.stringify({result:'PASS',startedUtc,completedUtc:new Date().toISOString(),checks,viewports:[1536,1366,768,390,320],metrics,scope:'Real compiled MVC/Razor over loopback; synthetic readmodels/components; actual static test MP4 playback/range; no SQL/CMS authorization/FFmpeg processing assertions'},null,2));
  console.log('RESULT PASS '+checks);
}catch(error){await writeFile(resolve(evidence,'browser-failure.json'),JSON.stringify({startedUtc,completedUtc:new Date().toISOString(),checks,error:String(error),errors,networkErrors,external,metrics},null,2));throw error;}
finally{await browser.close();}
