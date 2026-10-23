import {chromium} from 'playwright';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {solveLoginCaptcha} from './login-captcha.mjs';
const f=JSON.parse(await readFile(process.argv[2],'utf8')),folder=resolve(process.argv[3]);
const browser=await chromium.launch({channel:'msedge',headless:true});
const context=await browser.newContext({locale:'en-US'}),page=await context.newPage();
const report={actions:[],pages:[],errors:[],external:[]};
page.on('pageerror',e=>report.errors.push(e.message));
await context.route('**/*',r=>new URL(r.request().url()).origin===f.baseUrl?r.continue():(report.external.push('blocked'),r.abort()));
function check(v,why){if(!v)throw new Error(why);}
try{
 for(const width of [1440,390]){
  await page.setViewportSize({width,height:900});
  await page.goto(f.baseUrl+'/van-ban?q='+f.marker);
  await page.locator('#issued-from').fill('04/11/2026');await page.locator('#issued-to').fill('04/11/2026');
  await page.getByRole('button',{name:'Tra cứu',exact:true}).click();await page.waitForURL('**/van-ban?**');
  check(await page.locator('#issued-from').inputValue()==='04/11/2026','Browser locale reversed date');
  check((await page.locator('.document-table tbody tr').count())===1,'Browser date SQL filter wrong');
  await page.locator('#issued-to').fill('03/11/2026');check(!await page.locator('#issued-to').evaluate(e=>e.checkValidity()),'Client reversed range accepted');
  report.actions.push({width,date:'04/11/2026',browserLocale:'en-US',invalidRange:'DENIED'});
  for(const kind of ['Inquiry','Contact']){
   await page.goto(f.baseUrl+'/tuong-tac/'+kind);
   check(!await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2),'Public form viewport overflow '+width);
   await page.locator('[name="Input.Name"]').fill('Anonymous browser QA');
   await page.getByRole('button',{name:'Làm mới CAPTCHA',exact:true}).click();await page.waitForLoadState('domcontentloaded');
   check(await page.locator('[name="Input.Name"]').inputValue()==='Anonymous browser QA','Refresh lost fields');
   const submit=page.locator('button[type=submit]').last();await submit.focus();check(await submit.evaluate(e=>e===document.activeElement),'Submit keyboard focus missing');
   if(kind==='Contact')await page.screenshot({path:resolve(folder,'contact-'+width+'.png'),fullPage:true});
   report.pages.push({name:kind,width,overflow:false,refreshRetainsFields:true,keyboardFocus:true});
  }
 }
 await page.goto(f.baseUrl+'/admin/login');await page.locator('[name=UserName]').fill(f.username);await page.locator('[name=Password]').fill(f.password);await solveLoginCaptcha(page);await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();await page.waitForURL('**/admin');
 for(const width of [1440,390]){
  await page.setViewportSize({width,height:900});
  for(const [name,path] of [['roles','/admin/roles'],['site','/admin/site'],['videos','/admin/videos'],['schedules','/admin/schedules']]){
   const response=await page.goto(f.baseUrl+path);check(response.status()===200,name+' failed');
   check(!await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2),name+' viewport overflow '+width);
   const text=await page.locator('body').innerText();check(!/site\.manage|content\.publish|\bNotScanned\b|\bPendingReview\b/.test(text),'Raw machine labels '+name);
   report.pages.push({name,width,http:200,overflow:false,labels:'Vietnamese'});
  }
  await page.goto(f.baseUrl+'/admin/edit/'+f.realWeekId);
  check(await page.locator('[name^="ScheduleRows["][name$=".Date"]').evaluateAll(elements=>elements.filter(e=>e.value).length)===10,'Real Word private preview lost rows');
  const location=page.locator('[name="ScheduleRows[0].Location"]'),original=await location.inputValue();await location.fill('Kiểm tra chỉnh sửa riêng tư');check(await location.inputValue()==='Kiểm tra chỉnh sửa riêng tư','Word preview not editable');await location.fill(original);
  check(!await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2),'Private schedule editor viewport overflow '+width);
  await page.goto(f.baseUrl+'/admin/videos/upload/'+f.videoDraftId);check(await page.locator('input[type=file]').count()>0,'Direct video file upload control absent');
  await page.goto(f.baseUrl+'/bai-viet/'+f.videoPublicId);const player=page.locator('video').first();await player.evaluate(e=>e.load());await page.waitForFunction(()=>document.querySelector('video')?.readyState>=1);await player.evaluate(e=>{e.currentTime=Math.min(1,e.duration/2)});await page.waitForFunction(()=>document.querySelector('video')?.currentTime>0);
  report.actions.push({name:'Private real Word ten editable rows and direct video upload/internal MP4 seek',width,result:'PASS'});
 }
 await page.goto(f.baseUrl+'/');const toggle=page.locator('.mobile-menu-toggle');if(await toggle.isVisible())await toggle.click();
 const menu=page.locator('summary').filter({hasText:'Công khai thông tin'}).first();await menu.click();
 for(const label of ['Giấy chứng nhận sai hỏng, hủy','Người nước ngoài định cư ở Việt Nam','Tổ chức vi phạm pháp luật đất đai'])check(await page.getByRole('link',{name:label,exact:true}).count()>0,'Missing official menu '+label);
 report.actions.push({name:'Configured Test FR17 mobile menu all three official names',result:'PASS'});
 check(!report.errors.length&&!report.external.length,'Browser JS errors or external requests');
}finally{await writeFile(resolve(folder,'ui-report.json'),JSON.stringify(report,null,2),{flag:'wx'});await browser.close();}
