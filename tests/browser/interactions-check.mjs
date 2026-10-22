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
context.on('page',p=>p.on('pageerror',e=>report.errors.push(e.message)));
await context.route('**/*',route=>{if(new URL(route.request().url()).origin!==fixture.baseUrl){report.external.push('blocked');return route.abort();}return route.continue();});
const assert=(value,reason)=>{if(!value)throw new Error(reason);};
try{
 await page.goto(fixture.baseUrl+'/admin/login');await page.locator('[name=UserName]').fill(fixture.username);await page.locator('[name=Password]').fill(fixture.password); await solveLoginCaptcha(page);await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();await page.waitForURL('**/admin');
 const routes={comment:'/tuong-tac/Comment/'+fixture.articleId,inquiry:'/tuong-tac/Inquiry',contact:'/tuong-tac/Contact',consultation:'/tuong-tac/Consultation/'+fixture.draftId,inbox:'/admin/tuong-tac',detail:'/admin/tuong-tac/'+fixture.commentId,questions:'/tuong-tac/hoi-dap-da-duyet',article:'/bai-viet/'+fixture.articleId};
 for(const width of [390,1440,360,768,1024]){
  await page.setViewportSize({width,height:900});
  for(const [name,path] of Object.entries(routes)){
   const response=await page.goto(fixture.baseUrl+path);await page.locator('h1').first().waitFor();
   const layout=await page.evaluate(()=>({scroll:document.documentElement.scrollWidth,unlabelled:[...document.querySelectorAll('input:not([type=hidden]),textarea,select')].filter(x=>x.getClientRects().length&&!x.labels?.length&&!x.getAttribute('aria-label')).map(x=>x.name||x.id)}));
   const pass=response.status()===200&&layout.scroll<=width+1&&layout.unlabelled.length===0;report.pages.push({name,width,status:response.status(),pass,...layout});await page.screenshot({path:resolve(evidence,`${name}-${width}.png`),fullPage:true});assert(pass,`Interaction ${name} ${width}px layout/label failed.`);
  }
 }
 await page.goto(fixture.baseUrl+'/tuong-tac/Contact');await page.locator('[name="Input.Name"]').focus();await page.keyboard.type('Tên kiểm tra bằng bàn phím');await page.keyboard.press('Tab');assert(await page.locator('[name="Input.Email"]').evaluate(x=>document.activeElement===x),'Keyboard field focus incorrect.');
 await page.locator('.interaction-panel button[type=submit]:not([formaction])').click();assert(await page.locator('[name="Input.Email"]').evaluate(x=>!x.validity.valid),'Required email validation missing.');report.actions.push({name:'Keyboard focus and required-field validation',pass:true});
 assert(report.errors.length===0&&report.external.length===0,'Unexpected JS/off-origin request.');
}catch(error){report.failure=error.message;process.exitCode=1;}
finally{await browser.close();await writeFile(resolve(evidence,'ui-report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({pages:report.pages.length,pass:report.pages.filter(x=>x.pass).length,actions:report.actions.length,errors:report.errors.length,failure:report.failure||null}));}
