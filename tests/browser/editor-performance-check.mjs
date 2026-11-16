// Real editor/Jodit in Chromium. HTTP endpoints are controlled fixtures, NOT SQL benchmarks.
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {resolve,dirname,extname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const {chromium}=await import(process.env.CMS_PLAYWRIGHT_MODULE||'playwright');
const oldHarness=await readFile(resolve(root,'tests/browser/cms-editor-check.mjs'),'utf8');
// Reuse the existing presentation fixture markup, without running its unrelated tests.
const fixtureSource=oldHarness.slice(oldHarness.indexOf('const fields ='),oldHarness.indexOf('const rendered ='));
const fixture=Function(fixtureSource+'; return fixture;')();
const id='11111111-1111-1111-1111-111111111111',assetId='33333333-3333-3333-3333-333333333333';
const out=resolve(root,'.local/tests/editor-performance');await mkdir(out,{recursive:true});
const phase=process.argv[2]||'after',metrics=[],errors=[];
let checks=0;
const browser=await chromium.launch({channel:'msedge',headless:true});
async function scenario({existing=true,lost=false,missing=false,denied=false,editDuring=false,replayBefore=false,replayDuring=false}={}){
 const context=await browser.newContext();let saves=0,uploads=0,lookups=0,readiness=0;const savedBodies=[];
 await context.addInitScript(()=>{window.uiMetrics={started:0,feedback:0,longTasks:[],shifts:0};new PerformanceObserver(list=>list.getEntries().forEach(e=>window.uiMetrics.longTasks.push(e.duration))).observe({type:'longtask',buffered:true});new PerformanceObserver(list=>list.getEntries().forEach(e=>{if(!e.hadRecentInput)window.uiMetrics.shifts+=e.value;})).observe({type:'layout-shift',buffered:true});});
 await context.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url());assert.equal(url.hostname,'localhost');
  const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  const asset={id:assetId,originalName:'fixture.pdf',mimeType:'application/pdf',byteLength:80,previewUrl:'/admin/documents/file/'+assetId,url:'/media/'+assetId,scanStatus:'NotScanned',usable:true};
  if(url.pathname==='/admin/save'){saves++;savedBodies.push(req.postData());await new Promise(r=>setTimeout(r,250));if(saves===1&&(replayBefore||replayDuring))return route.abort('connectionreset');if(denied)return json({message:'Quyền lưu đã bị thu hồi.'},403);return json({contentId:id,currentRevisionId:id,rowVersion:btoa(String(saves)),state:'Draft',savedUtc:new Date().toISOString()});}
  if(url.pathname.endsWith('/result')){lookups++;return json(asset,missing?404:200);}
  if(url.pathname.startsWith('/admin/editor-readiness/')){readiness++;return json({errors:{},canSubmit:true,canPublish:true,fileNotice:''});}
  if(url.pathname.startsWith('/admin/documents/')&&req.method()==='POST'){uploads++;await new Promise(r=>setTimeout(r,editDuring?600:200));if(lost)return route.abort('connectionreset');return json(asset);}
  if(url.pathname==='/js/editor.js'&&phase==='before'&&process.env.EDITOR_PERF_BASELINE)return route.fulfill({body:await readFile(process.env.EDITOR_PERF_BASELINE),contentType:'text/javascript; charset=utf-8'});
  if(url.pathname.startsWith('/js/')||url.pathname.startsWith('/vendor/'))return route.fulfill({body:await readFile(resolve(root,'src/Web/wwwroot',url.pathname.slice(1))),contentType:extname(url.pathname)==='.js'?'text/javascript; charset=utf-8':'text/css; charset=utf-8'});
  if(url.pathname.startsWith('/admin/create')){
   let html=fixture(false);if(existing)html=html.replace('name="Id" type="hidden"','name="Id" type="hidden" value="'+id+'"');
   return route.fulfill({contentType:'text/html; charset=utf-8',body:html+'<script>window.landPortalStateLabel=x=>x;</script><script src="/vendor/jodit/jodit.min.js"></script><script src="/js/editor.js"></script>'});
  }
  throw Error('Unexpected fixture request '+url.pathname);
 });
 const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));await page.goto('http://localhost/admin/create');await page.locator('.jodit-wysiwyg').waitFor();
 await page.locator('[name=Title]').fill('Nội dung đang soạn cần giữ');
 if(replayBefore){await page.locator('[data-save]').click();await page.waitForFunction(()=>document.querySelector('#editor-feedback').textContent.startsWith('Mất kết nối khi lưu'));}
 await page.evaluate(()=>{window.uiMetrics.started=performance.now();new MutationObserver(()=>{if(!window.uiMetrics.feedback&&document.querySelector('#document-feedback').textContent)window.uiMetrics.feedback=performance.now()-window.uiMetrics.started;}).observe(document.querySelector('#document-feedback'),{childList:true,subtree:true,characterData:true});});
 await page.locator('#editor-document-file').setInputFiles({name:'fixture.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.4 presentation-only endpoint fixture not file validation %%EOF')});
 if(editDuring){await page.waitForFunction(()=>document.querySelector('#document-feedback').dataset.phase==='uploading'||document.querySelector('#document-feedback').dataset.phase==='checking');await page.locator('[name=Title]').fill('Nội dung đang soạn cần giữ');await page.locator('#editor-document-file').dispatchEvent('change');assert.equal(await page.locator('#editor-form').evaluate(x=>x.inert),false);checks++;if(replayDuring)await page.locator('[data-save]').click();}
 if(missing||denied||replayDuring)await page.waitForFunction(()=>document.querySelector('#document-feedback').dataset.phase==='unconfirmed');
 else if(!lost||phase==='after')await page.waitForFunction(()=>document.querySelector('#document-feedback').textContent.startsWith('Đã đính kèm'),null,{timeout:8000}).catch(async e=>{console.log({saves,uploads,errors,feedback:await page.locator('#editor-feedback').textContent(),documentFeedback:await page.locator('#document-feedback').textContent()});throw e;});
 else await page.waitForFunction(()=>document.querySelector('#document-feedback').textContent.startsWith('Mất kết nối'));
 const m=await page.evaluate(()=>({...window.uiMetrics,total:performance.now()-window.uiMetrics.started,feedbackText:document.querySelector('#document-feedback').textContent,formInert:document.querySelector('#editor-form').inert,title:document.querySelector('[name=Title]').value}));
 metrics.push({existing,lost,missing,denied,editDuring,replayBefore,replayDuring,...m,saves,uploads,lookups,readiness,scope:'Fixture save delay250ms/upload delay200ms; client comparison only'});
 assert.equal(uploads,1);assert.equal(m.formInert,false);assert.equal(m.title,'Nội dung đang soạn cần giữ');checks+=3;
 if(phase==='after'){assert(m.feedback<100);checks++;if(!missing&&!denied&&!replayDuring){assert(savedBodies.at(-1).includes(assetId));assert.equal(saves,replayBefore?3:existing?1:2);checks+=2;}}
 if(missing){await page.locator('#editor-document-file').dispatchEvent('change');await page.waitForTimeout(200);assert.equal(uploads,1,'uncertain result must never resend file');assert.equal(await page.locator('input[name=AttachmentIds]').count(),0);checks+=2;}
 if(denied||replayDuring){assert(!m.feedbackText.startsWith('Đã đính kèm'));assert.equal(await page.locator('input[name=AttachmentIds]').count(),1);checks+=2;}
 if(replayDuring){assert.equal(savedBodies.length,2);assert(!savedBodies[1].includes(assetId));const normalize=s=>s.replace(/------WebKitFormBoundary[^\r\n]+/g,'BOUNDARY');assert.equal(normalize(savedBodies[0]),normalize(savedBodies[1]),'uncertain save reuses exact command fields');checks+=3;}
 if(!lost&&!denied&&!replayDuring){
  const before=saves;await page.locator('[name=Title]').dispatchEvent('input');await page.waitForTimeout(1900);
  metrics.at(-1).unchangedAutosaveRequests=saves-before;
  if(phase==='after'){assert.equal(saves,before,'unchanged input must not save again');checks++;}
 }
 await context.close();
}
try{for(let n=0;n<3;n++)await scenario();await scenario({existing:false});await scenario({lost:true});if(phase==='after'){await scenario({lost:true,missing:true});await scenario({denied:true});await scenario({editDuring:true});await scenario({replayBefore:true});await scenario({editDuring:true,replayDuring:true});}assert.deepEqual(errors,[]);checks++;await writeFile(resolve(out,phase+'.json'),JSON.stringify({phase,checks,metrics},null,2));console.log(JSON.stringify({phase,checks,metrics}));}finally{await browser.close();}
