import fs from 'node:fs/promises';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';
// Existing Playwright installation can be supplied; do not install a browser or
// read operational configuration. Requires the isolated test host's ready.json.
const [run,playwrightModule]=process.argv.slice(2);
const {chromium}=await import(playwrightModule?pathToFileURL(path.resolve(playwrightModule)).href:'playwright');
const ready=JSON.parse(await fs.readFile(path.join(run,'ready.json'),'utf8'));
assert.equal(new URL(ready.url).hostname,'127.0.0.1');
const browser=await chromium.launch({channel:'msedge',headless:true});const checks=[];
try {
  const context=await browser.newContext({extraHTTPHeaders:{'X-Fixture-Actor':'admin'}});
  const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  for(const width of [1366,768,390,320]){
    await page.setViewportSize({width,height:900});
    const response=await page.goto(ready.url+'/admin/source-sync');assert.equal(response.status(),200);
    await page.locator('h1').waitFor();
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
    checks.push('Admin responsive '+width);
    await page.locator('summary').filter({hasText:'Ánh xạ chuyên mục'}).click();
    assert.equal(await page.locator('.source-sync-mapping').count(),15);
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
    checks.push('Category mappings fit '+width);
  }
  await page.locator('#sync-filter input[name=q]').fill('does-not-exist');
  const filtered=page.waitForResponse(r=>r.url().includes('fragment=true'));
  await page.locator('#sync-filter button').click();await filtered;
  await page.getByText('Không có bài trong danh sách này. Đồng bộ không tự công bố nội dung.').waitFor();
  checks.push('Search updates fragment without page reload');
  assert.deepEqual(errors,[]);checks.push('No JavaScript errors');
  await fs.writeFile(path.join(run,'browser-result.json'),JSON.stringify({passed:checks.length,checks}));
  await fs.writeFile(path.join(run,'browser.done'),'PASS');console.log(JSON.stringify({passed:checks.length,checks}));
}finally{await browser.close();}
