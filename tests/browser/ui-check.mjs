import { solveLoginCaptcha } from './login-captcha.mjs';
import { chromium } from 'playwright';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';

const workspace = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const values = { ...process.env };
try {
  for (const line of (await readFile(resolve(workspace, '.env'), 'utf8')).split(/\r?\n/)) {
    if (!line.trim() || line.trimStart().startsWith('#')) continue;
    const match = line.match(/^([A-Za-z_][A-Za-z0-9_]*)=(.*)$/);
    if (!match) throw new Error('Invalid project .env line; values omitted.');
    values[match[1]] ??= match[2].trim().replace(/^"|"$/g, '');
  }
} catch (error) { if (error.code !== 'ENOENT') throw error; }
const base = values.Public__BaseUrl ?? 'http://127.0.0.1:5188';
if (!['127.0.0.1', 'localhost', '[::1]'].includes(new URL(base).hostname)) throw new Error('QA only supports the project loopback URL.');
const evidence = resolve(process.env.LandPortal__EvidenceRoot || resolve(workspace, '.local/evidence'), 'ui');
await mkdir(evidence, { recursive: true });
const editorsOnly = process.argv.includes('--editors-only');
const previousReport = editorsOnly ? JSON.parse(await readFile(resolve(evidence,'ui-report.json'),'utf8')) : null;
const redact = text => [values.Bootstrap__AdminPassword, values.Runtime__RolePassword, values.Smtp__Password].filter(Boolean).reduce((result, secret) => result.split(secret).join('[REDACTED]'), text);
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const publicContext = await browser.newContext();
const adminContext = await browser.newContext();
const report = { schemaVersion: 1, utc: new Date().toISOString(), baseUrl: base, browser: await browser.version(), pageChecks: [], externalRequestsBlocked: [], consoleErrors: [], limitations: ['Viewport emulation in desktop Edge; not physical iOS/Safari.', 'Keyboard/reflow checks and visual review are not a WCAG certificate.'] };
for (const context of [publicContext, adminContext]) {
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.origin !== new URL(base).origin) { report.externalRequestsBlocked.push(url.origin); await route.abort(); }
    else await route.continue();
  });
}
const publicPage = await publicContext.newPage();
const adminPage = await adminContext.newPage();
for (const page of [publicPage, adminPage]) page.on('pageerror', error => report.consoleErrors.push(redact(error.message)));
let failures = 0;
try {
  await publicPage.goto(base, { waitUntil: 'networkidle' });
  const article = await publicPage.locator('a[href^="/bai-viet/"]').first().getAttribute('href');
  if (!article) throw new Error('No imported article exists on the public home page; UI evidence cannot claim real content.');
  if (!values.Bootstrap__AdminEmail || !values.Bootstrap__AdminPassword) throw new Error('Project bootstrap credentials are missing; no default password used.');
  await adminPage.goto(base + '/admin/login');
  await adminPage.locator('[name=UserName]').fill(values.Bootstrap__AdminEmail);
  await adminPage.locator('[name=Password]').fill(values.Bootstrap__AdminPassword); await solveLoginCaptcha(adminPage);
  await Promise.all([adminPage.waitForURL('**/admin'), adminPage.getByRole('button', { name: 'Đăng nhập', exact: true }).click()]);
  const editor = await adminPage.locator('a[href^="/admin/edit/"]').first().getAttribute('href');
  if (!editor) throw new Error('Admin contains no imported article to inspect.');
  const routes = [
    ['home', '/', publicPage],
    ['category', '/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat', publicPage],
    ['article', article, publicPage],
    ['documents', '/van-ban', publicPage],
    ['admin', '/admin', adminPage],
    ['editor', editor, adminPage]
  ].filter(([name]) => !editorsOnly || name==='editor');
  const manifest = JSON.parse(await readFile(resolve(workspace,'.local/snapshots/sample-manifest.json'),'utf8'));
  const pdf = manifest.Assets.find(asset => asset.MimeType === 'application/pdf');
  if (pdf) {
    const response = await publicContext.request.get(base + '/media/' + pdf.Id + '?download=true');
    const body = await response.body();
    report.pdf = { status: response.status(), contentType: response.headers()['content-type'], bytes: body.length, pass: response.ok() && body.subarray(0,5).toString() === '%PDF-' && body.length === pdf.Bytes };
    if (!report.pdf.pass) failures++;
  } else { report.pdf = {pass:false,reason:'No real local PDF in manifest'}; failures++; }
  for (const width of [360, 390, 768, 1024, 1440]) {
    for (const [name, path, page] of routes) {
      await page.setViewportSize({ width, height: 900 });
      const response = await page.goto(base + path, { waitUntil: 'networkidle' });
      await page.evaluate(async () => {
        for (const img of document.images) {
          if (!img.getClientRects().length) continue;
          img.scrollIntoView({block:'center'});
          await new Promise(resolve=>setTimeout(resolve,150));
          await Promise.race([img.decode().catch(()=>{}), new Promise(resolve=>setTimeout(resolve,5000))]);
        }
        scrollTo(0,0);
      });
      await page.waitForLoadState('networkidle');
      const layout = await page.evaluate(() => ({
        viewport: innerWidth, scrollWidth: document.documentElement.scrollWidth,
        heading: document.querySelector('h1')?.textContent.trim(),
        missingImages: [...document.images].filter(img => img.getClientRects().length && (!img.complete || img.naturalWidth === 0)).map(img => img.getAttribute('src')),
        unlabeled: [...document.querySelectorAll('input:not([type=hidden]),textarea,select')].filter(input => input.getClientRects().length && !input.labels?.length && !input.getAttribute('aria-label') && !input.getAttribute('aria-labelledby')).map(input => input.name || input.id),
        unexpectedValidationVisible: [...document.querySelectorAll('.validation-summary,[data-valmsg-summary],[role=alert]')].filter(element=>element.getClientRects().length && !element.textContent.trim()).length,
        editorFormPresent: Boolean(document.querySelector('#editor-form [name=Title]')),
        overflowing: [...document.querySelectorAll('body *')].filter(el => { const rect=el.getBoundingClientRect(); const style=getComputedStyle(el); return style.position!=='absolute' && style.position!=='fixed' && rect.width > 0 && rect.right > innerWidth+2; }).slice(0, 8).map(el => ({tag:el.tagName, class:el.className}))
      }));
      const status = response?.status() ?? 0;
      const pass = status === 200 && layout.scrollWidth <= width + 1 && layout.missingImages.length === 0 && layout.unlabeled.length === 0 && layout.unexpectedValidationVisible === 0 && (name!=='editor' || layout.editorFormPresent);
      if (!pass) failures++;
      report.pageChecks.push({ name, width, path, status, pass, ...layout });
      await page.evaluate(() => { document.activeElement?.blur?.(); scrollTo({top:0,left:0,behavior:'instant'}); });
      await page.screenshot({ path: resolve(evidence, `${name}-${width}.png`), fullPage: true });
    }
    console.log(`UI viewport ${width}: checked ${editorsOnly ? 'affected editor page' : 'home/category/article/documents/admin/editor'}.`);
  }
  await adminPage.goto(base + editor,{waitUntil:'networkidle'});
  const invalidFields = await adminPage.locator('#editor-form').evaluate(form=>Object.fromEntries(new FormData(form).entries()));
  invalidFields.Title=''; invalidFields.BodyHtml=''; invalidFields.intent='Save';
  const invalidResponse = await adminContext.request.post(base+'/admin/save',{form:invalidFields});
  const invalidHtml=await invalidResponse.text();
  const invalidSummary=await adminPage.evaluate(html=>new DOMParser().parseFromString(html,'text/html').querySelector('.validation-summary')?.textContent.trim() ?? '',invalidHtml);
  report.editorValidation={status:invalidResponse.status(),summaryHasVisibleMessage:Boolean(invalidSummary),titleRequiredMessage:invalidSummary.includes('Nhập tiêu đề bài viết.'),bodyRequiredMessage:invalidSummary.includes('Nhập nội dung bài viết.'),pass:invalidResponse.status()===200 && invalidSummary.includes('Nhập tiêu đề bài viết.') && invalidSummary.includes('Nhập nội dung bài viết.')};
  await adminPage.setViewportSize({width:1440,height:900});
  await adminPage.setContent(invalidHtml,{waitUntil:'networkidle'});
  report.editorValidation.visibleErrorSummary=await adminPage.locator('.validation-summary').isVisible();
  report.editorValidation.pass &&= report.editorValidation.visibleErrorSummary;
  await adminPage.evaluate(()=>{document.activeElement?.blur?.();scrollTo({top:0,left:0,behavior:'instant'});});
  await adminPage.screenshot({path:resolve(evidence,'editor-validation-errors-1440.png'),fullPage:true});
  if(!report.editorValidation.pass) failures++;
  if(editorsOnly){
    report.pageChecks=[...previousReport.pageChecks.filter(check=>check.name!=='editor'),...report.pageChecks];
    report.affectedRerun={previousUtc:previousReport.utc,retestedPage:'editor',viewportCount:5,reason:'Empty server validation summary corrected; POST required messages preserved.'};
  }
  await publicPage.setViewportSize({ width: 390, height: 844 });
  await publicPage.goto(base + '/van-ban', { waitUntil: 'networkidle' });
  const scroll = publicPage.locator('.table-scroll').first();
  if (await scroll.count()) {
    report.documentTable = await scroll.evaluate(element => {
      element.scrollLeft = element.scrollWidth - element.clientWidth;
      const links = [...element.querySelectorAll('a')].map(link => ({text:link.textContent.trim(),href:link.getAttribute('href'),rect:link.getBoundingClientRect()}));
      const downloadVisible=links.some(link=>link.href?.startsWith('/media/') && link.rect.right<=innerWidth && link.rect.left>=0);
      return {scrollWidth:element.scrollWidth,clientWidth:element.clientWidth,scrollLeft:element.scrollLeft,downloadVisible,rightmostLinks:links.filter(link=>link.rect.right<=innerWidth && link.rect.left>=0).map(link=>({text:link.text,href:link.href})),pass:element.scrollLeft>0 && downloadVisible};
    });
    if (!report.documentTable.pass) failures++;
    const downloadLink = scroll.locator('a[href^="/media/"]').last();
    if (await downloadLink.count()) {
      await downloadLink.focus();
      const focused = await downloadLink.evaluate(element => document.activeElement===element && element.getBoundingClientRect().right<=innerWidth);
      report.documentTable.downloadFocusReachable = focused;
      if (!focused) failures++;
    }
    await publicPage.evaluate(() => {document.activeElement?.blur?.();scrollTo({top:0,left:0,behavior:'instant'});});
    await publicPage.screenshot({path:resolve(evidence,'documents-390-scroll-right.png'),fullPage:true});
  } else {report.documentTable={pass:false,reason:'Scrollable document table missing'};failures++;}
  await publicPage.goto(base, { waitUntil: 'networkidle' });
  const navToggle = publicPage.locator('.mobile-menu-toggle');
  if (await navToggle.count()) await navToggle.click();
  const menu = publicPage.locator('details').filter({ has: publicPage.locator('summary').filter({hasText:'Tin tức'}) }).first();
  if (await menu.count()) {
    await menu.locator('summary').click();
    const categories = await menu.locator('a[href^="/chuyen-muc/"]').count();
    const opened = await menu.getAttribute('open');
    report.menu = { opened: opened !== null, categoryLinks: categories, pass: opened !== null && categories >= 14 };
    if (!report.menu.pass) failures++;
    await publicPage.screenshot({ path: resolve(evidence, 'menu-390.png'), fullPage: true });
  } else { report.menu = { pass: false, reason: 'Clickable news menu not found' }; failures++; }
  await publicPage.goto(base); await publicPage.keyboard.press('Tab');
  report.keyboard = await publicPage.evaluate(() => ({ firstFocusedTag: document.activeElement?.tagName, firstFocusedText: document.activeElement?.textContent.trim(), href: document.activeElement?.getAttribute('href') }));
  report.keyboard.pass = report.keyboard.href?.startsWith('#') === true;
  if (!report.keyboard.pass || report.consoleErrors.length || report.externalRequestsBlocked.length) failures++;
} catch (error) { failures++; report.error = redact(error.message); }
finally {
  report.failures = failures;
  await writeFile(resolve(evidence, 'ui-report.json'), JSON.stringify(report, null, 2));
  await browser.close();
}
console.log(`UI RESULT ${report.pageChecks.filter(x => x.pass).length}/${report.pageChecks.length} page checks; failures=${failures}; private evidence=.local/evidence/ui/ui-report.json`);
process.exitCode = failures ? 1 : 0;
