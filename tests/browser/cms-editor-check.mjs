// Isolated presentation/HTTP-contract checks with the real Jodit and editor scripts.
// Endpoints are mocked; this does not claim SQL, permissions, or file validation coverage.
// CMS_EDITOR_RENDERED_ROOT may point to editor.html / weekly.html from the MVC harness.
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, extname } from 'node:path';
import assert from 'node:assert/strict';
const { chromium } = await import(process.env.CMS_PLAYWRIGHT_MODULE || 'playwright');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const assetRoot = resolve(root, 'src/Web/wwwroot');
const read = p => readFile(resolve(root, p), 'utf8');
const css = (await Promise.all(['site', 'interactions', 'admin-editor', 'site-config', 'admin-theme'].map(x => read('src/Web/wwwroot/css/' + x + '.css')))).join('\n');
const contentId = '11111111-1111-1111-1111-111111111111';
const imageId = '22222222-2222-2222-2222-222222222222';
const documentId = '33333333-3333-3333-3333-333333333333';
const wordId = '44444444-4444-4444-4444-444444444444';
const fields = ['Date', 'Period', 'Time', 'Content', 'Chair', 'Attendees', 'Location', 'Note'];
const row = i => `<details data-weekly-row ${i < 2 ? 'open' : 'hidden'}><summary>Buổi ${i + 1}</summary><button type="button" data-copy-weekly>Sao chép buổi này</button>${fields.map(k => `<label for="ScheduleRows_${i}__${k}">${k}</label>${k === 'Period' ? `<select id="ScheduleRows_${i}__${k}" name="ScheduleRows[${i}].${k}"><option value=""></option><option>Sáng</option><option>Chiều</option></select>` : `<input id="ScheduleRows_${i}__${k}" name="ScheduleRows[${i}].${k}" ${k === 'Date' ? 'type="date"' : ''}>`}`).join('')}</details>`;
function fixture(weekly) {
    const weeklyFields = `<fieldset><legend>Lịch tuần</legend><input id="weekly-date" type="date"><p id="weekly-range"></p><input name="Metadata.WeeklyYear" value="2026"><input name="Metadata.WeeklyWeek" value="46"><input name="Metadata.WeeklyOriginalAssetId" type="hidden"><div id="weekly-original-review" hidden><a id="weekly-original-link">Word gốc</a><textarea name="Metadata.WeeklyOriginalReviewEvidence"></textarea></div>${Array.from({ length: 4 }, (_, i) => row(i)).join('')}<button type="button" data-add-weekly>Thêm buổi</button></fieldset>`;
    return `<body class="admin-body"><main class="admin-main"><form id="editor-form" class="editor-layout" data-state="Draft" data-can-save="true" data-user-id="fixture-user"><input name="__RequestVerificationToken" type="hidden" value="fixture-csrf"><input name="Id" type="hidden"><input name="RowVersion" type="hidden"><input name="CurrentRevisionId" type="hidden"><input name="RequestId" type="hidden"><input name="Kind" type="hidden" value="${weekly ? 'StaticPage' : 'Article'}"><input name="CoverAssetId" type="hidden"><section class="editor-main"><div id="editor-feedback" tabindex="-1"></div><label for="Title">Tiêu đề</label><input id="Title" name="Title" class="title-input"><textarea name="Summary"></textarea><label for="content-html">Nội dung</label><textarea id="content-html" name="BodyHtml"></textarea><button type="button" data-open-media="inline">Chèn ảnh</button><button type="button" data-open-media="cover">Chọn ảnh đại diện</button><div id="cover-preview"></div><ul id="selected-attachments"></ul><div class="editor-document-upload"><input id="editor-document-file" type="file" accept=".pdf,.docx,.xlsx"><progress id="document-progress" hidden></progress><p id="document-feedback"></p><button type="button" data-document-library>Tài liệu đã tải</button><div id="document-library"></div></div><div class="editor-video-references"></div><select name="Metadata.DisclosureGroupId"><option value="">Bài tin thông thường</option><option value="land">Công khai</option></select><div id="disclosure-fields" hidden><input name="Metadata.Province" value="Tỉnh đang soạn"><input name="Metadata.PublicationSource" value="Nguồn đang soạn"><textarea name="Metadata.PublicationReviewEvidence">Bằng chứng đang soạn</textarea></div>${weekly ? weeklyFields : ''}</section><aside class="editor-settings"><div id="editor-workflow-readiness"></div><input name="Author"><select name="CategoryId"><option value="76">Chuyên mục</option></select><button type="submit" name="intent" value="Save" data-save>Lưu bản nháp</button><button type="submit" name="intent" value="Submit" data-save-submit>Gửi duyệt</button><button type="submit" name="intent" value="Publish" data-save-publish>Đăng</button><a id="preview-link" href="#" target="_blank">Xem trước</a></aside></form></main><dialog id="media-dialog"><h2 id="media-dialog-title"></h2><button type="button" data-close-media>Đóng</button><input id="media-upload-file" type="file"><progress id="upload-progress" hidden></progress><p id="upload-feedback"></p><input id="media-search"><button data-search-media>Tìm</button><input id="media-caption"><div id="media-items"></div><div id="media-pagination"></div></dialog></body>`;
}
const rendered = new Set();
async function html(weekly, review = false) {
    let content = fixture(weekly);
    const file = process.env.CMS_EDITOR_RENDERED_ROOT ? resolve(process.env.CMS_EDITOR_RENDERED_ROOT, weekly ? 'weekly.html' : 'editor.html')
        : weekly ? process.env.CMS_WEEKLY_HTML : process.env.CMS_EDITOR_HTML;
    if (file) {
        content = await readFile(file, 'utf8');
        rendered.add(weekly ? 'weekly' : 'editor');
    }
    content = content.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<link\b[^>]*>/gi, '');
    if (review) {
        // A role-specific Razor-shaped control for the partial-success presentation case.
        content = content.replace('data-can-save="true"', 'data-can-save="false"').replace('data-state="Draft"', 'data-state="PendingReview"')
            .replace(/<aside class="editor-settings"[^>]*>/, match => match + '<button type="button" data-workflow="ApprovePublish">Duyệt và xuất bản fixture</button>');
        content += `<script>const f=document.querySelector('#editor-form');f.elements.namedItem('Id').value='${contentId}';f.elements.namedItem('CurrentRevisionId').value='${contentId}';f.elements.namedItem('RowVersion').value='MDAwMDAwMDE=';</script>`;
    }
    return content + `<style>${css}</style><link rel="stylesheet" href="/vendor/jodit/jodit.min.css"><script>window.landPortalStateLabel=x=>x;</script><script src="/js/admin-shell.js"></script><script src="/vendor/jodit/jodit.min.js"></script><script src="/js/editor.js"></script><script src="/js/editor-weekly.js"></script>`;
}
function formValues(request) {
    const body = request.postDataBuffer()?.toString('utf8') || '';
    const values = {};
    for (const match of body.matchAll(/Content-Disposition: form-data; name="([^"]+)"(?:;[^\r]*)?\r\n(?:[^\r]+\r\n)*\r\n([\s\S]*?)\r\n--/g)) {
        (values[match[1]] ||= []).push(match[2]);
    }
    return values;
}
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const context = await browser.newContext();
context.setDefaultTimeout(10000);
let checks = 0, saveCount = 0, failSave = false, uploads = 0;
let deferDocument = false, releaseDocument;
const saves = [], errors = [], forbiddenNetwork = [];
const image = { id: imageId, originalName: 'fixture.png', mimeType: 'image/png', byteLength: 68, scanStatus: 'NotScanned', previewUrl: '/admin/media/' + imageId + '/preview', url: '/media/' + imageId, notice: 'Ảnh fixture đã tải, chưa công khai.' };
const document = { id: documentId, originalName: 'fixture.pdf', mimeType: 'application/pdf', byteLength: 60, scanStatus: 'NotScanned', previewUrl: '/admin/documents/file/' + documentId, url: '/media/' + documentId, usable: true, notice: 'Fixture kiểm tra giao diện.' };
const word = { ...document, id: wordId, originalName: 'lich.docx', mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' };
context.on('page', page => page.on('pageerror', error => errors.push(error.message)));
await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url());
    if (url.hostname !== 'fixture.test') { forbiddenNetwork.push(request.url()); return route.abort(); }
    const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (url.pathname === '/admin/save') {
        const values = formValues(request); saves.push(values); saveCount++;
        if (failSave) return json({ message: 'Chưa lưu fixture.', fieldErrors: { Title: 'Sửa tiêu đề fixture.' } }, 400);
        return json({ contentId, currentRevisionId: contentId, rowVersion: Buffer.from(String(saveCount).padStart(8, '0')).toString('base64'), state: 'Draft', isPublic: false, isTrashed: false, savedUtc: new Date().toISOString() });
    }
    if (url.pathname.startsWith('/admin/editor-readiness/')) return json({ errors: {}, fileNotice: 'Fixture readiness.', canSubmit: true, canPublish: true });
    if (url.pathname.startsWith('/admin/approve-publish/')) return json({ message: 'Đã duyệt nhưng chưa xuất bản fixture.', savedDraft: { contentId, currentRevisionId: contentId, rowVersion: 'MDAwMDAwMDI=', state: 'Approved', savedUtc: new Date().toISOString() } }, 400);
    if (url.pathname === '/admin/media/upload') { uploads++; return json(image); }
    if (url.pathname === '/admin/media') return json({ items: [], total: 0, pageSize: 20, uploadAvailable: true, notice: '' });
    if (url.pathname.startsWith('/admin/documents/') && request.method() === 'POST') { uploads++; if (deferDocument) await new Promise(resolve => { releaseDocument = resolve; }); return json(document); }
    if (url.pathname.endsWith('/items')) return json({ items: [document, word] });
    if (url.pathname.startsWith('/admin/editor-weekly/')) return json({ rows: [{ date: '2026-11-09', period: 'Sáng', time: '08:00', content: 'Lịch fixture từ Word', chair: 'Người thử', attendees: '', location: 'Phòng thử', note: '' }], warnings: ['Kiểm tra bản nhập.'], assetId: wordId });
    if (url.pathname.startsWith('/admin/media/') || url.pathname.startsWith('/media/')) return route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="640" height="360"><rect width="100%" height="100%" fill="#8B2E2E"/></svg>' });
    if (url.pathname.startsWith('/admin/preview/')) return route.fulfill({ contentType: 'text/html; charset=utf-8', body: '<h1>Preview fixture đã lưu</h1>' });
    if (url.pathname === '/admin/session') return json({ expiresUtc: new Date(Date.now() + 1800000).toISOString() });
    if (url.pathname.startsWith('/vendor/') || url.pathname.startsWith('/js/') || url.pathname.startsWith('/fonts/')) {
        const path = resolve(assetRoot, '.' + url.pathname);
        if (!path.startsWith(assetRoot + '\\') && !path.startsWith(assetRoot + '/')) return route.abort();
        try { return route.fulfill({ body: await readFile(path), contentType: ({ '.js': 'text/javascript; charset=utf-8', '.css': 'text/css', '.woff2': 'font/woff2' })[extname(path)] || 'application/octet-stream' }); } catch { return route.fulfill({ status: 404, body: '' }); }
    }
    return route.fulfill({ contentType: 'text/html; charset=utf-8', body: await html(url.searchParams.get('template') === 'weekly', url.searchParams.get('review') === 'true') });
});
const page = await context.newPage();
const waitSaved = async (target, before) => {
    await target.waitForFunction(() => document.querySelector('#editor-feedback').textContent.startsWith('Đã lưu lúc'));
    assert(saveCount > before, 'expected a save request');
};
try {
    await page.goto('http://fixture.test/admin/create');
    await page.locator('.jodit-wysiwyg').waitFor();
    // Use actual rendered markup but exercise new-draft client state against mocked saves.
    await page.evaluate(() => { for (const name of ['Id', 'RowVersion', 'CurrentRevisionId']) document.querySelector('#editor-form').elements.namedItem(name).value = ''; });
    const title = page.locator('[name=Title]');
    await title.fill('Bài fixture giữ nguyên tiêu điểm');
    await waitSaved(page, 0);
    assert.equal(await title.evaluate(x => x === window.document.activeElement), true);
    assert.equal(new URL(page.url()).pathname, '/admin/edit/' + contentId);
    const backLink = await page.locator('.back-link').getAttribute('href').catch(() => null);
    if (backLink?.startsWith('/admin?')) { assert.equal(new URL(page.url()).searchParams.get('returnUrl'), backLink); checks++; }
    assert.equal(await title.inputValue(), 'Bài fixture giữ nguyên tiêu điểm'); checks++;
    assert.equal(await page.locator('[name=Id]').inputValue(), contentId); checks++;
    await page.locator('.jodit-wysiwyg').fill('Nội dung fixture đang soạn');
    await page.locator('[data-open-media=cover]').click();
    await page.waitForFunction(() => !document.querySelector('#media-upload-file').disabled);
    await page.locator('#media-upload-file').setInputFiles({ name: 'fixture.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==', 'base64') });
    await page.waitForFunction(id => document.querySelector('[name=CoverAssetId]').value === id, imageId);
    assert.equal(await page.locator('#media-dialog').isVisible(), false); assert.equal(await page.locator('#cover-preview img').count(), 1); checks++;
    const beforeDocument = saveCount;
    await page.locator('#editor-document-file').setInputFiles({ name: 'fixture.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n% presentation fixture, endpoint validation mocked\n%%EOF') });
    await page.waitForFunction(() => document.querySelector('#document-feedback').textContent.startsWith('Đã đính kèm'));
    assert.equal(await page.locator('input[name=AttachmentIds]').inputValue(), documentId);
    assert(saveCount >= beforeDocument + 2, 'document upload saves before and after attaching');
    assert(saves.some(s => s.AttachmentIds?.includes(documentId))); checks++;
    assert.equal(await page.locator('.jodit-wysiwyg').innerText(), 'Nội dung fixture đang soạn'); checks++;
    const group = page.locator('[name="Metadata.DisclosureGroupId"]');
    await group.evaluate(x => { for (let p = x.parentElement; p; p = p.parentElement) if (p.tagName === 'DETAILS') p.open = true; });
    await group.selectOption(await group.locator('option').nth(1).getAttribute('value'));
    await page.locator('[name="Metadata.Province"]').fill('Tỉnh thử cần giữ trong DOM');
    await group.selectOption('');
    const beforeClear = saveCount; await page.locator('[data-save]').click(); await waitSaved(page, beforeClear);
    const cleared = saves.at(-1);
    assert(!cleared['Metadata.Province']?.some(Boolean) && !cleared['Metadata.PublicationSource']?.some(Boolean), 'inactive disclosure values must not be posted'); checks++;
    await group.selectOption(await group.locator('option').nth(1).getAttribute('value'));
    assert.equal(await page.locator('[name="Metadata.Province"]').inputValue(), 'Tỉnh thử cần giữ trong DOM'); checks++;
    await group.selectOption('');
    const beforePreview = saveCount;
    const popupPromise = page.waitForEvent('popup'); await page.locator('#preview-link').click(); const popup = await popupPromise;
    await popup.waitForURL('**/admin/preview/' + contentId);
    assert(saveCount > beforePreview); await popup.close(); checks++;
    failSave = true;
    const failedPopupPromise = page.waitForEvent('popup'); await page.locator('#preview-link').click(); const failedPopup = await failedPopupPromise;
    if (!failedPopup.isClosed()) await failedPopup.waitForEvent('close');
    assert.equal(new URL(page.url()).pathname, '/admin/edit/' + contentId);
    assert.equal(await title.inputValue(), 'Bài fixture giữ nguyên tiêu điểm'); checks++;
    failSave = false;
    for (const width of [320, 390, 768, 1366]) {
        await page.setViewportSize({ width, height: 900 });
        const dimensions = await page.evaluate(() => ({ viewport: innerWidth, scroll: document.documentElement.scrollWidth }));
        assert(dimensions.scroll <= dimensions.viewport + 1, `editor overflow at ${width}: ${dimensions.scroll}`); checks++;
        if (width <= 800 && await page.locator('.cms-menu-toggle').count()) {
            await page.locator('.cms-menu-toggle').click();
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `open menu overflow at ${width}`);
            await page.keyboard.press('Escape'); checks++;
        }
    }
    if (await page.locator('.editor-shortcuts').count()) {
        await page.setViewportSize({ width: 390, height: 844 });
        for (const anchor of ['editor-content-field', 'editor-files', 'editor-actions']) {
            const link = page.locator('.editor-shortcuts a[href="#' + anchor + '"]');
            assert.equal(await link.count(), 1);
            assert.equal(await page.locator('#' + anchor).getAttribute('tabindex'), '-1');
            await link.focus(); await page.keyboard.press('Enter');
            await page.waitForFunction(id => document.activeElement?.id === id, anchor);
            assert.equal(new URL(page.url()).hash, '#' + anchor);
            assert(await page.locator('.editor-shortcuts').evaluate(x => x.getBoundingClientRect().width <= innerWidth)); checks++;
        }
        assert.equal(await page.locator('[data-save]').count(), 1, 'shortcuts must not duplicate submission controls'); checks++;
    }
    const weekly = await context.newPage(); await weekly.goto('http://fixture.test/admin/create?template=weekly');
    await weekly.locator('.jodit-wysiwyg').waitFor();
    await weekly.locator('[name=Title]').fill('Lịch fixture');
    await weekly.locator('#weekly-date').fill('2026-11-11'); await weekly.locator('#weekly-date').dispatchEvent('change');
    assert.equal(await weekly.locator('[name="Metadata.WeeklyYear"]').inputValue(), '2026');
    assert.equal(await weekly.locator('[name="Metadata.WeeklyWeek"]').inputValue(), '46');
    assert.equal(await weekly.locator('#weekly-date').inputValue(), '2026-11-09');
    assert((await weekly.locator('#weekly-range').innerText()).includes('15/11/2026')); checks++;
    const visibleRows = () => weekly.locator('[data-weekly-row]:visible');
    const beforeRows = await visibleRows().count(); await weekly.locator('[data-add-weekly]').click();
    assert.equal(await visibleRows().count(), beforeRows + 1); checks++;
    await weekly.locator('[name="ScheduleRows[0].Content"]').fill('Buổi thử cần sao chép');
    await weekly.locator('[data-copy-weekly]').first().click();
    assert.equal(await weekly.locator('[name$=".Content"]').evaluateAll(xs => xs.filter(x => x.value === 'Buổi thử cần sao chép').length), 2); checks++;
    await weekly.locator('[data-document-library]').click();
    await weekly.getByRole('button', { name: 'Nhập lịch từ Word', exact: true }).waitFor();
    weekly.on('dialog', dialog => dialog.accept());
    await weekly.getByRole('button', { name: 'Nhập lịch từ Word', exact: true }).click();
    await weekly.waitForFunction(() => document.querySelector('[name="ScheduleRows[0].Content"]').value === 'Lịch fixture từ Word');
    assert.equal(await weekly.locator('[name="Metadata.WeeklyOriginalAssetId"]').inputValue(), wordId);
    assert.equal(await weekly.locator('#weekly-original-review').isVisible(), true);
    assert.equal(await weekly.locator('[name="Metadata.WeeklyOriginalReviewEvidence"]').inputValue(), '');
    assert.equal(await visibleRows().count(), 1); checks++;
    assert.equal(await weekly.locator('input[name=AttachmentIds]').inputValue(), wordId); checks++;
    for (const width of [320, 390, 768, 1366]) {
        await weekly.setViewportSize({ width, height: 900 });
        const dimensions = await weekly.evaluate(() => ({ viewport: innerWidth, scroll: document.documentElement.scrollWidth }));
        assert(dimensions.scroll <= dimensions.viewport + 1, `weekly overflow at ${width}: ${dimensions.scroll}`); checks++;
    }
    assert.equal(uploads, 2); checks++;
    // Reproduce an already-running document XHR completing while a video attach
    // serializes through the real editor bridge. No real media or SQL is touched.
    const race = await context.newPage(); await race.goto('http://fixture.test/admin/create');
    await race.locator('.jodit-wysiwyg').waitFor();
    await race.evaluate(() => { for (const name of ['Id', 'RowVersion', 'CurrentRevisionId']) document.querySelector('#editor-form').elements.namedItem(name).value = ''; document.querySelector('#selected-attachments').replaceChildren(); });
    await race.locator('[name=Title]').fill('Tranh chấp lưu tài liệu và video fixture');
    deferDocument = true;
    await race.locator('#editor-document-file').setInputFiles({ name: 'fixture.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 fixture') });
    for (let n = 0; n < 100 && !releaseDocument; n++) await new Promise(resolve => setTimeout(resolve, 20));
    assert.equal(typeof releaseDocument, 'function');
    await race.evaluate(() => { window.externalAttach = window.landPortalEditor.withSavedDraft(draft => new Promise(resolve => { window.attachDraft = draft; window.finishAttach = resolve; })); });
    await race.waitForFunction(() => document.querySelector('#editor-form').inert && typeof window.finishAttach === 'function');
    releaseDocument(); deferDocument = false;
    await race.waitForFunction(id => !!document.querySelector('input[name=AttachmentIds][value="' + id + '"]'), documentId);
    assert.equal(await race.locator('#editor-form').evaluate(x => x.inert), true); checks++;
    const videoId = '55555555-5555-5555-5555-555555555555', attachedRowVersion = 'YXR0YWNoZWQ=';
    const beforeFinalSave = saves.length;
    await race.evaluate(async ({ contentId, videoId, rowVersion }) => { window.finishAttach({ contentId, currentRevisionId: contentId, videoAssetId: videoId, rowVersion, state: 'Draft', savedUtc: new Date().toISOString() }); await window.externalAttach; }, { contentId, videoId, rowVersion: attachedRowVersion });
    await race.waitForFunction(() => document.querySelector('#editor-feedback').textContent.startsWith('Đã lưu lúc'));
    const finalSave = saves.slice(beforeFinalSave).find(x => x.AttachmentIds?.includes(documentId) && x.VideoAssetIds?.includes(videoId));
    assert(finalSave, 'deferred autosave must preserve both finished document and attached video');
    assert.equal(finalSave.RowVersion[0], attachedRowVersion, 'save must use the new video attachment version');
    assert.equal(await race.locator('#editor-form').evaluate(x => x.inert), false); checks++;
    await race.close();
    const review = await context.newPage(); await review.goto('http://fixture.test/admin/edit/' + contentId + '?review=true');
    await review.locator('.jodit-wysiwyg').waitFor();
    await review.locator('[data-workflow=ApprovePublish]').click();
    await review.locator('#editor-state-recovery').waitFor();
    assert.equal(await review.locator('#editor-form').getAttribute('data-state'), 'Approved');
    assert.equal(await review.locator('[data-workflow=ApprovePublish]').isVisible(), false);
    const recoveryUrl = new URL(await review.locator('#editor-state-recovery').getAttribute('href'), 'http://fixture.test');
    assert.equal(recoveryUrl.pathname, '/admin/edit/' + contentId);
    if (backLink?.startsWith('/admin?')) assert.equal(recoveryUrl.searchParams.get('returnUrl'), backLink); checks++;
    assert.deepEqual(errors, []); checks++;
    assert.deepEqual(forbiddenNetwork, []); checks++;
    console.log(`CMS editor isolated browser PASS ${checks}/${checks}; real Jodit/scripts; HTML: editor=${rendered.has('editor') ? 'MVC-rendered' : 'fixture'}, weekly=${rendered.has('weekly') ? 'MVC-rendered' : 'fixture'}; mocked endpoints; no SQL/IIS traffic.`);
} catch (error) {
    console.error('Presentation diagnostic:', { saveCount, uploads, errors, feedback: await page.locator('#editor-feedback').textContent().catch(() => '(missing)'), documentFeedback: await page.locator('#document-feedback').textContent().catch(() => '(missing)'), overflow: await page.evaluate(() => Array.from(window.document.querySelectorAll('body *')).map(x=>({element:x.tagName+'.'+x.className,right:x.getBoundingClientRect().right,width:x.getBoundingClientRect().width})).filter(x=>x.width>0&&x.right>innerWidth+1).slice(0,12)) });
    throw error;
} finally { await browser.close(); }
