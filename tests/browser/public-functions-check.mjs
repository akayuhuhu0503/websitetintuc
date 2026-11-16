import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';

// Use the existing Playwright installation; never connect to an operational URL.
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const run = path.resolve(process.argv[2]);
assert.match(run, /[\\/]\.local[\\/]tests[\\/]public-functions-LPPublic_[a-f0-9]{12}$/);
const fixture = JSON.parse(await fs.readFile(path.join(run, 'ready.json'), 'utf8'));
assert.match(fixture.url, /^http:\/\/127\.0\.0\.1:\d+$/);
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const checks = [];
const check = (ok, name) => { assert.ok(ok, name); checks.push(name); console.log('PASS ' + name); };
try {
    const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    const requests = []; page.on('request', r => requests.push(r.url()));
    for (const width of [1920, 1366, 768, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        await page.goto(fixture.url, { waitUntil: 'networkidle' });
        check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${width}px no horizontal overflow`);
        const nav = page.locator('[data-portal-navigation]');
        const toggle = nav.locator('.mobile-menu-toggle');
        const compact = await toggle.isVisible();
        if (compact) await toggle.click();
        const news = nav.locator('.nav-dropdown').filter({ has: page.locator('summary', { hasText: /^Tin tức$/ }) }).first();
        const summary = news.locator(':scope > summary');
        if (compact) await summary.click(); else await summary.hover();
        await page.waitForFunction(() => [...document.querySelectorAll('.nav-dropdown')].some(x => x.open && x.querySelector('summary').textContent === 'Tin tức'));
        const panel = news.locator(':scope > .nav-panel');
        check(await panel.isVisible(), `${width}px News opens by ${compact ? 'touch/click' : 'hover'}`);
        const box = await panel.boundingBox();
        check(box && box.x >= -1 && box.x + box.width <= width + 1, `${width}px dropdown remains inside viewport`);
        check(await panel.locator('a[href^="/chuyen-muc/"]').count() === 21, `${width}px full visible catalog including empty categories`);
        check(await panel.locator('img, video').count() === 0 && await panel.locator('a[href^="/bai-viet/"]').count() === 0, `${width}px no articles or images in catalog`);
        if (!compact) check((await panel.locator('.site-menu-grid').evaluate(x => getComputedStyle(x).gridTemplateColumns)).split(' ').length >= 2, `${width}px multiple columns`);
        await summary.focus(); await page.keyboard.press('ArrowDown');
        check(await panel.locator('a').first().evaluate(x => x === document.activeElement), `${width}px keyboard enters menu`);
        await page.keyboard.press('Escape');
        check(await news.evaluate(x => !x.open) && await summary.evaluate(x => x === document.activeElement), `${width}px Escape closes and restores focus`);
        // Open/close by keyboard independently of hover.
        await page.keyboard.press('Enter');
        await panel.waitFor({ state: 'visible' });
        await panel.locator('a[href="/chuyen-muc/chuyen-muc-18"]').click();
        await page.waitForURL('**/chuyen-muc/chuyen-muc-18');
        check(await page.locator('h1').textContent() === 'Chuyên mục 18', `${width}px empty category link resolves correctly`);
    }
    await page.setViewportSize({ width: 1366, height: 900 }); requests.length = 0;
    await page.goto(fixture.url, { waitUntil: 'networkidle' });
    check(!requests.some(x => x.includes(`/video-media/${fixture.videoId}/play`) || x.includes('/tracks?')), 'No MP4 or captions fetched before Play');
    const slot = page.locator('[data-home-video]').first();
    const before = page.url();
    await slot.locator('button').focus(); await page.keyboard.press('Enter');
    const player = slot.locator('video');
    await page.waitForFunction(() => { const v = document.querySelector('[data-home-video] video'); return v && v.currentTime > 0 && v.readyState >= 2; });
    check(page.url() === before, 'Keyboard Play stays on homepage and real MP4 advances');
    check(await player.evaluate(v => v.controls && v.playsInline && v.preload === 'none'), 'Native playback controls, inline playback and lazy initialization');
    check(await player.evaluate(v => Math.abs(v.getBoundingClientRect().width / v.getBoundingClientRect().height - 16 / 9) < 0.02), 'Active player keeps 16:9 ratio');
    await page.waitForFunction(() => document.querySelector('[data-home-video] video track') !== null);
    check(await player.locator('track').count() === 1, 'Published eligible subtitle is available in player');
    await player.evaluate(v => { v.pause(); v.currentTime = 1; });
    await page.waitForFunction(() => Math.abs(document.querySelector('[data-home-video] video').currentTime - 1) < 0.2);
    check(page.url() === before, 'Pause and seek do not navigate');
    await page.locator('.home-video-copy h3 a').first().click();
    await page.waitForURL(`**/bai-viet/${fixture.contentId}`);
    check(await page.locator('h1').textContent() === 'Video kiểm tra phát tại trang chủ', 'Independent title opens correct published article');
    await page.route(`**/video-media/${fixture.videoId}/play`, route => route.fulfill({ status: 404, body: '' }));
    await page.goto(fixture.url, { waitUntil: 'networkidle' });
    await page.locator('[data-home-video] button').first().click();
    await page.locator('.home-video-error:not([hidden])').first().waitFor();
    check(page.url().replace(/\/$/, '') === fixture.url.replace(/\/$/, ''), 'Denied media displays inline error without navigating');
    await page.goto(fixture.url + '/tai-lieu', { waitUntil: 'networkidle' });
    check(await page.locator('.material-card').count() === 1, 'Materials layout contains only selected published document');
    check(errors.length === 0, 'No JavaScript exceptions across focused browser checks');
    await fs.writeFile(path.join(run, 'browser-result.json'), JSON.stringify({ passed: checks.length, checks, screenshots: false }, null, 2));
    await fs.writeFile(path.join(run, 'browser.done'), 'PASS');
} finally { await browser.close(); }
