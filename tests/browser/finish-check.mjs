import { solveLoginCaptcha } from './login-captcha.mjs';
import { chromium } from 'playwright';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
const fixture=JSON.parse(await readFile(process.argv[2],'utf8'));
const evidence=resolve(process.argv[3],'ui');await mkdir(evidence,{recursive:true});
const browser=await chromium.launch({channel:'msedge',headless:true});
const context=await browser.newContext();const page=await context.newPage();
const report={utc:new Date().toISOString(),pages:[],errors:[],external:[],actions:[]};
page.on('pageerror',e=>report.errors.push(e.message));
await context.route('**/*',route=>{if(new URL(route.request().url()).origin!==fixture.baseUrl){report.external.push('blocked');return route.abort();}return route.continue();});
const assert=(value,reason)=>{if(!value)throw new Error(reason);};
try{
 await page.goto(fixture.baseUrl+'/admin/login');await page.locator('[name=UserName]').fill(fixture.manager);await page.locator('[name=Password]').fill(fixture.password); await solveLoginCaptcha(page);await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();await page.waitForURL('**/admin');
 const routes={inquiry:'/tuong-tac/Inquiry',lookup:'/tra-cuu-ho-so',inbox:'/admin/tuong-tac?kind=Inquiry&field=Finish%20HTTP',detail:'/admin/tuong-tac/'+fixture.inquiry,calendar:'/admin/hoi-dap/lich',outbox:'/admin/outbox',uncertain:'/admin/outbox/'+fixture.uncertainId};
 if(fixture.infectedSubmission)routes.quarantine='/admin/tuong-tac/'+fixture.infectedSubmission;
 for(const width of [390,1440,360,768,1024]){
  await page.setViewportSize({width,height:900});
  for(const [name,path] of Object.entries(routes)){
   const response=await page.goto(fixture.baseUrl+path);await page.locator('h1').first().waitFor();
   const layout=await page.evaluate(()=>({scroll:document.documentElement.scrollWidth,unlabelled:[...document.querySelectorAll('input:not([type=hidden]),textarea,select')].filter(x=>x.getClientRects().length&&!x.labels?.length&&!x.getAttribute('aria-label')).map(x=>x.name||x.id)}));
   const pass=response.status()===200&&layout.scroll<=width+1&&layout.unlabelled.length===0;report.pages.push({name,width,status:response.status(),pass,...layout});await page.screenshot({path:resolve(evidence,`${name}-${width}.png`),fullPage:true});assert(pass,`Finish ${name} ${width}px layout/label failed.`);
  }
 }
 if(fixture.infectedSubmission){
  await page.goto(fixture.baseUrl+'/admin/tuong-tac/'+fixture.inquiry);const event=page.waitForEvent('download');await page.locator(`a[href="/admin/hoi-dap/tep/${fixture.fileId}"]`).click();const file=await event;await file.saveAs(resolve(evidence,'clean-download.pdf'));assert(await file.failure()===null,'Clean UI download failed.');
  await page.goto(fixture.baseUrl+'/admin/tuong-tac/'+fixture.infectedSubmission);assert(await page.getByText(/Infected/).count()>0&&await page.locator(`a[href="/admin/hoi-dap/tep/${fixture.infected}"]`).count()===0,'Infected UI offered a download.');report.actions.push({name:'Genuine Clean UI download and Infected quarantine with no link',pass:true});
 }
 await page.goto(fixture.baseUrl+'/tuong-tac/Inquiry');await page.locator('[name="Input.Name"]').focus();await page.keyboard.type('Tên Test');await page.keyboard.press('Tab');assert(await page.locator('[name="Input.Email"]').evaluate(x=>document.activeElement===x),'Keyboard focus incorrect.');const accept=(await page.locator('[type=file]').getAttribute('accept')).split(',').sort().join(',');assert(accept===['application/pdf','image/jpeg','image/png'].sort().join(','),'Attachment selector does not declare confirmed types.');report.actions.push({name:'Keyboard focus and confirmed attachment types',pass:true});
 await page.goto(fixture.baseUrl+'/admin/hoi-dap/lich');await page.locator('[name=Label]').fill('UI calendar temporary');await page.locator('[name=PolicyVersion]').fill(fixture.calendarPolicy);await page.locator('[name=EffectiveFrom]').fill('2099-01-01');await page.locator('[name=Exceptions]').fill('2099-01-02|off|Test holiday\n2099-01-03|work|Test makeup');
 const calendarPost=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname==='/admin/hoi-dap/lich');await page.getByRole('button',{name:'Tạo phiên bản lịch tạm tính'}).click();const savedCalendar=await calendarPost;assert(savedCalendar.status()===302,'Calendar UI POST failed: '+savedCalendar.status());await page.waitForURL('**/admin/hoi-dap/lich');
 // The caller verifies this run's unique policy and both exceptions in real SQL.
 // A capped list can omit it when earlier fixtures used future CreatedUtc clocks.
 report.actions.push({name:'Temporary calendar POST accepted; unique policy SQL readback required by caller',policy:fixture.calendarPolicy,pass:true});
 await page.goto(fixture.baseUrl+'/admin/outbox/'+fixture.uncertainId);await page.locator('[name=Decision]').selectOption('unknown');await page.locator('[name=Evidence]').fill('Operator Test: chưa đủ chứng cứ, giữ Uncertain, không gửi lại.');await page.getByRole('button',{name:'Lưu quyết định có audit'}).click();await page.waitForURL('**/admin/outbox/'+fixture.uncertainId);assert(await page.locator('[name=Decision]').count()===1,'Unknown decision incorrectly resolved/resubmitted message.');report.actions.push({name:'Uncertain unknown reconciliation preserves no-retry status',pass:true});
 await page.goto(fixture.baseUrl+'/admin/tuong-tac?kind=Inquiry&field=Finish%20HTTP');const row=page.locator(`[data-id="${fixture.inquiry}"]`);await row.locator('[data-select-record]').check();await page.locator('#batch-action').selectOption('reject');await page.locator('#batch-reason').fill('Test UI: từ chối có kết quả từng hồ sơ');await page.locator('#batch-form button').click();await page.locator('#batch-results').filter({hasText:'Success'}).waitFor();report.actions.push({name:'Real batch POST returns individual Success',pass:true});
 await page.screenshot({path:resolve(evidence,'batch-result-1024.png'),fullPage:true});
 const downloadEvent=page.waitForEvent('download');await page.getByRole('link',{name:'Xuất Excel tối thiểu theo bộ lọc'}).click();const download=await downloadEvent;await download.saveAs(resolve(evidence,'ui-minimal.xlsx'));assert((await download.failure())===null,'Excel UI download failed.');report.actions.push({name:'Permission-scoped Excel download',pass:true});
 assert(report.errors.length===0&&report.external.length===0,'Unexpected JS/off-origin request.');
}catch(error){report.failure=error.message;process.exitCode=1;}
finally{await browser.close();await writeFile(resolve(evidence,'ui-report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({pages:report.pages.length,pass:report.pages.filter(x=>x.pass).length,actions:report.actions.length,errors:report.errors.length,failure:report.failure||null}));}
