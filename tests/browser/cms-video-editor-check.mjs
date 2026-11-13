import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

// Browser/DOM interaction delta only. The HTTP replies below are fixtures, not
// evidence of SQL/FFmpeg/antivirus processing; those boundaries have separate tests.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const script = await readFile(resolve(root, 'src/Web/wwwroot/js/editor-video.js'), 'utf8');
const css = await readFile(resolve(root, 'src/Web/wwwroot/css/video.css'), 'utf8');
const contentId = '11111111-1111-4111-8111-111111111111';
const sessionId = '22222222-2222-4222-8222-222222222222';
const assetId = '33333333-3333-4333-8333-333333333333';
let session = null, attached = false, editable = true, attachCount = 0, createCount = 0, failures = [];
const requests = [];
const html = `<!doctype html><html lang="vi"><meta charset="utf-8"><style>*{box-sizing:border-box}body{margin:8px;font:16px Arial}button,input{font:inherit;max-width:100%}${css}</style>
<form><input type="hidden" name="__RequestVerificationToken" value="isolated-csrf"><section id="editor-video" class="editor-video video-panel" data-content-id="${contentId}" data-max-bytes="1024" data-chunk-bytes="16">
<h2>Video của bài viết</h2><label for="editor-video-file">Chọn video</label><input id="editor-video-file" type="file">
<div class="video-actions"><button type="button" data-video-action="start">Tải video lên</button><button type="button" data-video-action="pause" disabled>Tạm dừng</button><button type="button" data-video-action="resume" disabled>Tiếp tục tải</button><button type="button" data-video-action="cancel" disabled>Hủy lần tải</button></div>
<label for="editor-video-progress">Tiến độ tải</label><progress id="editor-video-progress" max="100" value="0"></progress><p data-video-message role="status"></p><p data-video-processing role="status"></p>
<button type="button" data-video-action="attach" hidden>Kiểm tra và gắn lại vào nháp</button><button type="button" data-video-action="retry" hidden>Thử xử lý lại</button><div data-video-preview></div><div data-video-attached></div><details><summary>Các lần tải của tôi</summary><div data-video-sessions></div></details></section></form>
<script>window.bridgeCalls=0;window.landPortalEditor={ensureSavedDraft:async()=>({id:'${contentId}',rowVersion:'current'}),withSavedDraft:async operation=>{window.bridgeCalls++;return operation({id:'${contentId}',rowVersion:'current'});}};</script><script src="/editor-video.js"></script></html>`;
const server = createServer(async (request, response) => {
  const path = new URL(request.url, 'http://localhost').pathname;
  requests.push({ path, method: request.method });
  const chunks = []; for await (const chunk of request) chunks.push(chunk); const bytes = Buffer.concat(chunks);
  const send = (value, status = 200) => { response.writeHead(status, { 'Content-Type': 'application/json' }); response.end(JSON.stringify(value)); };
  if (path === '/') { response.writeHead(200, { 'Content-Type': 'text/html' }); response.end(html); return; }
  if (path === '/editor-video.js') { response.writeHead(200, { 'Content-Type': 'application/javascript' }); response.end(script); return; }
  if (request.method !== 'GET' && request.headers['x-csrf-token'] !== 'isolated-csrf') { send({ error: 'CSRF missing' }, 400); return; }
  if (path === `/admin/videos/editor/${contentId}`) {
    send({ canUpload: editable, sessions: session ? [{ ...session, status: session.ready ? 'Đã xử lý' : 'Đang tải', autoAttach: editable && session.ready && !attached, attached, retryAllowed: false }] : [], attached: attached ? [{ mediaAssetId: assetId, ready: true, status: 'Đã xử lý' }] : [] }); return;
  }
  if (path === '/admin/videos/sessions' && request.method === 'POST') {
    createCount++; const value = JSON.parse(bytes);
    if (!value.autoAttach || value.name !== 'Bản tin.v2.mp4') { send({ error: 'Expected explicit autoattach and multi-dot name' }, 400); return; }
    session = { id: sessionId, state: 'Uploading', expectedBytes: value.bytes, receivedBytes: 0, chunkBytes: 16, chunkCount: Math.ceil(value.bytes / 16), chunkManifestSha256: value.chunkManifestSha256, extension: '.mp4', chunks: [], ready: false };
    send(session); return;
  }
  if (path === `/admin/videos/sessions/${sessionId}`) { send(session); return; }
  if (path.startsWith(`/admin/videos/sessions/${sessionId}/chunks/`)) {
    const index = Number(path.split('/').pop()); const hash = createHash('sha256').update(bytes).digest('hex');
    if (hash !== request.headers['x-chunk-sha256']) { send({ error: 'Wrong bytes' }, 409); return; }
    session.chunks.push({ index, bytes: bytes.length, sha256: hash }); session.receivedBytes += bytes.length;
    send({ confirmed: true }); return;
  }
  if (path.endsWith('/complete')) { session = { ...session, state: 'Completed', mediaAssetId: assetId, ready: true, job: { state: 'Ready' } }; send(session); return; }
  if (path.endsWith('/attach-ready')) { attachCount++; const value = JSON.parse(bytes); if (value.rowVersion !== 'current' || value.sessionId !== sessionId) { send({ error: 'Wrong draft' }, 409); return; } attached = true; send({ contentId, currentRevisionId: 'revision', rowVersion: 'after', state: 'Draft', isPublic: false, isTrashed: false, savedUtc: new Date().toISOString(), videoAssetId: assetId }); return; }
  response.writeHead(404); response.end();
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ channel: 'msedge', headless: true });
let count = 0;
function check(pass, message) { if (!pass) throw new Error(message); count++; console.log(`PASS ${message}`); }
try {
  const page = await browser.newPage(); page.on('pageerror', error => failures.push(error.message));
  await page.goto(origin); await page.locator('[data-video-sessions]').getByText('Chưa có lần tải video nào.').waitFor({ state: 'attached' });
  const file = { name: 'Bản tin.v2.mp4', mimeType: 'video/mp4', buffer: Buffer.from('ISOLATED VIDEO BYTES; UI ONLY!') };
  await page.locator('#editor-video-file').setInputFiles(file);
  await page.locator('[data-video-action="start"]').click();
  await page.waitForFunction(() => document.querySelector('[data-video-processing]').textContent.includes('được gắn vào bản nháp'));
  check(createCount === 1 && attachCount === 1, 'Editor uploads once and autoattaches processed video once');
  check(await page.evaluate(() => window.bridgeCalls) === 1, 'Autoattach uses shared saved-draft bridge and exact rowversion');
  check(await page.locator('#editor-video-progress').evaluate(node => node.value) === 100, 'Upload progress uses acknowledged bytes');
  check(await page.locator('[data-video-preview] video').getAttribute('src') === `/admin/videos/media/${assetId}/play?contentId=${contentId}`, 'Preview uses authorized processed-media endpoint');
  check(!requests.some(x => x.path.includes('/workflow/') || x.path.includes('/publish')), 'Upload/autoattach never invoke publication');
  await page.reload(); await page.locator('[data-video-attached]').getByText('Video đã gắn:', { exact: false }).waitFor();
  check(attachCount === 1, 'Reopened page restores attached state without reattaching');
  for (const width of [320, 390, 768, 1366]) {
    await page.setViewportSize({ width, height: 800 });
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `Video upload UI has no horizontal overflow at ${width}px`);
  }
  editable = false; await page.reload();
  await page.waitForFunction(() => document.querySelector('[data-video-action="start"]').disabled);
  check(await page.locator('[data-video-action="start"]').isDisabled(), 'Closed draft disables uploading after server state refresh');
  check(failures.length === 0, 'Video editor interaction has no JavaScript errors');
  console.log(`CMS_VIDEO_BROWSER ${count}/${count} PASS (mock HTTP UI fixture; no SQL, screenshots or native processing)`);
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
