import { solveLoginCaptcha, solveReauthCaptcha } from './login-captcha.mjs';
import { chromium } from 'playwright';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const fixture = JSON.parse(await readFile(resolve(root, '.local/tests/phase2a/ui-credentials.json'), 'utf8'));
if (fixture.database !== 'LandPortal_Test' || !['127.0.0.1', 'localhost'].includes(new URL(fixture.baseUrl).hostname)) throw new Error('UI profile must be local SQL Test.');
const evidence = resolve(process.env.LandPortal__EvidenceRoot || resolve(root, '.local/evidence'), 'phase2a/ui'); await mkdir(evidence, { recursive: true });
const report = { schemaVersion: 1, utc: new Date().toISOString(), database: fixture.database, pageChecks: [], actions: [], blockedRequests: [], pageErrors: [], limitations: ['Desktop Edge viewport emulation, not physical iOS/Safari.', 'Focus/reflow checks and screenshots do not certify WCAG.'] };
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const writerContext = await browser.newContext(); const reviewContext = await browser.newContext(); const guestContext = await browser.newContext();
for (const context of [writerContext, reviewContext, guestContext]) await context.route('**/*', async route => { const url = new URL(route.request().url()); if (url.origin !== new URL(fixture.baseUrl).origin) { report.blockedRequests.push(url.origin); await route.abort(); } else await route.continue(); });
const writer = await writerContext.newPage(); const reviewer = await reviewContext.newPage(); const guest = await guestContext.newPage();
for (const page of [writer, reviewer, guest]) page.on('pageerror', error => report.pageErrors.push(error.message));
let failures = 0;
function assert(value, message) { if (!value) throw new Error(message); }
async function login(page, name) { await page.goto(fixture.baseUrl + '/admin/login'); await page.locator('[name=UserName]').fill(name); await page.locator('[name=Password]').fill(fixture.password); await solveLoginCaptcha(page); await Promise.all([page.waitForURL('**/admin'), page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()]); }
async function clickSave(page, selector) { const beforeRevision=await page.locator('[name=CurrentRevisionId]').inputValue();const response = page.waitForResponse(r => r.url().includes('/admin/save') && r.request().method() === 'POST' && /name="autosave"\r?\n\r?\nfalse/.test(r.request().postData()||'')); await page.locator(selector).click(); const saved = await response; assert(saved.ok(), 'UI save/submit failed HTTP ' + saved.status());await page.waitForFunction(({selector,beforeRevision})=>{const form=document.querySelector('#editor-form'),id=document.querySelector('[name=Id]')?.value,revision=document.querySelector('[name=CurrentRevisionId]')?.value;return Boolean(id&&revision&&(selector.includes('submit')?form?.dataset.state==='PendingReview':revision!==beforeRevision));},{selector,beforeRevision});await page.waitForLoadState('networkidle');return {contentId:await page.locator('[name=Id]').inputValue()}; }
async function workflow(page, action) { const response = page.waitForResponse(r => r.url().includes('/admin/workflow/') && r.request().method() === 'POST'); const navigation=page.waitForEvent('framenavigated',{predicate:frame=>frame===page.mainFrame()});await page.locator(`[data-workflow="${action}"]`).click(); const saved = await response; assert(saved.ok(), 'UI workflow ' + action + ' failed HTTP ' + saved.status());await navigation;await page.waitForLoadState('networkidle'); }
async function screenshotCheck(page, name, width, path) {
  await page.setViewportSize({ width, height: 900 }); const response = await page.goto(fixture.baseUrl + path, { waitUntil: 'networkidle' });
  await page.evaluate(async () => { for (const img of document.images) { if (!img.getClientRects().length) continue; img.scrollIntoView({ block: 'center' }); await Promise.race([img.decode().catch(() => {}), new Promise(resolve => setTimeout(resolve, 5000))]); } scrollTo(0,0); });
  const layout = await page.evaluate(() => ({ viewport: innerWidth, scrollWidth: document.documentElement.scrollWidth, heading: document.querySelector('h1')?.textContent.trim(), missingImages: [...document.images].filter(x => x.getClientRects().length && (!x.complete || !x.naturalWidth)).map(x => x.getAttribute('src')), unlabelled: [...document.querySelectorAll('input:not([type=hidden]),textarea,select')].filter(x => x.getClientRects().length && !x.labels?.length && !x.getAttribute('aria-label') && !x.getAttribute('aria-labelledby')).map(x => x.name || x.id) }));
  const pass = response?.status() === 200 && layout.scrollWidth <= width + 1 && !layout.missingImages.length && !layout.unlabelled.length;
  report.pageChecks.push({ name, width, path, status: response?.status(), pass, ...layout }); if (!pass) failures++;
  await page.screenshot({ path: resolve(evidence, `${name}-${width}.png`), fullPage: true });
}
try {
  await login(writer, fixture.writer); await login(reviewer, fixture.reviewer);
  await writer.goto(fixture.baseUrl + '/admin/create', { waitUntil: 'networkidle' });
  const title = 'QA E2E tiếng Việt rất dài — Kiểm tra quy trình soạn thảo, gửi duyệt, xem trước và xuất bản đúng phiên bản ' + Date.now();
  await writer.locator('[name=Title]').fill(title); await writer.locator('[name=Summary]').fill('Mô tả được nhập từ giao diện và lưu vào SQL Server test.');
  const category = await writer.locator('[name=CategoryId] option').evaluateAll(options => options.find(x => x.value)?.value); assert(category, 'No category option.'); await writer.locator('[name=CategoryId]').selectOption(category);
  const body = writer.locator('.jodit-wysiwyg[contenteditable=true]').first(); await body.click(); await writer.keyboard.type('Nội dung nhập bằng bàn phím để kiểm chứng UI gọi backend SQL thật.');
  await body.evaluate(element=>{const clipboard=new DataTransfer();clipboard.setData('text/html','<p class="MsoNormal" style="mso-fareast-font-family:Calibri" onclick="alert(1)">Đoạn dán từ Word phải được làm sạch.</p>');clipboard.setData('text/plain','Đoạn dán từ Word phải được làm sạch.');element.dispatchEvent(new ClipboardEvent('paste',{bubbles:true,cancelable:true,clipboardData:clipboard}));});
  await writer.getByText('Đoạn dán từ Word phải được làm sạch.',{exact:false}).first().waitFor({state:'visible'});
  await writer.locator('.jodit-toolbar-button_table button').first().click();
  await writer.getByRole('gridcell', { name: '2 by 2', exact: true }).click();
  await body.locator('td').first().click();await writer.keyboard.type('Bảng số liệu đất đai');await body.locator('td').nth(1).click();await writer.keyboard.type('Diện tích 1.234,56 m²');
  const saved = await clickSave(writer, '[data-save]'); assert(saved.contentId, 'UI save returned no content ID.'); const id = saved.contentId;
  await writer.goto(fixture.baseUrl + '/admin/edit/' + id, { waitUntil: 'networkidle' });
  async function chooseMedia(mode,id,name,caption='') { await writer.locator(`[data-open-media=${mode}]`).click();await writer.locator('#media-search').fill(name);const loaded=writer.waitForResponse(r=>r.url().includes('/admin/media?')&&r.request().method()==='GET');await writer.locator('[data-search-media]').click();await loaded;if(caption)await writer.locator('#media-caption').fill(caption);const item=writer.locator('#media-items .media-item').filter({has:writer.locator(`img[src*="${id}"]`)});if(mode==='attachment')await writer.locator('#media-items .media-item').filter({hasText:name}).first().click();else await item.first().click(); }
  await chooseMedia('cover',fixture.image,fixture.imageName);
  await chooseMedia('inline',fixture.image,fixture.imageName,'Chú thích ảnh nguồn thật trong bài kiểm thử');
  await chooseMedia('attachment',fixture.pdf,fixture.pdfName);
  // Picker selection persists through actual save; source shared image is not re-downloaded.
  await clickSave(writer, '[data-save]');
  report.actions.push({ name: 'Create article with long Vietnamese title, keyboard rich text, actual table toolbar/grid, inline caption and image/PDF picker then persist to SQL Test', status: 'PASS', contentId: id });
  const privateArticle = await guestContext.request.get(fixture.baseUrl + '/bai-viet/' + id); assert(privateArticle.status() === 404, 'UI draft leaked publicly.');
  const previewPath = '/admin/preview/' + id;
  for (const width of [390, 1440]) {
    await screenshotCheck(writer, 'editor', width, '/admin/edit/' + id);
    await screenshotCheck(writer, 'admin-list', width, '/admin?q=' + encodeURIComponent(title));
    await screenshotCheck(writer, 'preview', width, previewPath);
  }
  await writer.goto(fixture.baseUrl + '/admin/edit/' + id, { waitUntil: 'networkidle' });
  const retainedSummary='QA V04 retained edit '+Date.now();await writer.locator('[name=Summary]').fill(retainedSummary);await writer.context().clearCookies();
  const expiredSave=writer.waitForResponse(r=>r.url().includes('/admin/save')&&r.request().method()==='POST');await writer.locator('[data-save]').click();assert((await expiredSave).status()===401,'Expired editor save was not401');
  await writer.locator('#reauth-dialog').waitFor({state:'visible'});assert(await writer.locator('[name=Summary]').inputValue()===retainedSummary,'Expiry lost working prose');
  await writer.locator('#reauth-user').fill(fixture.writer);await writer.locator('#reauth-password').fill(fixture.password);await solveReauthCaptcha(writer);
  const reauthenticated=writer.waitForResponse(r=>r.url().endsWith('/admin/reauthenticate')&&r.request().method()==='POST');await writer.locator('#reauth-dialog form button[type=submit]').click();assert((await reauthenticated).status()===200,'Actual reauthentication failed');await writer.locator('#reauth-dialog').waitFor({state:'hidden'});
  assert(await writer.locator('[name=Summary]').inputValue()===retainedSummary,'Reauthentication lost working prose');await clickSave(writer,'[data-save]');await writer.reload();assert(await writer.locator('[name=Summary]').inputValue()===retainedSummary,'Retained prose was not saved to SQL after reauth');
  report.actions.push({name:'V04 expired cookie401 preserves unsaved prose; actual same-account reauth/newCSRF/manual save persists SQL Test',status:'PASS'});
  await clickSave(writer, '[data-save-submit]');
  for (const width of [390, 1440]) { await screenshotCheck(reviewer, 'review-list', width, '/admin?status=PendingReview');await screenshotCheck(reviewer,'review-editor',width,'/admin/edit/'+id); }
  await reviewer.goto(fixture.baseUrl + '/admin/edit/' + id, { waitUntil: 'networkidle' }); await workflow(reviewer, 'Approve'); await workflow(reviewer, 'PublishApproved');
  await guest.goto(fixture.baseUrl + '/bai-viet/' + id, { waitUntil: 'networkidle' }); assert((await guest.locator('h1').textContent()).trim() === title, 'UI published a different title.'); assert(await guest.locator(`img[src^="/media/${fixture.image}"]`).count(), 'UI cover picker did not publish selected image.');
  assert(await guest.locator('.article-body table').count()&& (await guest.locator('.article-body').textContent()).includes('Bảng số liệu đất đai') && (await guest.locator('.article-body figcaption').textContent()).includes('Chú thích ảnh nguồn thật'), 'Actual toolbar table and caption were lost after UI publication.');
  assert((await guest.locator('.article-body').textContent()).includes('Đoạn dán từ Word phải được làm sạch.')&&!await guest.locator('.article-body [onclick]').count(),'Word paste content lost or active handler survived publishing.');
  report.actions.push({ name: 'Writer UI submit then separate reviewer UI approves and publishes exact revision seen by anonymous visitor', status: 'PASS' });
  await guest.goto(fixture.baseUrl);const realArticle=await guest.locator('a[href^="/bai-viet/"]').first().getAttribute('href');assert(realArticle,'No original imported source content on home.');
  for (const width of [360,390,768,1024,1440]) { await screenshotCheck(guest,'public-home',width,'/');await screenshotCheck(guest,'public-category',width,'/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat');await screenshotCheck(guest,'public-article',width,realArticle);await screenshotCheck(guest,'public-documents',width,'/van-ban');await screenshotCheck(writer,'regression-admin',width,'/admin');await screenshotCheck(writer,'regression-editor',width,'/admin/edit/'+id);console.log(`CMS UI regression viewport ${width}: six real routes checked.`); }
  await guest.setViewportSize({width:390,height:900});await guest.goto(fixture.baseUrl + '/van-ban',{waitUntil:'networkidle'});const pdf=await guestContext.request.get(fixture.baseUrl+'/media/'+fixture.pdf+'?download=true'); const bytes=await pdf.body();assert(pdf.status()===200&&bytes.subarray(0,5).toString()==='%PDF-','Source-blocked local PDF failed.');
  await writer.goto(fixture.baseUrl+'/admin/edit/'+id); await writer.keyboard.press('Tab'); const focus=await writer.evaluate(()=>({tag:document.activeElement?.tagName,text:document.activeElement?.textContent.trim(),outline:getComputedStyle(document.activeElement).outlineStyle}));report.keyboard={...focus,pass:focus.tag!=='BODY'};assert(report.keyboard.pass,'Admin keyboard focus unreachable.');
  report.sourceIndependence={profile:'Abort all off-origin browser requests only; no machine firewall changes',localPdfStatus:pdf.status(),localPdfBytes:bytes.length,externalAttempts:report.blockedRequests.length,pass:report.blockedRequests.length===0};
  assert(!report.pageErrors.length,'Browser JS errors.');assert(!report.blockedRequests.length,'Unexpected source/external resource attempts.');
} catch(error){ failures++; report.error=String(error.message).split(fixture.password).join('[REDACTED]');report.dialogText=await writer.locator('#media-dialog').textContent().catch(()=>null);await writer.screenshot({path:resolve(evidence,'error-390-or-desktop.png'),fullPage:true}).catch(()=>{}); }
finally {report.failures=failures;await writeFile(resolve(evidence,'ui-report.json'),JSON.stringify(report,null,2));await browser.close();}
console.log(`CMS UI actual checks=${report.pageChecks.length}; failures=${failures}; report=.local/evidence/phase2a/ui/ui-report.json`); if(report.error)console.log(report.error);process.exitCode=failures?1:0;
