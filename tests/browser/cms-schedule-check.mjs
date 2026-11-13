// Real Razor from the isolated SQL/HTTP fixture + real editor.js/Jodit.
// Mock only the final HTTP actions to check which explicit mode the UI sends.
import {readFile} from 'node:fs/promises';
import {resolve,dirname,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
const {chromium}=await import(process.env.CMS_PLAYWRIGHT_MODULE||'playwright');
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const assets=resolve(root,'src/Web/wwwroot');
const rendered=process.argv[2];
if(!rendered)throw new Error('Provide the isolated ScheduleOnly/Followup evidence directory');
const pages={};
for(const state of ['pending','approved']){
 pages[state]=(await readFile(resolve(rendered,`schedule-${state}.html`),'utf8')).replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'').replace(/<link\b[^>]*>/gi,'')+'<script>window.landPortalStateLabel=x=>x;</script><script src="/vendor/jodit/jodit.min.js"></script><script src="/js/editor.js"></script>';
}
function values(request){
 const result={};const body=request.postDataBuffer()?.toString('utf8')||'';
 for(const match of body.matchAll(/Content-Disposition: form-data; name="([^"]+)"(?:;[^\r]*)?\r\n(?:[^\r]+\r\n)*\r\n([\s\S]*?)\r\n--/g))result[match[1]]=match[2];
 return result;
}
const browser=await chromium.launch({channel:'msedge',headless:true});
let checks=0;
try{
 for(const [state,mode] of [['pending','approved'],['approved','approved'],['pending','direct'],['approved','direct']]){
  const context=await browser.newContext();const requests=[],errors=[];let current;
  await context.route('**/*',async route=>{
   const request=route.request(),url=new URL(request.url());
   if(url.hostname!=='fixture.test')throw new Error('Unexpected external request');
   const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
   if(url.pathname.startsWith('/admin/editor-readiness/'))return json({errors:{},canSubmit:true,canPublish:true,fileNotice:'Fixture'});
   if(url.pathname==='/admin/session')return json({expiresUtc:new Date(Date.now()+1800000).toISOString()});
   if(url.pathname.startsWith('/admin/workflow/')){const form=values(request);requests.push({route:'approve',form});return json({...current,rowVersion:'MDAwMDAwMDI=',state:'Approved',savedUtc:new Date().toISOString()});}
   if(url.pathname.startsWith('/admin/editor-schedule/')){requests.push({route:'schedule',form:values(request)});return json({jobId:'fixture',message:'Lịch fixture đã lưu.'});}
   if(url.pathname.startsWith('/js/')||url.pathname.startsWith('/vendor/')){
    const path=resolve(assets,'.'+url.pathname);if(!path.startsWith(assets+'\\')&&!path.startsWith(assets+'/'))throw new Error('Unexpected asset path');
    return route.fulfill({body:await readFile(path),contentType:extname(path)==='.js'?'text/javascript; charset=utf-8':'text/css'});
   }
   if(url.pathname.startsWith('/admin/edit/'))return route.fulfill({body:pages[state],contentType:'text/html; charset=utf-8'});
   throw new Error('Unexpected fixture endpoint '+url.pathname);
  });
  const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://fixture.test/admin/edit/fixture');await page.locator('.jodit-wysiwyg').waitFor();
  current=await page.evaluate(()=>{const f=document.querySelector('#editor-form');return{contentId:f.elements.namedItem('Id').value,currentRevisionId:f.elements.namedItem('CurrentRevisionId').value,rowVersion:f.elements.namedItem('RowVersion').value,isPublic:false,isTrashed:false};});
  await page.locator('.editor-inline-schedule > summary').click();
  await page.locator('#editor-due').fill('2026-12-01T10:00');
  await page.locator(`[data-editor-schedule="${mode}"]`).click();
  await page.waitForFunction(()=>document.querySelector('#editor-feedback').textContent.includes('Lịch fixture đã lưu.'));
  const schedule=requests.filter(r=>r.route==='schedule');assert.equal(schedule.length,1);checks++;
  assert.equal(schedule[0].form.EditorMode,mode);assert.equal(schedule[0].form.Direct,mode==='direct'?'true':'false');checks++;
  assert.equal(requests.filter(r=>r.route==='approve').length,state==='pending'&&mode==='approved'?1:0);checks++;
  assert.equal(schedule[0].form.ContentVersion,state==='pending'&&mode==='approved'?'MDAwMDAwMDI=':current.rowVersion);assert.equal(schedule[0].form.RevisionId,current.currentRevisionId);checks++;
  assert.deepEqual(errors,[]);checks++;
  await context.close();
 }
 console.log(JSON.stringify({status:'PASS',checks,scope:'Four explicit approved/direct scheduling choices with real Razor and editor JS; no live SQL mutations from browser'}));
}finally{await browser.close();}
