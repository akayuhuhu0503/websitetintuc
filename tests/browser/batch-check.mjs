import {chromium} from 'playwright';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {resolve} from 'node:path';
const fixture=JSON.parse(await readFile(process.argv[2],'utf8'));const folder=resolve(process.argv[3],'ui');await mkdir(folder,{recursive:true});
const report={utc:new Date().toISOString(),pages:[],errors:[],external:[]};const browser=await chromium.launch({channel:'msedge',headless:true});
try{
 const page=await browser.newPage();page.on('pageerror',e=>report.errors.push(e.message));
 await page.route('**/*',route=>{if(new URL(route.request().url()).origin!==fixture.baseUrl){report.external.push(route.request().url());return route.abort();}return route.continue();});
 for(const width of [390,1440]){
  await page.setViewportSize({width,height:900});let index=0;
  for(const path of fixture.paths){const response=await page.goto(fixture.baseUrl+path);await page.locator('main').first().waitFor();await page.evaluate(async()=>{await Promise.all([...document.images].map(async x=>{x.loading='eager';try{await x.decode();}catch{}}));});const result=await page.evaluate(()=>({scroll:document.documentElement.scrollWidth,broken:[...document.images].filter(x=>x.src.includes('/media/')&&(!x.complete||x.naturalWidth===0)).map(x=>x.src)}));
   const pass=response.status()===200&&result.scroll<=width+1&&result.broken.length===0;report.pages.push({width,path,status:response.status(),pass,...result});await page.screenshot({path:resolve(folder,`page-${index++}-${width}.png`),fullPage:true});if(!pass)throw new Error('Batch layout/media failed '+path+' '+width);
  }
 }
 if(report.errors.length||report.external.length)throw new Error('Unexpected script/off-origin dependency');
}catch(e){report.failure=e.message;process.exitCode=1;}finally{await browser.close();await writeFile(resolve(folder,'ui-report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({pages:report.pages.length,pass:report.pages.filter(x=>x.pass).length,errors:report.errors.length,external:report.external.length,failure:report.failure}));}
