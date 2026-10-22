// Explicit, bounded source inspection. Never invoked by the regression runner.
import {chromium} from 'playwright';
import {lookup} from 'node:dns/promises';
import {writeFile,mkdir} from 'node:fs/promises';
import {resolve} from 'node:path';
import {randomUUID,createHash} from 'node:crypto';
if(!process.argv.includes('--live-source-inspection'))throw new Error('Explicit live source inspection flag required.');
const targets=['https://qldd.mae.gov.vn/chuyen-muc/do-dac-va-dang-ky-dat-dai-83.htm','https://qldd.mae.gov.vn/chuyen-muc/thong-ke-kiem-ke-dat-dai-81.htm'];
function public4(ip){const [a,b,c]=ip.split('.').map(Number);return ![0,10,127].includes(a)&&a<224&&!(a===169&&b===254)&&!(a===172&&b>=16&&b<=31)&&!(a===192&&(b===168||b===0))&&!(a===100&&b>=64&&b<=127)&&!(a===198&&[18,19].includes(b))&&!(a===198&&b===51&&c===100)&&!(a===203&&b===0&&c===113);}
const folder=resolve('.local/evidence/phase3a','browser-'+new Date().toISOString().replace(/[-:.]/g,'')+'-'+randomUUID().slice(0,8));await mkdir(folder,{recursive:false});
const report={utc:new Date().toISOString(),scope:'Two empty source categories only; anonymous read-only browser, separate from crawler GET counters',pinnedPublicIp:null,maximumRequests:20,requests:[],blocked:[],pages:[],errors:[]};
let browser,chain=Promise.resolve(),bytes=0;
try{
 const ips=(await lookup('qldd.mae.gov.vn',{all:true,family:4})).map(x=>x.address);
 if(!ips.length||ips.some(ip=>!public4(ip)))throw new Error('Non-public source DNS blocked.');report.pinnedPublicIp=ips[0];
 browser=await chromium.launch({channel:'msedge',headless:true,args:['--no-proxy-server','--host-resolver-rules=MAP qldd.mae.gov.vn '+ips[0]]});
 const context=await browser.newContext({serviceWorkers:'block',acceptDownloads:false});
 if(context.routeWebSocket)await context.routeWebSocket('**/*',r=>r.close());
 await context.route('**/*',async route=>{
  const req=route.request(),u=new URL(req.url()),type=req.resourceType();
  const staticFile=/\.(js|css)$/i.test(u.pathname)||/\/(ScriptResource|WebResource)\.axd$/i.test(u.pathname);
  const allowed=req.method()==='GET'&&u.hostname==='qldd.mae.gov.vn'&&u.port===''&&u.username===''&&['http:','https:'].includes(u.protocol)&&!/[?&](delete|save|remove|update|insert|action|cmd)=/i.test(u.search)&&((type==='document'&&targets.includes(u.href))||(['script','stylesheet'].includes(type)&&staticFile));
  if(!allowed){report.blocked.push({url:u.href,type,method:req.method()});await route.abort();return;}
  const task=chain.then(async()=>{
   if(report.requests.length>=20||bytes>=32*1024*1024){report.blocked.push({url:u.href,type,reason:'InspectionBudget'});await route.abort();return;}
   await new Promise(r=>setTimeout(r,2100));const entry={url:u.href,type,startedUtc:new Date().toISOString()};report.requests.push(entry);
   await route.continue();const response=await req.response();if(response){await response.finished();entry.status=response.status();const body=await response.body();bytes+=body.length;entry.bytes=body.length;entry.sha256=createHash('sha256').update(body).digest('hex');if(body.length>8*1024*1024)throw new Error('Source response exceeded inspection size.');}
  });chain=task.catch(error=>{report.errors.push(error.message);});await chain;
 });
 for(const [index,url]of targets.entries()){
  const page=await context.newPage();page.on('pageerror',e=>report.errors.push(e.message));
  try{const response=await page.goto(url,{waitUntil:'domcontentloaded',timeout:90000});await page.waitForTimeout(2000);await chain;
   const html=await page.content();await writeFile(resolve(folder,'rendered-'+index+'.bin'),html,{flag:'wx'});await page.screenshot({path:resolve(folder,'category-'+index+'.png'),fullPage:true});
   const observed=await page.evaluate(()=>({links:[...document.querySelectorAll('#KhuVuc_DanhSachTin a[href]')].map(x=>x.href),title:document.title,sourceError:document.body.textContent.includes('Error executing child request')}));
   report.pages.push({url,status:response?.status(),...observed,wholeCategoryTotal:null,result:observed.links.length?'RenderedLinksObserved':'NoRenderedRows_TOTAL_UNKNOWN'});
  }catch(error){report.pages.push({url,result:'INSPECTION_PARTIAL',error:error.message,wholeCategoryTotal:null});}finally{await page.close();}
 }
 await chain;
}catch(error){report.errors.push(error.message);process.exitCode=1;}finally{if(browser)await browser.close();report.bytes=bytes;await writeFile(resolve(folder,'browser-report.json'),JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify({folder,requests:report.requests.length,blocked:report.blocked.length,pages:report.pages.map(x=>({url:x.url,status:x.status,result:x.result})),errors:report.errors}));}
