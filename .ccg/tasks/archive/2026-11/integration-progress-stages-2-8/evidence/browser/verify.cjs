// 本次頁面驗收工具：使用全新無登入資料的瀏覽器，只讀本機進度頁，驗證後確實關閉。
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const assert = require('node:assert/strict');

(async () => {
  const out = __dirname;
  const browser = await chromium.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: true
  });
  const results = [];
  try {
    for (const [name, width, height, colorScheme] of [
      ['desktop', 1440, 1000, 'light'],
      ['mobile', 390, 844, 'light'],
      ['narrow-dark', 320, 800, 'dark']
    ]) {
      const context = await browser.newContext({ viewport: { width, height }, colorScheme });
      const result = { name, width, height, colorScheme, passed: false };
      results.push(result);
      try {
        await context.route(/^https?:/, route => route.abort());
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(pathToFileURL(path.resolve('文件/規劃設計書/實作計畫-3.3/實作進度.html')).href);
        await page.waitForSelector('.planned-phase');
        assert.equal(await page.locator('.planned-phase').count(), 7);
        assert.equal(await page.locator('.planned-wp.is-todo').count(), 53);
        const summary = page.locator('.planned-phase > summary').first();
        await summary.focus();
        await page.keyboard.press('Enter');
        assert.equal(await page.locator('.planned-phase').first().getAttribute('open'), '');
        assert.equal(await page.locator('.planned-phase-toggle').first().evaluate(
          el => getComputedStyle(el, '::before').content), '"-"');
        await page.keyboard.press('Space');
        assert.equal(await page.locator('.planned-phase').first().getAttribute('open'), null);
        assert.equal(await page.locator('.planned-phase-toggle').first().evaluate(
          el => getComputedStyle(el, '::before').content), '"+"');
        for (const item of await page.locator('.planned-phase > summary').all()) await item.click();
        result.layout = await page.locator('#planned-phases').evaluate(root => ({
          viewport: innerWidth,
          documentWidth: document.documentElement.scrollWidth,
          overflow: [...root.querySelectorAll('.planned-wp, .details, .planned-phase > summary')]
            .filter(el => el.scrollWidth > el.clientWidth + 1)
            .map(el => ({ className: el.className, width: el.clientWidth, scrollWidth: el.scrollWidth })),
          counts: [...root.querySelectorAll('.planned-phase-count')].map(el => el.textContent.trim())
        }));
        await page.locator('#planned-phases').evaluate(
          element => window.scrollTo(0, element.getBoundingClientRect().top + window.scrollY - 55));
        await page.screenshot({ path: path.join(out, name + '.png') });
        assert.deepEqual(errors, []);
        assert.deepEqual(result.layout.overflow, []);
        assert.ok(result.layout.documentWidth <= width + 1, JSON.stringify(result.layout));
        result.keyboard = 'Enter/Space passed';
        result.pageErrors = errors.length;
        result.passed = true;
      } catch (error) {
        result.error = error.message;
        throw error;
      } finally {
        await context.close();
      }
    }
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify(results, null, 2) + '\r\n');
  }
  console.log(JSON.stringify(results, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });
