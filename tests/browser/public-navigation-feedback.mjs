import {pathToFileURL} from 'node:url';
import {writeFile} from 'node:fs/promises';
import {join} from 'node:path';
import assert from 'node:assert/strict';
const [base,out]=process.argv.slice(2);
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(base));
const modulePath=process.env.CMS_PLAYWRIGHT_MODULE;
const {chromium}=await import(modulePath.startsWith('file:')?modulePath:pathToFileURL(modulePath));
const browser=await chromium.launch({channel:'msedge',headless:true});
try {
 const page=await browser.newPage();await page.goto(base+'/');
 let release;const wait=new Promise(resolve=>release=resolve);
 await page.route('**/tin-tuc?navigation-fixture=1',async route=>{await wait;await route.continue();});
 const feedback=await page.evaluate(async()=>{
  const a=document.createElement('a');a.href='/tin-tuc?navigation-fixture=1';a.textContent='Navigation fixture';document.body.prepend(a);
  const start=performance.now();a.click();
  await new Promise(requestAnimationFrame);
  return {ms:performance.now()-start,busy:document.querySelector('main').getAttribute('aria-busy'),cursor:getComputedStyle(a).cursor};
 });
 assert.equal(feedback.busy,'true');assert.equal(feedback.cursor,'progress');assert(feedback.ms<100);
 const loaded=page.waitForURL('**/tin-tuc?navigation-fixture=1');release();await loaded;
 assert.equal(await page.locator('main').getAttribute('aria-busy'),null);
 await page.goBack();assert.equal(await page.locator('main').getAttribute('aria-busy'),null);
 await page.evaluate(()=>{
  for(const attrs of [{href:'#main-content'},{href:'/media/00000000-0000-0000-0000-000000000000'},{href:'/tin-tuc',target:'_blank'},{href:'/tin-tuc',download:'file'}]){
   const a=document.createElement('a');Object.assign(a,attrs);document.body.append(a);
   // Cancel default navigation after the application handler has evaluated it.
   window.addEventListener('click',e=>e.preventDefault(),{once:true});a.click();a.remove();
   if(document.body.classList.contains('public-navigation-pending'))throw Error('Non-page action marked pending');
  }
 });
 await writeFile(join(out,'feedback.json'),JSON.stringify({feedback,backRestored:true,nonPageActionsUnchanged:true},null,2));
 console.log('FEEDBACK_PASS');
} finally {await browser.close();}
