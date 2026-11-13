// Focused browser contract: a server-side video attachment changes the real draft
// version. HTTP replies are fixtures; SQL/worker coverage lives in the C# checks.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
const { chromium } = await import(process.env.CMS_PLAYWRIGHT_MODULE || 'playwright');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const assets = resolve(root, 'src/Web/wwwroot');
const content = '11111111-1111-4111-8111-111111111111';
const video = '22222222-2222-4222-8222-222222222222';
const initialVersion = 'MDAwMDAwMDE=', attachedVersion = 'MDAwMDAwMDI=';
let renderedVersion = initialVersion, saveCalls = 0, releaseSave, holdSave = false;
const errors = [];
function html() {
  return `<!doctype html><html lang="vi"><meta charset="utf-8"><body>
<a class="back-link" href="/admin?kind=Video&amp;state=Draft">Danh sách</a>
<form id="editor-form" data-state="Draft" data-can-save="true" data-user-id="isolated">
<input type="hidden" name="Id" value="${content}"><input type="hidden" name="RowVersion" value="${renderedVersion}">
<input type="hidden" name="CurrentRevisionId" value="${content}"><input type="hidden" name="RequestId"><input type="hidden" name="__RequestVerificationToken" value="isolated">
<input name="Title" value="Bản nháp đang soạn"><input name="Kind" value="Video" type="hidden"><input name="CoverAssetId" type="hidden">
<textarea id="content-html" name="BodyHtml">Nội dung được giữ nguyên</textarea><p id="editor-feedback"></p>
<div class="editor-video-references">${renderedVersion === attachedVersion ? `<input type="hidden" name="VideoAssetIds" value="${video}">` : ''}</div>
<button type="submit" name="intent" value="Save">Lưu</button></form>
<input id="outside-file" type="file">
<script src="/vendor/jodit/jodit.min.js"></script><script src="/js/editor.js"></script></body></html>`;
}
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const context = await browser.newContext(); context.setDefaultTimeout(10000);
await context.route('**/*', async route => {
  const url = new URL(route.request().url());
  if (url.hostname !== 'fixture.test') return route.abort();
  if (url.pathname === '/admin/save') {
    saveCalls++;
    if (holdSave) await new Promise(resolve => { releaseSave = resolve; });
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ contentId: content, currentRevisionId: content, rowVersion: attachedVersion, state: 'Draft', isPublic: false, isTrashed: false, savedUtc: new Date().toISOString() }) });
  }
  if (url.pathname.startsWith('/vendor/') || url.pathname.startsWith('/js/')) {
    const path = resolve(assets, '.' + url.pathname);
    assert(path.startsWith(assets + '\\') || path.startsWith(assets + '/'));
    return route.fulfill({ contentType: extname(path) === '.js' ? 'text/javascript' : 'text/css', body: await readFile(path) });
  }
  return route.fulfill({ contentType: 'text/html; charset=utf-8', body: html() });
});
let passed = 0;
const check = (truth, text) => { assert(truth, text); passed++; console.log('PASS ' + text); };
async function page() {
  const p = await context.newPage(); p.on('pageerror', e => errors.push(e.message));
  await p.goto('http://fixture.test/admin/edit/' + content + '?returnUrl=' + encodeURIComponent('/admin?kind=Video&state=Draft'));
  await p.waitForFunction(() => !!window.landPortalEditor?.acceptServerVideoState);
  return p;
}
try {
  const clean = await page();
  check(await clean.evaluate(v => window.landPortalEditor.acceptServerVideoState({ rowVersion: v }), initialVersion) === 'current', 'Matching version causes no reload or draft change');
  renderedVersion = attachedVersion;
  await Promise.all([clean.waitForNavigation(), clean.evaluate(v => window.landPortalEditor.acceptServerVideoState({ rowVersion: v }), attachedVersion)]);
  check(await clean.locator('[name=RowVersion]').inputValue() === attachedVersion && await clean.locator('[name=VideoAssetIds]').inputValue() === video, 'Clean open editor reloads complete server draft, including attached video');
  check(new URL(clean.url()).searchParams.get('returnUrl') === '/admin?kind=Video&state=Draft', 'Clean refresh preserves list filters');
  await clean.close();

  renderedVersion = initialVersion;
  const dirty = await page();
  await dirty.locator('[name=Title]').fill('Phần mới chưa lưu phải được giữ');
  check(await dirty.evaluate(v => window.landPortalEditor.acceptServerVideoState({ rowVersion: v }), attachedVersion) === 'conflict', 'Changed server version pauses dirty editor instead of merging silently');
  const before = saveCalls; await dirty.waitForTimeout(1750);
  check(saveCalls === before, 'Stale asset list is not autosaved after background attachment');
  check(await dirty.locator('[name=Title]').inputValue() === 'Phần mới chưa lưu phải được giữ' && await dirty.locator('[name=RowVersion]').inputValue() === initialVersion, 'Unsaved input and original concurrency token stay intact');
  check(await dirty.locator('#editor-video-reconcile').getAttribute('target') === '_blank' && (await dirty.locator('#editor-feedback').textContent()).includes('tự lưu đã tạm dừng'), 'Explicit comparison action opens separate tab with clear Vietnamese guidance');
  await dirty.close({ runBeforeUnload: false });

  const inflight = await page();
  holdSave = true;
  await inflight.locator('button[type=submit]').click();
  await inflight.waitForFunction(() => document.querySelector('#editor-feedback').textContent.includes('Đang lưu'));
  check(await inflight.evaluate(v => window.landPortalEditor.acceptServerVideoState({ rowVersion: v }), attachedVersion) === 'deferred', 'Polling defers while an actual save is in flight');
  while (!releaseSave) await inflight.waitForTimeout(10);
  releaseSave(); holdSave = false; releaseSave = undefined;
  await inflight.waitForFunction(v => document.querySelector('[name=RowVersion]').value === v, attachedVersion);
  check(await inflight.evaluate(v => window.landPortalEditor.acceptServerVideoState({ rowVersion: v }), attachedVersion) === 'current', 'Save completion resolves polling race without false conflict');
  await inflight.close();

  const selected = await page();
  await selected.locator('#outside-file').setInputFiles({ name: 'new.png', mimeType: 'image/png', buffer: Buffer.from('unsubmitted fixture') });
  check(await selected.evaluate(v => window.landPortalEditor.acceptServerVideoState({ rowVersion: v }), attachedVersion) === 'conflict', 'Unsaved file selection outside form prevents destructive automatic reload');
  check(await selected.locator('#outside-file').evaluate(input => input.files[0].name) === 'new.png', 'Selected local file is retained for operator review');
  await selected.close();
  check(errors.length === 0, 'Real editor/Jodit bridge has no JavaScript errors');
  console.log(`CMS_VIDEO_BACKGROUND_BROWSER ${passed}/${passed} PASS (mock HTTP; no SQL, no screenshots)`);
} finally {
  if (releaseSave) releaseSave();
  await browser.close();
}
