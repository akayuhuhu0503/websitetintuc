// Browser-only interaction checks using Razor pages captured by the separate
// real HTTP/SQL fixture. Never connects to CMS or a production database.
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {resolve,extname,sep} from 'node:path';
import {createRequire} from 'node:module';
const require=createRequire(resolve(process.argv[3]||'tests/browser/package.json'));
const {chromium}=require('playwright');
const run=resolve(process.argv[2]);const web=resolve('src/Web/wwwroot');
const errors=[],checks=[],requests=[];let lastItems=[];let executed=0;
const check=(ok,name)=>{if(!ok)throw new Error(name);checks.push(name);console.log('PASS '+name);};
const server=createServer(async(req,res)=>{try{
 const u=new URL(req.url,'http://127.0.0.1');
 if(req.method==='POST'){
  let body='';for await(const c of req)body+=c;const data=JSON.parse(body);requests.push({path:u.pathname,data});
  res.setHeader('Content-Type','application/json');
  if(u.pathname==='/admin/bulk/preview'){lastItems=data.items;res.end(JSON.stringify({label:'Gửi duyệt',items:data.items.map((x,i)=>({...x,title:x.title,eligible:i!==0,reason:i===0?'Bài đã đổi phiên bản.':null,url:'/admin/edit/'+x.id}))}));}
  else{executed++;await new Promise(r=>setTimeout(r,75));if(executed===2){res.statusCode=409;res.end(JSON.stringify({message:'Phiên bản đã thay đổi.'}));}else res.end(JSON.stringify({alreadyApplied:false}));}return;
 }
 if(u.pathname==='/admin'){res.setHeader('Content-Type','text/html; charset=utf-8');res.end(await readFile(resolve(run,u.searchParams.get('page')==='2'?'bulk-page2.html':'bulk-page1.html')));return;}
 const p=resolve(web,'.'+decodeURIComponent(u.pathname));if(!p.startsWith(web+sep)){res.writeHead(404).end();return;}
 const mime={'.js':'application/javascript','.css':'text/css','.woff2':'font/woff2'}[extname(p)]||'application/octet-stream';res.setHeader('Content-Type',mime);res.end(await readFile(p));
}catch{res.writeHead(404).end();}});
await new Promise(r=>server.listen(0,'127.0.0.1',r));const base='http://127.0.0.1:'+server.address().port;
const browser=await chromium.launch({channel:'msedge',headless:true});
try{
 const page=await browser.newPage({viewport:{width:1366,height:900}});page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>new URL(route.request().url()).origin===base?route.continue():route.abort());
 await page.goto(base+'/admin');const rows=page.locator('.bulk-row:not(:disabled)');await rows.nth(0).check();await rows.nth(1).check();
 check((await page.locator('#bulk-count').innerText()).includes('2 bài'),'select individual rows');
 await page.getByRole('link',{name:'Tiếp →',exact:true}).click();await page.waitForLoadState('load');
 check((await page.locator('#bulk-count').innerText()).includes('2 bài'),'selection survives pagination with empty query filters');
 await page.locator('#bulk-select-page').check();const n=2+await page.locator('.bulk-row:not(:disabled)').count();
 check((await page.locator('#bulk-count').innerText()).includes(`${n} bài`),'select all affects current page only');
 await page.locator('#bulk-action').selectOption('PublishDirect');await page.locator('#bulk-preview').click();await page.locator('#bulk-dialog[open]').waitFor();
 check(lastItems.length===n && (await page.locator('#bulk-summary').innerText()).includes('Bỏ qua 1'),'confirmation shows eligibility and reasons for all selected pages');
 await page.locator('#bulk-confirm').click();await page.waitForFunction(()=>document.querySelector('#bulk-confirm').hidden);
 check(executed===n-1,'ineligible skipped and independent items continue after one error');
 check((await page.locator('#bulk-summary').innerText()).includes('Lỗi/chưa xác nhận 1'),'result counts and failure detail visible');
 check(new Set(requests.filter(r=>r.path.endsWith('/execute')).map(r=>r.data.requestId)).size===executed,'one independent idempotency key per item');
 check(await page.locator('#bulk-dialog[open]').count()===1 && await page.locator('#bulk-list-fragment').count()===1,'list refreshed without losing result dialog');
 await page.locator('#bulk-close').click();await page.goto(base+'/admin?categoryId=76');
 check((await page.locator('#bulk-count').innerText()).includes('0 bài')&&(await page.locator('#bulk-notice').innerText()).includes('Bộ lọc'),'category change resets scope visibly');
 for(const width of [1366,390]){
  await page.setViewportSize({width,height:900});await page.locator('.bulk-row:not(:disabled)').first().check();await page.locator('#bulk-action').selectOption('PublishDirect');await page.locator('#bulk-preview').click();await page.locator('#bulk-dialog[open]').waitFor();
  check(await page.locator('#bulk-dialog').evaluate(e=>e.scrollWidth<=e.clientWidth+1),`dialog width ${width}`);await page.locator('#bulk-close').click();await page.locator('#bulk-clear').click();
 }
 check(errors.length===0,'no JavaScript errors');console.log(`BULK BROWSER ${checks.length}/${checks.length}`);
}finally{await browser.close();server.close();}
