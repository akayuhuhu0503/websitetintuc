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
const evidence=resolve(root,'.local/tests/public-width');await mkdir(evidence,{recursive:true});
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
    const overflow=[...document.querySelectorAll('.gdla-home article,.home-information-row>section,.home-highlights,.home-service-panel')].filter(e=>e.getBoundingClientRect().width>0&&e.scrollWidth>e.clientWidth+2).map(e=>e.className);
    const panels=[...document.querySelectorAll('.home-information-row>section')].map(e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height};});
    return {viewport:innerWidth,scroll:document.documentElement.scrollWidth,main:rect('main'),header:rect('.masthead-inner'),footer:rect('.footer-main'),quick:rect('.home-quick-lookup'),top:rect('.home-feature-grid'),lead:rect('.reference-lead'),secondary:rect('.home-secondary-list'),highlights:rect('.home-highlights'),services:rect('.home-service-panel'),info:rect('.home-information-row'),panels,event:rect('.home-event-strip'),latest:rect('.home-latest'),category:rect('.category-sections'),video:rect('.home-video-section'),overflow,height:document.body.scrollHeight};
  });
  metrics.push({width,kind,...m});
  check(m.scroll<=width+1,`${kind} ${width}px no page horizontal overflow`);
  check(m.overflow.length===0,`${kind} ${width}px cards/long titles fit within panels`);
  check(m.main&&m.header&&m.footer&&Math.abs(m.main.x-m.header.x)<2&&Math.abs(m.main.w-m.header.w)<2&&Math.abs(m.main.x-m.footer.x)<2&&Math.abs(m.main.w-m.footer.w)<2,`${kind} ${width}px header/content/footer share one frame`);
  check(m.main.w<=1401&&(width<1536||Math.abs(m.main.w-1400)<2),`${kind} ${width}px shared content frame uses 1400px maximum`);
  check(m.height>1024,`${kind} ${width}px retained long-page content`);
  check(m.quick&&m.top&&m.quick.bottom<=m.top.y+1,`${kind} ${width}px quick lookup precedes the lead news region`);
  if(width>=1200){
    check(m.lead&&m.secondary&&m.highlights&&Math.abs(m.lead.y-m.secondary.y)<3&&Math.abs(m.lead.y-m.highlights.y)<3,`${kind} ${width}px lead, secondary list and highlights align`);
    check(m.lead.w>m.secondary.w&&m.secondary.w>=200&&m.highlights.w>=200,`${kind} ${width}px lead remains largest and side columns readable`);
    check(m.services&&m.video&&Math.abs(m.video.y-m.services.y)<3&&Math.abs(m.video.x-m.highlights.x)<3,`${kind} ${width}px services and video share the next row`);
    check(Math.abs(m.services.x-m.lead.x)<3&&Math.abs(m.services.right-m.secondary.right)<3,`${kind} ${width}px services span the two news columns`);
    check(m.panels.length===4&&m.panels.every(p=>Math.abs(p.y-m.panels[0].y)<2)&&Math.max(...m.panels.map(p=>p.w))-Math.min(...m.panels.map(p=>p.w))<2,`${kind} ${width}px four information panels have equal widths and aligned tops`);
    check(await page.locator('.home-secondary-story .item-meta,.home-highlights-list .item-meta').evaluateAll(es=>es.every(e=>parseFloat(getComputedStyle(e).fontSize)>=12)),`${kind} ${width}px news metadata remains legible`);
    check(await page.locator('.home-secondary-story h3,.home-highlights-list h3').evaluateAll(es=>es.every(e=>parseFloat(getComputedStyle(e).fontSize)>=14)),`${kind} ${width}px secondary titles remain legible`);
    check(await page.locator('.home-service-search input').evaluate(e=>{const h=e.getBoundingClientRect().height;return h>=28&&h<=34&&e.getBoundingClientRect().right<=e.parentElement.getBoundingClientRect().right;}),`${kind} ${width}px quick-search input retains compact height and fits its group`);
  }
  if(m.lead&&m.secondary&&Math.abs(m.lead.y-m.secondary.y)<3)check(m.lead.right<=m.secondary.x+1,`${kind} ${width}px primary and secondary news do not overlap`);
  if(width<=768)check(m.lead&&m.highlights&&m.highlights.y>=m.lead.bottom-2,`${kind} ${width}px highlights stack after primary news`);
  if(m.event){check(Math.abs(m.event.h-m.event.w*10/117)<2,`${kind} ${width}px banner retains 117:10 without caption height`);}
}
try{
  for(const width of [1536,1366,1920,768,390,320]){
    await page.setViewportSize({width,height:1024});
    for(const fallback of [false,true]){
      await load('?videos=1&banners=1'+(fallback?'&fallback=true':''));await measure(width,fallback?'fallback':'configured');
      check(await page.locator('.brand-logo,.footer-main img').count()===0,`${fallback?'fallback':'configured'} ${width}px no displayed logo`);
      check(await page.locator('.home-video-section').count()===1,`${fallback?'fallback':'configured'} ${width}px video rendered once`);
      check(await page.locator('.home-information-row .home-notices').count()===1&&await page.locator('.home-information-row .document-preview').count()===1&&await page.locator('.home-information-row .home-compact-category').count()===2,`${fallback?'fallback':'configured'} ${width}px documents/notices/two categories in information row`);
      check(await page.locator('.home-secondary-story').count()===4,`${fallback?'fallback':'configured'} ${width}px four available secondary news items retained`);
      const topUrls=await page.locator('.reference-lead h2 a,.home-secondary-story h3 a,.home-highlights-list h3 a').evaluateAll(es=>es.map(e=>e.getAttribute('href')));
      check(topUrls.length===9&&new Set(topUrls).size===9,`${fallback?'fallback':'configured'} ${width}px all three news regions use distinct article identities`);
      check(!(await page.locator('body').innerText()).match(/@if\s*\(|@await\s|@foreach\s*\(/),`${fallback?'fallback':'configured'} ${width}px no literal Razor expressions visible`);
    }
    await load('?videos=0&banners=3');
    check(await page.locator('.home-event-strip').evaluate(e=>{const frame=e.querySelector('.event-slides').getBoundingClientRect(),dots=e.querySelector('.event-dots'),r=dots.getBoundingClientRect();return Math.abs(frame.height-frame.width*10/117)<2&&r.top>=frame.bottom-1&&dots.textContent.trim()==='';}),`${width}px banner preserves 117:10 and keeps dots outside artwork without text`);
  }
  await page.setViewportSize({width:1366,height:1024});
  for(const fallback of [false,true])for(const count of [1,2,5,6,9,10,12]){
    await load(`?news=${count}&videos=1&banners=0`+(fallback?'&fallback=true':''));
    const urls=await page.locator('.reference-lead h2 a,.home-secondary-story h3 a,.home-highlights-list h3 a,.home-latest .news-card h3 a').evaluateAll(es=>es.map(e=>e.getAttribute('href')));
    check(urls.length===count&&new Set(urls).size===count,`${fallback?'fallback':'configured'} ${count} available articles are neither repeated nor omitted`);
    if(count<=5){
      check(await page.locator('.home-highlights').count()===0,`${fallback?'fallback':'configured'} ${count} articles do not fabricate highlights`);
      check(await page.locator('.home-feature-grid').evaluate(e=>{const top=e.getBoundingClientRect().top,video=e.querySelector('.home-video-section').getBoundingClientRect();return Math.abs(video.top-top)<2;}),`${fallback?'fallback':'configured'} ${count} articles and video do not leave an empty first row`);
    }
    if(count===10){
      check(await page.locator('.home-latest .news-card').count()===1,'Ten distinct articles leave exactly one latest item');
      check(await page.locator('.home-latest .news-card').evaluate(e=>{const r=e.getBoundingClientRect(),img=e.querySelector('.card-image')?.getBoundingClientRect();return r.width<=761&&img&&img.width<r.width*.4;}),'A single latest article has a bounded card and compact thumbnail');
    }
  }
  await load('?news=10&repeatRecent=true&recentFirst=true&videos=0&banners=0');
  const reordered=await page.locator('.reference-lead h2 a,.home-secondary-story h3 a,.home-highlights-list h3 a,.home-latest .news-card h3 a').evaluateAll(es=>es.map(e=>e.getAttribute('href')));
  check(reordered.length===10&&new Set(reordered).size===10&&reordered[0].endsWith('000000000001'),'Duplicate Recent block preceding Featured preserves the selected lead and unique article IDs');
  for(const categories of [0,1,2,3]){
    await load(`?categories=${categories}&videos=0&banners=0`);
    check(await page.locator('[data-block-type="Category"]').count()===categories,`${categories} configured categories render exactly once`);
    check(await page.evaluate(()=>document.documentElement.scrollWidth)<=1366,`${categories} categories create no horizontal overflow`);
  }
  await load('?noNews=true&videos=1&banners=0');
  check(await page.locator('.home-feature-grid').evaluate(e=>{const service=e.querySelector('.home-service-panel').getBoundingClientRect(),video=e.querySelector('.home-video-section').getBoundingClientRect();return service.right<=video.left+1||service.bottom<=video.top+1||video.bottom<=service.top+1;}),'Video and services never overlap when all news is absent');
  for(const count of [0,1,3]){
    await load(`?videos=${count}&banners=${count}&sidebar=${count>0}`);
    check(await page.locator('.home-video-card').count()===count,`Video ${count} data items rendered without fabricated fallback`);
    check(await page.locator('[data-event-slide]').count()===count,`Banner ${count} data items retained`);
    check(await page.locator('[data-event-dot]').count()===(count>1?count:0),`Banner ${count} dots only when needed`);
    if(count===0){check(await page.locator('.home-video-section,.home-event-strip,.home-sidebar-banners').count()===0,'Absent video/banner leave no empty card');
      check(await page.locator('.home-information-row>section').count()===4,'Missing video does not remove the four information panels');
      check(await page.locator('.home-feature-grid').evaluate(e=>Math.abs(e.getBoundingClientRect().width-e.querySelector('.home-service-panel').getBoundingClientRect().width)<2),'Missing video lets service panel use the available row');}
    if(count===3){await page.locator('[data-event-dot="2"]').click();await page.waitForTimeout(450);check(await page.locator('[data-event-dot="2"]').getAttribute('aria-current')==='true','Banner dots select requested CMS banner');}
  }
  await load('?hideFeatured=true&hideDocuments=true&hideNotices=true&hideVideo=true&banners=0');
  check(await page.locator('.reference-lead,.document-preview,.home-notices,.home-video-section').count()===0,'Disabled featured/documents/notices/video are not re-enabled by layout');
  check(await page.locator('[data-block-type="Category"]').count()===4,'Enabled configured categories remain available exactly once');
  for(const fallback of [false,true]){
    await load('?noNews=true&noCategories=true&hideDocuments=true&hideNotices=true&hideVideo=true&banners=0'+(fallback?'&fallback=true':''));
    check(await page.locator('.reference-lead,.home-secondary-list,.home-highlights,.home-video-section,.home-information-row>section').count()===0,`${fallback?'fallback':'configured'} missing data leaves no fabricated news/video/information panel`);
    check(await page.locator('.home-service-panel').count()===1,`${fallback?'fallback':'configured'} real service links remain available without content`);
    check(await page.locator('.home-feature-grid').evaluate(e=>Math.abs(e.getBoundingClientRect().width-e.querySelector('.home-service-panel').getBoundingClientRect().width)<2),`${fallback?'fallback':'configured'} missing news/video does not leave an empty feature column`);
    check(await page.locator('.home-information-row').evaluate(e=>e.getBoundingClientRect().height===0),`${fallback?'fallback':'configured'} absent information has no empty row height`);
    check(await page.evaluate(()=>document.documentElement.scrollWidth)<=1366,`${fallback?'fallback':'configured'} empty content state has no horizontal overflow`);
  }
  await load('?noImages=true&videos=0&banners=0');
  check(await page.locator('.home-news-cluster img').count()===0,'No-image news does not fabricate image placeholders');
  check(await page.locator('.reference-lead').evaluate(e=>getComputedStyle(e).color)!=='rgb(255, 255, 255)','No-image primary news remains readable on light background');
  const utilityLinks=await page.locator('.home-quick-lookup-links>a').evaluateAll(es=>es.map(e=>e.getAttribute('href')));
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
  await load('?videos=0&banners=0');
  const serviceSearch=page.locator('.home-service-panel form[action="/tim-kiem"]');
  await serviceSearch.locator('input[name="q"]').fill('văn bản đất đai');await serviceSearch.locator('button[type="submit"]').click();
  check(new URL(page.url()).pathname==='/tim-kiem'&&new URL(page.url()).searchParams.get('q')==='văn bản đất đai','Service-panel quick search uses the existing search endpoint');
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
  await writeFile(resolve(evidence,'browser-result.json'),JSON.stringify({result:'PASS',startedUtc,completedUtc:new Date().toISOString(),checks,viewports:[1536,1366,1920,768,390,320],metrics,scope:'Real compiled MVC/Razor over loopback; synthetic readmodels/components; actual static test MP4 playback/range; no SQL/CMS authorization/FFmpeg processing assertions'},null,2));
  console.log('RESULT PASS '+checks);
}catch(error){await writeFile(resolve(evidence,'browser-failure.json'),JSON.stringify({startedUtc,completedUtc:new Date().toISOString(),checks,error:String(error),errors,networkErrors,external,metrics},null,2));throw error;}
finally{await browser.close();}
