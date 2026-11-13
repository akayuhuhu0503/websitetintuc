// Focused homepage geometry checks; compiled Razor fixture, no SQL or CMS writes.
import {pathToFileURL,fileURLToPath} from 'node:url';
import {resolve,dirname} from 'node:path';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const {chromium}=await import(pathToFileURL(process.env.CMS_PLAYWRIGHT_MODULE||resolve(root,'tests/browser/node_modules/playwright/index.mjs')));
const base=process.argv[2]||'http://127.0.0.1:5407';
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(base),'Loopback fixture only');
const out=resolve(root,'.local/tests/public-wide');await mkdir(out,{recursive:true});
const browser=await chromium.launch({channel:'msedge',headless:true});
const page=await browser.newPage({deviceScaleFactor:1});
const results=[],errors=[];let checks=0;
page.on('pageerror',e=>errors.push(e.message));
function check(condition,message){assert(condition,message);checks++;}
async function load(query){const r=await page.goto(base+'/?'+query,{waitUntil:'networkidle'});assert.equal(r.status(),200);await page.evaluate(()=>document.fonts.ready);}
async function measure(){return page.evaluate(()=>{
 const rect=e=>{if(!e)return null;const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height,right:r.right,bottom:r.bottom};};
 const get=s=>rect(document.querySelector(s));
 const frame=get('main'),body=getComputedStyle(document.body);
 return {innerWidth,outerWidth,clientWidth:document.documentElement.clientWidth,dpr:devicePixelRatio,scale:visualViewport.scale,zoom:body.zoom,transform:body.transform,token:body.getPropertyValue('--gdla-container'),maxWidth:getComputedStyle(document.querySelector('main')).maxWidth,scrollWidth:document.documentElement.scrollWidth,frame,left:frame.x,right:innerWidth-frame.right,
 aligned:['.masthead-inner','.main-nav>.container','.home-quick-lookup','.home-feature-grid','.home-information-row','.home-event-strip','.footer-main'].map(s=>({selector:s,rect:get(s)})),lead:get('.reference-lead'),secondary:get('.home-secondary-list'),highlights:get('.home-highlights'),service:get('.home-service-panel'),video:get('.home-video-section'),banner:get('.event-slides'),dots:get('.event-dots'),latest:get('.home-latest .news-card'),thumb:get('.home-latest .card-image'),
 columns:[...document.querySelectorAll('.home-feature-grid>section')].map(rect),
 overflow:[...document.querySelectorAll('.gdla-home article,.home-information-row>section,.home-service-panel')].filter(e=>e.clientWidth&&e.scrollWidth>e.clientWidth+2).map(e=>e.className),
 sideImages:[...document.querySelectorAll('.home-secondary-image')].map(e=>e.getBoundingClientRect().width)};
});}
const overlaps=(a,b)=>a&&b&&a.x<b.right-1&&a.right>b.x+1&&a.y<b.bottom-1&&a.bottom>b.y+1;
try{
 for(const width of [1366,1536,1920,2560,768,390,320]){
  await page.setViewportSize({width,height:1080});
  for(const fallback of [false,true]){
   await load('news=10&videos=1&banners=3'+(fallback?'&fallback=true':''));
   const m=await measure();results.push({fallback,...m});
   check(m.innerWidth===width&&m.scale===1&&m.zoom==='1'&&m.transform==='none','Measured CSS viewport; no zoom/scale enlargement');
   check(m.scrollWidth<=width+1&&m.overflow.length===0,'No page or card overflow');
   const expected=Math.min(1740,width-(width>900?64:width>480?32:24));
   check(Math.abs(m.frame.w-expected)<2,'Homepage uses bounded 1740px frame');
   check(m.aligned.filter(x=>x.rect).every(x=>Math.abs(x.rect.x-m.frame.x)<2&&Math.abs(x.rect.w-m.frame.w)<2),'Header/navigation/news/banner/footer aligned');
   if(width>=1366){
    check(m.lead.w>m.secondary.w&&m.secondary.w>300&&m.highlights.w>300,'Three balanced desktop columns');
    check(m.lead.h<=420&&m.lead.h>=310,'Lead image height remains bounded');
    check(m.sideImages.every(w=>w<=97),'Secondary thumbnails stay compact');
    check(Math.abs(m.service.y-m.video.y)<2&&!overlaps(m.service,m.video),'Services beside video');
    if(width===1920)check(m.left>=80&&m.left<=100&&m.right>=80&&m.right<=100,'1920px margins within requested range');
    if(width<=1536)check(m.left>=24&&m.left<=40,'Smaller desktop retains safe padding');
   }
   if(m.banner)check(Math.abs(m.banner.h-m.banner.w*10/117)<2&&m.dots.y>=m.banner.bottom-1,'Banner ratio and controls below image');
   check(m.latest&&m.latest.w<=761&&m.thumb.w<=m.latest.w*.4,'Single latest item never stretches its thumbnail');
   if(!fallback)await page.screenshot({path:resolve(out,`homepage-${width}.png`)});
  }
  for(const query of ['news=0&videos=1&banners=0','news=1&videos=0&banners=0','news=2&videos=1&banners=0','news=6&videos=1&banners=0','news=1&noImages=true&videos=0&banners=0']){
   await load(query);const m=await measure();results.push({query,...m});
   check(m.scrollWidth<=width+1&&m.overflow.length===0,'Sparse content remains within viewport');
   check(!overlaps(m.service,m.video),'Missing news cannot overlap service/video');
   check(!m.banner&&(query.startsWith('news=6')?m.highlights?.h<200:!m.highlights),'Sparse highlights shrink without an artificial banner');
   if(query.startsWith('news=2')&&width>=1366)check(m.secondary.h<m.lead.h,'One secondary story does not fill a tall empty card');
   if(m.lead)check(m.lead.h<=420,'Single/few stories do not inflate lead height');
  }
 }
 await page.setViewportSize({width:1920,height:1080});await load('news=10&videos=1');
 await page.evaluate(()=>document.body.classList.remove('is-home'));
 const nonHome=await measure();check(nonHome.frame.w===1400,'Non-home CSS frame remains 1400px');
 check(errors.length===0,'No JavaScript errors');
 await writeFile(resolve(out,'geometry.json'),JSON.stringify({result:'PASS',checks,results,nonHome,scope:'Isolated compiled Razor; CSS geometry only, not operational SQL or IIS'},null,2));
 console.log(JSON.stringify({result:'PASS',checks,desktop:results.filter(r=>!r.fallback&&!r.query&&r.innerWidth>=1366).map(r=>({viewport:r.innerWidth,frame:r.frame.w,left:r.left,right:r.right,leadHeight:r.lead.h,scale:r.scale,zoom:r.zoom}))}));
}finally{await browser.close();}
