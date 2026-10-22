// Solve the real rendered arithmetic question. No server bypass or fixed Test answer.
export async function solveLoginCaptcha(page) {
  const text = await page.locator('#captcha-question').innerText();
  const values = text.match(/\d+/g)?.map(Number);
  if (values?.length !== 2 || !/cộng|trừ/.test(text)) throw new Error('Actual login CAPTCHA missing or unrecognized');
  await page.locator('[name=CaptchaAnswer]').fill(String(text.includes('cộng') ? values[0] + values[1] : values[0] - values[1]));
}
export async function solveReauthCaptcha(page) {
  await page.waitForFunction(() => /\d+.*(cộng|trừ).*\d+/.test(document.querySelector('#reauth-captcha-question')?.textContent ?? ''));
  const text = await page.locator('#reauth-captcha-question').innerText();
  const values = text.match(/\d+/g)?.map(Number);
  if (values?.length !== 2) throw new Error('Actual reauthentication CAPTCHA missing');
  await page.locator('#reauth-captcha-answer').fill(String(text.includes('cộng') ? values[0] + values[1] : values[0] - values[1]));
}
