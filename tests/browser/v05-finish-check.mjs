import {chromium} from 'playwright';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {resolve} from 'node:path';
import {solveLoginCaptcha} from './login-captcha.mjs';
const f=JSON.parse(await readFile(process.argv[2],'utf8')),dir=resolve(process.argv[3],'ui');await mkdir(dir,{recursive:true});
const browser=await chromium.launch({channel:'msedge',headless:true}),context=await browser.newContext(),page=await context.newPage();
const report={pages:[],actions:[],errors:[],external:[]};page.on('pageerror',e=>report.errors.push(e.message));
await context.route('**/*',r=>new URL(r.request().url()).origin===f.baseUrl?r.continue():(report.external.push('blocked'),r.abort()));
function check(value,why){if(!value)throw new Error(why);}
try{
 await page.goto(f.baseUrl+'/admin/login');await page.locator('[name=UserName]').fill(f.username);await page.locator('[name=Password]').fill(f.password);await solveLoginCaptcha(page);await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();await page.waitForURL('**/admin');
 report.actions.push({name:'Browser actual login answers rendered CAPTCHA and authenticates with Identity',status:'PASS'});
 for(const width of [1440,390]){
  await page.setViewportSize({width,height:900});
  for(const [name,path] of [['documents','/admin/documents/'+f.contentId],['word-preview','/admin/edit/'+f.contentId+'?documentId='+f.badId],['weekly','/lich-tuan?year='+f.year+'&week='+f.week],['search-answers','/tim-kiem?kind=Inquiry'],['menu','/']]){
   const response=await page.goto(f.baseUrl+path);check(response.status()===200,`${name} HTTP ${response.status()}`);
   // The immutable long hash/table can scroll within its designated table container.
   const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);check(!overflow,`${name} viewport overflow ${width}`);
   await page.screenshot({path:resolve(dir,`${name}-${width}.png`),fullPage:true});report.pages.push({name,width,http:response.status(),overflow});
  }
 }
 await page.goto(f.baseUrl+'/admin/edit/'+f.contentId+'?documentId='+f.badId);check((await page.locator('.editor-notice').allTextContents()).join(' ').includes('Sai mẫu'),'Parser diagnostic missing in actual browser');
 await page.locator('[name="ScheduleRows[0].Location"]').fill('Browser manual correction');await page.locator('[name="ScheduleRows[0].Location"]').blur();
 check(await page.locator('[name="ScheduleRows[0].Location"]').inputValue()==='Browser manual correction','Preview cannot be corrected');
 report.actions.push({name:'Actual parser-error preview exposes correction fields, no publish click/no official fixture',status:'PASS'});
 await page.goto(f.baseUrl+'/');const menu=page.locator('summary').filter({hasText:'Công khai thông tin'});
 if(await menu.count()){const toggle=page.locator('.mobile-menu-toggle');if(await toggle.isVisible()&&await toggle.getAttribute('aria-expanded')!=='true')await toggle.click();await menu.first().click();check(!await page.getByText('Người nước ngoài định cư ở Việt Nam',{exact:true}).count(),'Unapproved category exposed');await page.screenshot({path:resolve(dir,'menu-open-390.png'),fullPage:true});report.actions.push({name:'FR17 mobile navigation opens with approved names only',status:'PASS'});}
 check(report.errors.length===0&&report.external.length===0,'JS or off-origin request');
}finally{await writeFile(resolve(dir,'ui-report.json'),JSON.stringify(report,null,2),{flag:'wx'});await browser.close();}
