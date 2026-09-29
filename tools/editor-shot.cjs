// Screenshots the running editor's two pages headless (dt edit --screenshot). Needs Playwright
// (npm i -g playwright) and a Chromium it can find; the cloud sessions have both. CommonJS so that
// NODE_PATH finds a global install.
const { chromium } = require('playwright');

(async () => {
  const [url, out] = process.argv.slice(2);
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1280, height: 860 } });
  page.on('pageerror', e => { console.error('page error:', e.message); process.exitCode = 1; });
  await page.goto(url);
  await page.waitForSelector('.files button.on');
  await page.waitForSelector('.row');
  await page.screenshot({ path: out.replace(/\.png$/, '-tuning.png') });
  await page.click('#tab-routes');
  await page.waitForSelector('#features tr');
  await page.screenshot({ path: out.replace(/\.png$/, '-routes.png'), fullPage: true });
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
