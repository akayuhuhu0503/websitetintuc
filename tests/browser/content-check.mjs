import { solveLoginCaptcha } from './login-captcha.mjs';
import { chromium } from 'playwright';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const fixture = JSON.parse(await readFile(resolve(root, '.local/tests/phase2a/ui-credentials.json'), 'utf8'));
if (fixture.database !== 'LandPortal_Test' || new URL(fixture.baseUrl).hostname !== '127.0.0.1') throw new Error('Typed content browser requires isolated SQL Test localhost.');
const evidence = resolve(process.env.LandPortal__EvidenceRoot || resolve(root, '.local/evidence'), 'phase2b/content-ui'); await mkdir(evidence, { recursive: true });
const report = { utc: new Date().toISOString(), database: fixture.database, pageChecks: [], actions: [], pageErrors: [], offOriginRequests: [], limitations: ['Headless Edge viewport emulation; no physical mobile/UAT/WCAG certification.', 'QA documents and agencies are explicitly test fixtures in SQL Test; no source Dev article changed.'] };
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const writerContext = await browser.newContext(), reviewerContext = await browser.newContext(), guestContext = await browser.newContext();
for (const context of [writerContext, reviewerContext, guestContext]) await context.route('**/*', async route => { const url = new URL(route.request().url()); if (url.origin !== new URL(fixture.baseUrl).origin) { report.offOriginRequests.push(url.origin); await route.abort(); } else await route.continue(); });
const writer = await writerContext.newPage(), reviewer = await reviewerContext.newPage(), guest = await guestContext.newPage();
for (const page of [writer, reviewer, guest]) page.on('pageerror', error => report.pageErrors.push(error.message));
function assert(value, message) { if (!value) throw new Error(message); }
async function login(page, name) { await page.goto(fixture.baseUrl + '/admin/login'); await page.locator('[name=UserName]').fill(name); await page.locator('[name=Password]').fill(fixture.password); await solveLoginCaptcha(page); await Promise.all([page.waitForURL('**/admin'), page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()]); }
async function save(page, selector = '[data-save]') {
  const before = await page.locator('[name=CurrentRevisionId]').inputValue();
  const response = page.waitForResponse(r => r.url().includes('/admin/save') && r.request().method() === 'POST' && /name="autosave"\r?\n\r?\nfalse/.test(r.request().postData() || ''));
  await page.locator(selector).click(); const result = await response; assert(result.ok(), 'Typed UI save failed HTTP ' + result.status());
  await page.waitForFunction(({ selector, before }) => { const form = document.querySelector('#editor-form'), id = document.querySelector('[name=Id]')?.value, revision = document.querySelector('[name=CurrentRevisionId]')?.value; return Boolean(id && revision && (selector.includes('submit') ? form?.dataset.state === 'PendingReview' : revision !== before)); }, { selector, before });
  await page.waitForLoadState('networkidle');
  // A new save navigates after consuming fetch JSON. Read the confirmed/reloaded
  // form rather than a CDP response body discarded by that real navigation.
  return { contentId: await page.locator('[name=Id]').inputValue(), currentRevisionId: await page.locator('[name=CurrentRevisionId]').inputValue() };
}
async function workflow(page, action) {
  const response = page.waitForResponse(r => r.url().includes('/admin/workflow/') && r.request().method() === 'POST');
  const navigation = page.waitForEvent('framenavigated', { predicate: frame => frame === page.mainFrame() });
  await page.locator(`[data-workflow="${action}"]`).click(); const result = await response; assert(result.ok(), 'Typed workflow ' + action + ' failed HTTP ' + result.status()); await navigation; await page.waitForLoadState('networkidle');
}
async function screenshot(page, name, width, path) {
  await page.setViewportSize({ width, height: 900 }); const response = await page.goto(fixture.baseUrl + path, { waitUntil: 'networkidle' });
  const layout = await page.evaluate(() => ({ viewport: innerWidth, scrollWidth: document.documentElement.scrollWidth, heading: document.querySelector('h1')?.textContent.trim(), unlabelled: [...document.querySelectorAll('input:not([type=hidden]),textarea,select')].filter(x => x.getClientRects().length && !x.labels?.length && !x.getAttribute('aria-label') && !x.getAttribute('aria-labelledby')).map(x => x.name || x.id) }));
  const pass = response?.status() === 200 && layout.scrollWidth <= width + 1 && layout.unlabelled.length === 0;
  report.pageChecks.push({ name, width, path, status: response?.status(), pass, ...layout }); await page.screenshot({ path: resolve(evidence, `${name}-${width}.png`), fullPage: true }); assert(pass, `${name} at ${width}px failed label/reflow check.`);
}
async function metadata(name, value) { await writer.locator(`[name="Metadata.${name}"]`).fill(value); }
async function pdfAttachment(label) {
  await writer.locator('[data-open-media="attachment"]').click(); await writer.locator('#media-search').fill(fixture.pdfName);
  const loaded = writer.waitForResponse(r => r.url().includes('/admin/media?') && r.request().method() === 'GET'); await writer.locator('[data-search-media]').click(); await loaded;
  await writer.locator('#media-items .media-item').filter({ hasText: fixture.pdfName }).first().click();
  await writer.locator(`input[name="AttachmentLabels[${fixture.pdf}]"]`).fill(label);
}
try {
  await login(writer, fixture.writer); await login(reviewer, fixture.reviewer);
  const stamp = Date.now(); const savedKinds = {};
  for (const kind of ['Announcement', 'LegalDocument', 'DraftDocument', 'StaticPage']) {
    await writer.goto(fixture.baseUrl + '/admin/create?kind=' + kind, { waitUntil: 'networkidle' });
    const title = `QA UI ${kind} dữ liệu thử ${stamp}`; await writer.locator('[name=Title]').fill(title); await writer.locator('[name=Summary]').fill('QA SQL Test — metadata và bản nháp được kiểm tra qua giao diện.');
    const category = await writer.locator('[name=CategoryId] option').evaluateAll(options => options.find(x => x.value)?.value); assert(category, 'Typed content has no category picker.'); await writer.locator('[name=CategoryId]').selectOption(category);
    await metadata('PublicationDate', '2026-10-13');
    const body = writer.locator('.jodit-wysiwyg[contenteditable=true]').first();
    if (kind === 'StaticPage') { await body.fill(''); await pdfAttachment('QA PDF giới thiệu — tài nguyên có thật trong kho Test'); }
    else { await body.click(); await writer.keyboard.type('QA nội dung mẫu ' + kind + ' được nhập bằng bàn phím và lưu vào SQL Test.'); }
    if (kind === 'Announcement') await metadata('Unit', 'Đơn vị QA nội bộ');
    if (kind === 'LegalDocument' || kind === 'DraftDocument') {
      await metadata('Number', `QA-UI-${stamp}/${kind === 'LegalDocument' ? '42/2026/NĐ-CP' : 'DT/2026'}`);
      await metadata('DocumentType', kind === 'LegalDocument' ? 'Nghị định QA' : 'Dự thảo QA'); await metadata('IssuingBody', 'Cơ quan QA nội bộ'); await metadata('Field', 'Đất đai QA');
      await writer.locator('[name="Metadata.MappingStatus"]').selectOption('Reviewed');
    }
    if (kind === 'LegalDocument') { await metadata('Signer', 'Người ký QA'); await metadata('IssuedDate', '2026-10-01'); await metadata('EffectiveDate', '2026-11-01'); await writer.locator('[name="Metadata.EffectivenessStatus"]').selectOption('ConfirmedEffective'); await metadata('EffectivenessEvidence', 'QA Test: căn cứ giả lập cho phép kiểm tra trường hiệu lực; không phải văn bản cơ quan.'); await pdfAttachment('QA tài liệu văn bản đính kèm'); }
    if (kind === 'DraftDocument') { await metadata('ConsultationStartDate', '2026-10-01'); await metadata('ConsultationEndDate', '2026-11-05'); await metadata('ConsultationDateEvidence', 'QA Test: khoảng ngày được nhập tường minh cho bài thử.'); }
    const result = await save(writer); const id = result.contentId; savedKinds[kind] = { id, title, revisionId: result.currentRevisionId };
    for (const width of [390, 1440]) await screenshot(writer, 'typed-' + kind.toLowerCase(), width, '/admin/edit/' + id);
    assert((await writer.locator('[name=Kind]').inputValue()) === kind, 'Persisted content kind changed.'); assert((await writer.locator('[name="Metadata.PublicationDate"]').inputValue()) === '2026-10-13', 'Metadata date-only changed after SQL reload.');
    const anonymous = await guestContext.request.get(fixture.baseUrl + '/bai-viet/' + id); assert(anonymous.status() === 404, 'Typed draft leaked to anonymous article route.');
    await writer.goto(fixture.baseUrl + '/admin/preview/' + id, { waitUntil: 'networkidle' }); assert((await writer.locator('.preview-article h1').textContent()) === title, 'Typed private preview did not show the saved title.');
    if (kind === 'StaticPage') { assert((await writer.locator('.article-body').innerText()).trim() === '', 'PDF-only static page acquired invented prose.'); assert(await writer.locator(`.attachments a[href*="${fixture.pdf}"]`).count() === 1, 'Real PDF-only static preview lacks its attachment.'); }
    report.actions.push({ name: `Create/save/reload/private preview ${kind}${kind === 'StaticPage' ? ' with PDF-only body' : ''}`, pass: true, contentId: id, revisionId: result.currentRevisionId });
  }
  const legal = savedKinds.LegalDocument; const number = `QA-UI-${stamp}/42/2026/NĐ-CP`;
  await writer.goto(fixture.baseUrl + '/admin/edit/' + legal.id); const submitted = await save(writer, '[data-save-submit]'); assert(submitted.currentRevisionId === legal.revisionId, 'Submit changed an unchanged immutable legal revision.');
  await reviewer.goto(fixture.baseUrl + '/admin/edit/' + legal.id); assert(await reviewer.locator('[name="Metadata.Number"]').inputValue() === number, 'Reviewer sees wrong metadata revision.'); await workflow(reviewer, 'Approve'); await workflow(reviewer, 'PublishApproved');
  await guest.goto(fixture.baseUrl + '/bai-viet/' + legal.id, { waitUntil: 'networkidle' }); assert((await guest.locator('h1').textContent()).trim() === legal.title, 'Typed publication selected a different title.');
  const published = await guest.locator('.document-metadata').innerText(); for (const expected of [number, 'Nghị định QA', 'Cơ quan QA nội bộ', 'Người ký QA', '01/10/2026', '01/11/2026', '13/10/2026']) assert(published.includes(expected), 'Published legal metadata missing ' + expected);
  assert((await guest.locator('.attachments').innerText()).includes('QA tài liệu văn bản đính kèm'), 'Published PDF label did not follow its approved revision.');
  report.actions.push({ name: 'Writer submits immutable legal revision; separate reviewer approves and publishes exact typed metadata/date/PDF label', pass: true, contentId: legal.id, revisionId: legal.revisionId });
  await writer.goto(fixture.baseUrl + '/admin/edit/' + legal.id); await workflow(writer, 'StartDraft'); const changedNumber = `QA-UI-PRIVATE-${stamp}/DRAFT`;
  await metadata('Number', changedNumber); await metadata('IssuedDate', '2026-12-01'); await save(writer);
  await guest.goto(fixture.baseUrl + '/bai-viet/' + legal.id); const unchanged = await guest.locator('.document-metadata').innerText(); assert(unchanged.includes(number) && unchanged.includes('01/10/2026') && !unchanged.includes(changedNumber) && !unchanged.includes('01/12/2026'), 'Draft legal metadata changed the public revision.');
  await guest.goto(fixture.baseUrl + '/tim-kiem?q=' + encodeURIComponent(changedNumber)); assert((await guest.locator('.result-count').innerText()).startsWith('0 '), 'Search reveals draft-only legal metadata.');
  report.actions.push({ name: 'New legal draft changes number/date while public article and search remain on approved revision', pass: true });
  await guest.goto(fixture.baseUrl + '/van-ban'); await guest.locator('#doc-q').fill(number); await guest.locator('[name=agency]').fill('Co quan QA noi bo'); await guest.locator('[name=issuedFrom]').fill('2026-10-01'); await guest.locator('[name=issuedTo]').fill('2026-10-01');
  await Promise.all([guest.waitForNavigation(), guest.getByRole('button', { name: 'Tra cứu', exact: true }).click()]); assert(await guest.locator(`a[href^="/bai-viet/${legal.id}"]`).count() >= 1, 'Advanced SQL document lookup lost the published legal revision.');
  report.actions.push({ name: 'Advanced document UI submits combined Vietnamese-normalized authority, number and issue-date filters', pass: true });
  for (const width of [360, 768, 1024]) { await screenshot(guest, 'search', width, '/tim-kiem?q=dat&kind=LegalDocument'); await screenshot(guest, 'documents', width, '/van-ban?q=dat'); }
  assert(report.pageErrors.length === 0 && report.offOriginRequests.length === 0, 'Typed content browser errors or source/hotlink requests.');
} catch (error) { report.failure = String(error.message).split(fixture.password).join('[REDACTED]'); await writer.screenshot({ path: resolve(evidence, 'failed-editor.png'), fullPage: true }).catch(() => {}); process.exitCode = 1; }
finally { await browser.close(); await writeFile(resolve(evidence, 'ui-report.json'), JSON.stringify(report, null, 2)); console.log(JSON.stringify({ pages: report.pageChecks.length, passingPages: report.pageChecks.filter(x => x.pass).length, actions: report.actions.length, failures: report.failure ? 1 : 0, errors: report.pageErrors.length })); if (report.failure) console.log(report.failure); }
