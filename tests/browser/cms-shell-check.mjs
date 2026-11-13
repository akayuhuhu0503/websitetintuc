// Isolated browser checks for CMS shell, picker and bulk UX. No SQL or IIS traffic.
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import assert from 'node:assert/strict';
const { chromium } = await import(process.env.CMS_PLAYWRIGHT_MODULE || 'playwright');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const read = p => readFile(resolve(root, p), 'utf8');
const css = (await Promise.all(['site', 'interactions', 'admin-editor', 'site-config', 'admin-theme'].map(x => read('src/Web/wwwroot/css/' + x + '.css')))).join('\n');
const shell = await read('src/Web/wwwroot/js/admin-shell.js');
const site = await read('src/Web/wwwroot/js/site-admin.js');
const batch = await read('src/Web/wwwroot/js/interaction-batch.js');
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage(); const errors = []; page.on('pageerror', e => errors.push(e.message));
let checks = 0;
const fixture = `<style>${css}</style><body class="admin-body"><header class="admin-header"><a class="admin-brand">QLĐĐ</a><button class="cms-menu-toggle" aria-controls="cms-sidebar" aria-expanded="false">Menu</button><div class="admin-account">Tài khoản thử</div></header><div class="admin-shell"><aside class="admin-sidebar" id="cms-sidebar"><p class="sidebar-label">NỘI DUNG</p><a href="/admin" data-admin-nav="articles">Bài viết</a><a data-admin-nav="site" href="/admin/site?section=menus">Cấu hình</a><a data-admin-nav="banners" href="/admin/site?section=banners">Banner</a></aside><main class="admin-main"><form id="site-edit-form" class="site-admin-form"><label for="site_label">Tên</label><input id="site_label" value="Tên đang sửa"><label for="site_targetKind">Liên kết</label><select id="site_targetKind"><option>Route</option><option>Content</option><option>External</option></select><div data-target-kind="Route"><input id="route_value" value="home"></div><div data-target-kind="Content"><select id="site_content"><option value="">Chọn</option></select><button type="button" data-picker-target="site_content" data-picker-type="content">Tìm bài</button></div><div data-target-kind="Internal External"><input id="external_value" value="https://example.org/"></div><select id="Banner_MediaAssetId" class="site-image-select"><option value="">Chọn ảnh</option></select><button type="button" data-picker-target="Banner_MediaAssetId" data-picker-type="image">Chọn từ thư viện ảnh</button><div id="site-image-slot"></div><label for="banner_start">Bắt đầu</label><input type="datetime-local" id="banner_start" value="2026-11-13T09:00"><select id="site_position"><option value="home_event_strip">Dải sự kiện</option></select><p id="event-banner-crop-warning" hidden>Cảnh báo cắt ảnh</p><details class="cms-advanced"><summary>Nâng cao</summary><input id="required-alt" required value="Mô tả"></details></form><img id="site-image-preview" hidden alt="Xem trước"><div class="table-scroll"><table class="admin-table"><tr><td>Nội dung kiểm tra bảng rộng bên trong vùng cuộn</td><td>Chuyên mục</td><td>Trạng thái</td><td>Thao tác</td></tr></table></div><div class="editor-layout"><section class="editor-main"><div class="editor-document-upload"><input type="file"><button>Đính kèm tài liệu</button></div></section><aside class="editor-settings"><div class="editor-inline-schedule"><input type="datetime-local"><button>Hẹn giờ</button></div></aside></div><form id="batch-form" hidden><p data-batch-count></p><select id="batch-action"><option value="reject">Từ chối</option></select><textarea id="batch-reason"></textarea><button>Áp dụng</button></form><div data-batch-row data-id="a"><input type="checkbox" data-select-record><details data-batch-editor hidden><summary>Biên tập bản công khai</summary></details></div><div id="batch-results"></div></main></div></body>`;
await page.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.hostname !== 'fixture.test') return route.abort();
    if (url.pathname === '/admin/site/picker') return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ page: Number(url.searchParams.get('page')), hasMore: url.searchParams.get('page') === '1', items: [{ value: '11111111-1111-1111-1111-111111111111', label: '<script>không chạy</script> Ảnh thử', ...(url.searchParams.get('type') === 'image' ? { preview: '/admin/site/media/11111111-1111-1111-1111-111111111111' } : {}) }] }) });
    if (url.pathname.startsWith('/admin/site/media/')) return route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="1170" height="100"><rect width="100%" height="100%" fill="#8B2E2E"/></svg>' });
    return route.fulfill({ contentType: 'text/html', body: fixture });
});
try {
    await page.goto('http://fixture.test/admin/site/edit?section=banners');
    await page.addScriptTag({ content: shell }); await page.addScriptTag({ content: site }); await page.addScriptTag({ content: batch });
    assert.equal(await page.locator('[data-admin-nav=banners]').getAttribute('aria-current'), 'page'); checks++;
    assert.equal(await page.locator('[data-target-kind=Content]').isVisible(), false); checks++;
    await page.locator('#site_targetKind').selectOption('External');
    assert.equal(await page.locator('#external_value').isVisible(), true); assert.equal(await page.locator('#route_value').inputValue(), 'home'); checks++;
    await page.locator('#site_label').fill('Tên mới phải giữ');
    await page.locator('[data-picker-type=image]').click();
    await page.locator('.cms-picker-item').waitFor();
    await page.locator('#cms-picker-query').fill('ảnh'); await page.locator('.cms-picker-search button').click();
    await page.locator('[data-picker-next]').click(); await page.waitForFunction(() => document.querySelector('[data-picker-page]').textContent === 'Trang 2'); checks++;
    await page.locator('.cms-picker-item').click();
    assert.equal(await page.locator('#site_label').inputValue(), 'Tên mới phải giữ'); assert.equal(await page.locator('#banner_start').inputValue(), '2026-11-13T09:00'); assert.equal(await page.locator('#external_value').inputValue(), 'https://example.org/'); checks++;
    assert.equal(await page.locator('#Banner_MediaAssetId').inputValue(), '11111111-1111-1111-1111-111111111111'); assert.equal(await page.locator('dialog').isVisible(), false); checks++;
    assert.equal(await page.locator('#batch-form').isVisible(), false); await page.locator('[data-select-record]').check(); assert.equal(await page.locator('#batch-form').isVisible(), true); assert.equal(await page.locator('[data-batch-editor]').isVisible(), true); checks++;
    await page.locator('[data-select-record]').uncheck(); assert.equal(await page.locator('#batch-form').isVisible(), false); checks++;
    for (const width of [320, 390, 768, 1366]) {
        await page.setViewportSize({ width, height: 900 });
        const size = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, viewport: innerWidth }));
        assert(size.scroll <= size.viewport + 1, `page overflow at ${width}: ${size.scroll}`); checks++;
        if (width <= 800) { assert.equal(await page.locator('#cms-sidebar').isVisible(), false); await page.locator('.cms-menu-toggle').click(); assert.equal(await page.locator('#cms-sidebar').isVisible(), true); await page.keyboard.press('Escape'); assert.equal(await page.locator('#cms-sidebar').isVisible(), false); checks++; }
        await page.locator('[data-picker-type=image]').click(); await page.locator('.cms-picker-item').waitFor();
        assert(await page.locator('dialog').evaluate(x => x.getBoundingClientRect().right <= innerWidth && x.getBoundingClientRect().left >= 0), `dialog overflow ${width}`); await page.keyboard.press('Escape'); checks++;
    }
    assert.deepEqual(errors, []); checks++;
    console.log(`CMS shell/picker isolated browser PASS ${checks}/${checks}; no SQL/IIS traffic.`);
} finally { await browser.close(); }
