// Isolated browser layout check: actual CSS/JS and the shared Razor article markup.
// No SQL, accounts, operational HTTP requests, screenshots or media writes.
import { chromium } from 'playwright';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';
import assert from 'node:assert/strict';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const read = p => readFile(resolve(root, p), 'utf8');
const partial = await read('src/Web/Views/Shared/_HomeEventStrip.cshtml');
const configured = await read('src/Web/Views/Home/Configured.cshtml');
const fallback = await read('src/Web/Views/Home/Index.cshtml');
const controller = await read('src/Web/Controllers/HomeController.cs');
assert(configured.includes('<partial name="_HomeEventStrip" model="Model.Site" />'));
assert(controller.includes('if (site is not null) return View("Configured"'));
assert(!fallback.includes('home-event-strip')); // No public configuration: no fabricated banner.
const css = (await Promise.all(['site','site-config','interactions','public-reflow','portal-design','typography','home'].map(n => read(`src/Web/wwwroot/css/${n}.css`)))).join('\n');
const script = await read('src/Web/wwwroot/js/site.js');
const adminScript = await read('src/Web/wwwroot/js/site-admin.js');
const adminView = await read('src/Web/Views/SiteAdmin/Edit.cshtml');
assert(adminView.includes('id="site_position"') && adminView.includes('id="event-banner-crop-warning"'));
const sizes = [[1170,100],[11700,1000],[4000,4000],[1000,6000],[8000,200]];
const svg = (w,h) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}"><rect width="100%" height="100%" fill="#8B2E2E"/></svg>`;
const article = partial.match(/<article[\s\S]*?<\/article>/)[0];
function strip(count) {
    if (!count) return '';
    const slides = Array.from({length:count}, (_,i) => article
        .replace('hidden="@(i>0)"',i?'hidden':'')
        .replace('aria-label="@(i+1) / @banners.Length"',`aria-label="${i+1} / ${count}"`)
        .replace(/<a [^\n]+>/,`<a href="/fixture-link-${i}" aria-label="Fixture ${i}">`)
        .replace('@banner.ImageUrl',`http://fixture.test/image/${i}`).replace('@banner.Alt',`Fixture ${i}`)).join('');
    return `<section class="home-event-strip" data-event-carousel><div class="event-slides">${slides}</div>${count>1?`<nav class="event-dots" data-event-dots hidden>${Array.from({length:count},(_,i)=>`<button data-event-dot="${i}"><span></span></button>`).join('')}</nav>`:''}</section>`;
}
const browser = await chromium.launch({channel:'msedge',headless:true});
const page = await browser.newPage();
let dimension = sizes[0], checks = 0;
const errors=[];page.on('pageerror',e=>errors.push(e.message));
await page.route('**/*',route=>route.request().url().startsWith('http://fixture.test/')
    ?route.fulfill({contentType:'image/svg+xml',body:svg(...dimension)}) :route.abort());
async function render(count) {
    await page.goto('about:blank');
    await page.setContent(`<style>${css}</style><body class="public-portal portal-shell portal-public is-home"><main class="main-content">${strip(count)}<div id="following">Following content</div></main></body>`);
    await page.addScriptTag({content:script});
    if(count) await page.waitForFunction(()=>[...document.querySelectorAll('.event-slide img')].every(i=>i.complete&&i.naturalWidth));
}
async function measure() {
    const result=await page.evaluate(()=>{
        const e=document.querySelector('.home-event-strip'),r=e.getBoundingClientRect(),p=e.parentElement.getBoundingClientRect();
        const img=e.querySelector('.is-active img')||e.querySelector('img'),ir=img.getBoundingClientRect();
        return {w:r.width,h:r.height,center:r.x+r.width/2,parentCenter:p.x+p.width/2,iw:ir.width,ih:ir.height,fit:getComputedStyle(img).objectFit,overflow:document.documentElement.scrollWidth>innerWidth};
    });
    assert(result.w<=1170.1&&result.h<=100.1,JSON.stringify(result));
    assert(Math.abs(result.h-result.w*10/117)<0.1,JSON.stringify(result));
    assert(Math.abs(result.center-result.parentCenter)<0.1);
    assert(Math.abs(result.iw-result.w)<0.1&&Math.abs(result.ih-result.h)<0.1);
    assert.equal(result.fit,'cover');assert.equal(result.overflow,false);checks++;
    return result;
}
try {
    for(const width of [1920,1366,768,390]){
        await page.setViewportSize({width,height:900});
        for(dimension of sizes){await render(1);await measure();assert.equal(await page.locator('[data-event-dot]').count(),0);}
        await render(0);assert.equal(await page.locator('.home-event-strip').count(),0);checks++;
        await render(3);await measure();await page.locator('[data-event-dot="2"]').click();await page.waitForTimeout(450);await measure();
        assert.equal(await page.locator('.is-active a').getAttribute('href'),'/fixture-link-2');
    }
    await page.setViewportSize({width:1366,height:900});await page.mouse.move(0,0);await render(3);await page.bringToFront();
    await page.waitForFunction(()=>document.querySelector('[data-event-dot="1"]')?.getAttribute('aria-current')==='true',{},{timeout:7000});checks++;
    // Real image load and CMS change events; warning is advisory and position-scoped.
    for(dimension of sizes){
        await page.goto('about:blank');
        await page.setContent(`<select class="site-image-select"><option value="one" selected>Fixture</option><option value="">None</option></select><select id="site_position"><option>home_event_strip</option><option>home</option></select><img id="site-image-preview"><p id="event-banner-crop-warning" hidden>Crop warning</p>`);
        // Serve only the existing authenticated preview URL in this isolated fixture.
        await page.route('**/admin/site/media/**',r=>r.fulfill({contentType:'image/svg+xml',body:svg(...dimension)}));
        await page.evaluate(()=>{const base=document.createElement('base');base.href='http://fixture.test/';document.head.append(base);});
        await page.addScriptTag({content:adminScript});
        await page.waitForFunction(()=>document.querySelector('img').complete&&document.querySelector('img').naturalWidth);
        assert.equal(await page.locator('#event-banner-crop-warning').isVisible(),Math.min(dimension[0]/dimension[1]/11.7,11.7/(dimension[0]/dimension[1]))<.9);checks++;
        await page.selectOption('#site_position','home');assert.equal(await page.locator('#event-banner-crop-warning').isVisible(),false);checks++;
        await page.selectOption('#site_position','home_event_strip');await page.selectOption('.site-image-select','');assert.equal(await page.locator('#event-banner-crop-warning').isVisible(),false);checks++;
    }
    assert.deepEqual(errors,[]);
    console.log(JSON.stringify({result:'PASS',checks,viewports:[1920,1366,768,390],imageSizes:sizes,scope:'Isolated Edge layout/CMS warning; compiled Razor separately, no SQL/IIS/CMS workflow E2E'}));
} finally { await browser.close(); }
