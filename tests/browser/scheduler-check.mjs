import { solveLoginCaptcha } from './login-captcha.mjs';
import { chromium } from 'playwright';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const fixture=JSON.parse(await readFile(resolve(root,'.local/tests/phase2b/scheduler-ui.json'),'utf8'));
if(fixture.database!=='LandPortal_Test'||new URL(fixture.baseUrl).hostname!=='127.0.0.1')throw new Error('Scheduler UI requires isolated SQL Test loopback.');
const evidence=resolve(process.env.LandPortal__EvidenceRoot || resolve(root, '.local/evidence'), 'phase2b/scheduler/ui');await mkdir(evidence,{recursive:true});
const report={utc:new Date().toISOString(),database:fixture.database,pageChecks:[],actions:[],pageErrors:[],offOriginRequests:[],limitations:['Headless Edge viewport emulation, not physical mobile/UAT/WCAG certification.']};
const browser=await chromium.launch({channel:'msedge',headless:true});const context=await browser.newContext();
await context.route('**/*',async route=>new URL(route.request().url()).origin===new URL(fixture.baseUrl).origin?route.continue(): (report.offOriginRequests.push(new URL(route.request().url()).origin),route.abort()));
const page=await context.newPage();page.on('pageerror',error=>report.pageErrors.push(error.message));
function assert(value,message){if(!value)throw new Error(message);}
async function postClick(name){const response=page.waitForResponse(r=>r.request().method()==='POST');await page.getByRole('button',{name,exact:true}).click();const posted=await response;assert(posted.status()===302,`Scheduler form refused HTTP ${posted.status()}`);await page.waitForLoadState('networkidle');}
async function scan(name,width,path){await page.setViewportSize({width,height:900});const response=await page.goto(fixture.baseUrl+path,{waitUntil:'networkidle'});const layout=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,unlabelled:[...document.querySelectorAll('input:not([type=hidden]),textarea,select')].filter(x=>x.getClientRects().length&&!x.labels?.length&&!x.getAttribute('aria-label')).map(x=>x.name||x.id)}));const pass=response.status()===200&&layout.scroll<=width+1&&layout.unlabelled.length===0;report.pageChecks.push({name,width,path,status:response.status(),pass,...layout});await page.screenshot({path:resolve(evidence,`${name}-${width}.png`),fullPage:true});assert(pass,`${name} ${width}px reflow/label failed.`);}
// Native datetime-local canonicalizes zero seconds away; Playwright requires that exact form.
function vietnam(seconds){return new Date(Date.now()+seconds*1000+7*3600*1000).toISOString().slice(0,19).replace(/:00$/,'');}
try{
 await page.goto(fixture.baseUrl+'/admin/login');await page.locator('[name=UserName]').fill(fixture.user);await page.locator('[name=Password]').fill(fixture.password); await solveLoginCaptcha(page);await postClick('Đăng nhập');
 const paths=[['schedule-list','/admin/schedules'],['schedule-edit','/admin/schedules/edit?contentId='+fixture.contentId]];
 for(const width of [390,1440])for(const [name,path]of paths)await scan(name,width,path);
 for(const width of [360,768,1024])for(const [name,path]of paths)await scan(name,width,path);
 await page.goto(fixture.baseUrl+'/admin/schedules/edit?contentId='+fixture.contentId);await page.locator('[name=DueVietnam]').focus();assert(await page.locator('[name=DueVietnam]').evaluate(el=>el===document.activeElement),'Date field cannot receive keyboard focus.');let radioFocused=false;let tabStops=0;for(;tabStops<12;tabStops++){await page.keyboard.press('Tab');radioFocused=await page.locator('[name=Direct][value=false]').evaluate(el=>el===document.activeElement);if(radioFocused)break;}assert(radioFocused,'Keyboard traversal did not reach approval radio after native date/time subfields.');report.actions.push(`Keyboard traversed native datetime subfields to approval radio in ${tabStops+1} Tab presses.`);
 await page.locator('[name=DueVietnam]').fill(vietnam(120));await page.locator('[name=Direct][value=true]').check();await postClick('Lưu lịch SQL');assert((await page.locator('tbody tr').filter({hasText:fixture.title}).locator('.schedule-state').innerText()).trim()==='Pending','New SQL schedule not listed as Pending.');report.actions.push('Created SQL schedule by real admin form with UTC+7 date and direct branch.');
 const row=page.locator('tbody tr').filter({hasText:fixture.title});assert(await row.count()===1,'Scheduler list did not scope newly created content.');const cancelResponse=page.waitForResponse(r=>r.request().method()==='POST');await row.getByRole('button',{name:'Hủy lịch',exact:true}).click();assert((await cancelResponse).status()===302,'Cancel form refused.');await page.waitForLoadState('networkidle');assert((await page.locator('tbody tr').filter({hasText:fixture.title}).locator('.schedule-state').innerText()).trim()==='Canceled','Canceled SQL schedule was not reflected.');report.actions.push('Canceled schedule by real CSRF/rowversion form without publishing or withdrawing.');
 await page.goto(fixture.baseUrl+'/admin/schedules/edit?contentId='+fixture.contentId);await page.locator('[name=DueVietnam]').fill(vietnam(8));await page.locator('[name=Direct][value=true]').check();await postClick('Lưu lịch SQL');report.actions.push('Created a distinct future schedule for independent worker execution.');
 assert(report.pageErrors.length===0&&report.offOriginRequests.length===0,'JS failure or external request occurred.');report.pass=true;
}catch(error){report.pass=false;report.error=error.message;throw error;}finally{await writeFile(resolve(evidence,'ui-report.json'),JSON.stringify(report,null,2));await browser.close();}
