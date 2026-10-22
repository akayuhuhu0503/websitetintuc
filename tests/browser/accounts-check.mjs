import { solveLoginCaptcha } from './login-captcha.mjs';
import { chromium } from 'playwright';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const fixture = JSON.parse(await readFile(resolve(root, '.local/tests/phase2b/accounts-ui.json'), 'utf8'));
if (fixture.database !== 'LandPortal_Test' || new URL(fixture.baseUrl).hostname !== '127.0.0.1') throw new Error('Account browser requires isolated SQL Test localhost.');
const evidence = resolve(process.env.LandPortal__EvidenceRoot || resolve(root, '.local/evidence'), 'phase2b/accounts/ui'); await mkdir(evidence, { recursive: true });
const report = { utc: new Date().toISOString(), database: fixture.database, pageChecks: [], actions: [], pageErrors: [], offOriginRequests: [], limitations: ['Headless Edge viewport emulation; no physical mobile/UAT/WCAG certification.'] };
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const administrator = await browser.newContext(); const temporary = await browser.newContext();
for (const context of [administrator, temporary]) await context.route('**/*', async route => { const url = new URL(route.request().url()); if (url.origin !== new URL(fixture.baseUrl).origin) { report.offOriginRequests.push(url.origin); await route.abort(); } else await route.continue(); });
const page = await administrator.newPage(); const temp = await temporary.newPage();
for (const current of [page, temp]) current.on('pageerror', error => report.pageErrors.push(error.message));
function assert(value, message) { if (!value) throw new Error(message); }
async function login(current, user) { await current.goto(fixture.baseUrl + '/admin/login'); await current.locator('[name=UserName]').fill(user); await current.locator('[name=Password]').fill(fixture.password); await solveLoginCaptcha(current); await Promise.all([current.waitForNavigation(), current.getByRole('button', { name: 'Đăng nhập', exact: true }).click()]); }
async function submit(current, name) { const response = current.waitForResponse(r => r.request().method() === 'POST'); await current.getByRole('button', { name, exact: true }).click(); const posted = await response; assert(posted.status() === 302, 'Account form refused HTTP ' + posted.status()); await current.waitForLoadState('networkidle'); }
async function screenshot(name, width, path) {
  await page.setViewportSize({ width, height: 900 }); const response = await page.goto(fixture.baseUrl + path, { waitUntil: 'networkidle' });
  const layout = await page.evaluate(() => ({ viewport: innerWidth, scrollWidth: document.documentElement.scrollWidth, heading: document.querySelector('h1')?.textContent.trim(), unlabelled: [...document.querySelectorAll('input:not([type=hidden]),textarea,select')].filter(x => x.getClientRects().length && !x.labels?.length && !x.getAttribute('aria-label')).map(x => x.name || x.id) }));
  const pass = response?.status() === 200 && layout.scrollWidth <= width + 1 && layout.unlabelled.length === 0;
  report.pageChecks.push({ name, width, path, status: response?.status(), pass, ...layout });
  await page.screenshot({ path: resolve(evidence, `${name}-${width}.png`), fullPage: true });
  assert(pass, `${name} at ${width}px failed accessibility/reflow check.`);
}
try {
  await login(page, fixture.admin);
  const routes = [['users', '/admin/users'], ['user-create', '/admin/users/create'], ['user-detail', '/admin/users/' + fixture.tempId], ['roles', '/admin/roles'], ['role-edit', '/admin/roles/' + fixture.roleId], ['password', '/admin/account/change-password']];
  for (const width of [390, 1440]) for (const [name, path] of routes) await screenshot(name, width, path);
  for (const width of [360, 768, 1024]) for (const [name, path] of routes.filter(x => ['users', 'roles'].includes(x[0]))) await screenshot(name, width, path);
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.goto(fixture.baseUrl + '/admin/roles/create');
  const groupName = 'QA UI nhóm biên tập ' + Date.now(); await page.locator('[name=Name]').fill(groupName);
  const permission = page.locator('input[type=checkbox][value="content.edit.own"]'); await permission.focus(); assert(await permission.evaluate(x => x === document.activeElement), 'Catalog permission cannot receive keyboard focus.'); await page.keyboard.press('Space');
  await submit(page, 'Lưu nhóm quyền'); assert((await page.locator('[name=Name]').inputValue()) === groupName, 'Saved role UI did not reload its name.');
  const roleId = new URL(page.url()).pathname.split('/').at(-1); report.actions.push({ name: 'Keyboard selects catalog permission and posts role to SQL Test', pass: true, roleId });
  await page.goto(fixture.baseUrl + '/admin/users/create'); const userName = 'QA-2B-UI-Temp-' + Date.now();
  await page.locator('[name=UserName]').fill(userName); await page.locator('[name=Email]').fill(userName + '@example.invalid'); await page.locator('[name=DisplayName]').fill('QA UI người biên tập'); await page.locator('[name=TemporaryPassword]').fill(fixture.password);
  await submit(page, 'Tạo tài khoản'); const userId = new URL(page.url()).pathname.split('/').at(-1); assert(await page.getByRole('heading', { name: userName, exact: true }).count() === 1, 'New account did not open real detail.');
  await page.locator(`input[type=checkbox][name=RoleIds][value="${roleId}"]`).check(); await submit(page, 'Áp dụng nhóm quyền và thu hồi phiên');
  report.actions.push({ name: 'Create internal account and explicitly assign a delegated role via UI', pass: true, userId });
  await login(temp, userName); assert(new URL(temp.url()).pathname === '/admin/account/change-password', 'New UI-created account bypassed mandatory change.');
  await temp.locator('[name=CurrentPassword]').fill(fixture.password); await temp.locator('[name=NewPassword]').fill(fixture.newPassword); await temp.locator('[name=ConfirmPassword]').fill(fixture.newPassword);
  await submit(temp, 'Đổi mật khẩu'); assert(new URL(temp.url()).pathname === '/admin', 'Changed-password editor did not receive allowed admin landing.'); report.actions.push({ name: 'Temporary account changes its own password before editor access', pass: true });
  await page.goto(fixture.baseUrl + '/admin/users/' + userId); await page.locator('[name=DisplayName]').fill('QA UI thông tin đã cập nhật'); await submit(page, 'Lưu thông tin'); assert(await page.locator('[name=DisplayName]').inputValue() === 'QA UI thông tin đã cập nhật', 'Profile form success did not persist.'); report.actions.push({ name: 'Profile editor saves and independently reloads SQL-backed form', pass: true });
  assert(report.pageErrors.length === 0 && report.offOriginRequests.length === 0, 'Browser emitted errors or off-origin requests.');
} catch (error) { report.failure = error.message; process.exitCode = 1; }
finally { await browser.close(); await writeFile(resolve(evidence, 'ui-report.json'), JSON.stringify(report, null, 2)); console.log(JSON.stringify({ pages: report.pageChecks.length, passingPages: report.pageChecks.filter(x => x.pass).length, actions: report.actions.length, failures: report.failure ? 1 : 0, errors: report.pageErrors.length })); }
