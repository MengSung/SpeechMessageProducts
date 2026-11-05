// 僅使用獨立無登入狀態的 headless 瀏覽器，封鎖外部網路；所有 context 與 browser 均確定關閉。
const { chromium } = require('C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const root = 'D:/音訊科技產品/系統平台/SpeechMessageProducts/.worktrees/1.0.0.10.IntegrateWebhook_SDK.worktree/.worktrees/1.0.0.11.Integration.P1.Worktree';
const pagePath = path.join(root, '文件/規劃設計書/實作計畫-3.3/實作進度.html');
const html = fs.readFileSync(pagePath, 'utf8');
const start = html.indexOf('const DATA = {');
const end = html.indexOf('\n};', start) + 3;
const data = vm.runInNewContext(html.slice(start, end) + '\nDATA;');
function synthetic(alert, notices) {
  return html.slice(0, start) + 'const DATA = ' +
    JSON.stringify({ ...data, alert, notices }) + ';' + html.slice(end);
}

(async () => {
  const results = [];
  const browser = await chromium.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: true
  });
  try {
    for (const colorScheme of ['light', 'dark']) {
      for (const width of [1440, 390, 320]) {
        const context = await browser.newContext({ viewport: { width, height: 950 }, colorScheme });
        const result = { width, colorScheme };
        results.push(result);
        try {
          await context.route(/^https?:/, route => route.abort());
          const page = await context.newPage();
          const errors = [];
          page.on('pageerror', error => errors.push(error.message));
          await page.goto(pathToFileURL(pagePath).href);
          const info = page.locator('#alert > details.notice.info');
          assert.equal(await info.count(), 1);
          assert.equal(await info.getAttribute('open'), null);
          assert.equal(await page.locator('#notices').isVisible(), false);
          assert.equal(await page.locator('#alert [role="alert"]').count(), 0);
          assert.equal(await page.locator('#p1-wps .wp.is-done').count(), 3);
          assert.equal(await page.locator('#p1-wps .wp.is-active').count(), 1);
          await page.screenshot({ path: path.join(__dirname, `real-${width}-${colorScheme}.png`) });
          const summary = info.locator(':scope > summary');
          assert.ok(await summary.evaluate(el => el.getBoundingClientRect().height >= 44));
          await summary.focus();
          await page.keyboard.press('Enter');
          assert.equal(await info.getAttribute('open'), '');
          assert.equal(await info.locator('.notice-content').isVisible(), true);
          assert.equal(await summary.locator('.notice-toggle').evaluate(el => getComputedStyle(el, '::before').content), '"− 收合"');
          await page.keyboard.press('Space');
          assert.equal(await info.getAttribute('open'), null);
          await summary.click();
          assert.equal(await info.getAttribute('open'), '');
          await summary.click();
          assert.equal(await info.getAttribute('open'), null);

          await page.goto('about:blank');
          await page.setContent(synthetic(data.alert, [
            { level: 'warn', title: '待確認：測試證據尚未補齊', body: '<check> 詳細紀錄', steps: ['可重跑後補證據'] },
            { level: 'error', title: '建置阻擋：請修正失敗項目', body: '真正失敗詳細原因' },
            { level: '" onclick="attack', title: '<unknown>', body: 'x'.repeat(150) }
          ]));
          const warn = page.locator('#notices > article.warn');
          const error = page.locator('#notices > article.error');
          assert.equal(await warn.getAttribute('role'), 'status');
          assert.equal(await error.getAttribute('role'), 'alert');
          assert.equal(await warn.locator('.notice-heading').isVisible(), true);
          assert.equal(await error.locator('.notice-heading').isVisible(), true);
          assert.equal(await warn.locator('.notice-content').isVisible(), false);
          assert.equal(await page.locator('#notices details[open]').count(), 0);
          assert.equal(await page.locator('#notices [onclick]').count(), 0);
          assert.equal(await page.locator('#notices > details.info .notice-heading').innerText(), '一般資訊\n<unknown>');
          result.colors = await page.locator('#notices').evaluate(el => [...el.children].map(child => ({
            className: child.className,
            border: getComputedStyle(child).borderLeftColor,
            background: getComputedStyle(child).backgroundColor
          })));
          assert.equal(result.colors[0].border, 'rgb(250, 178, 25)');
          assert.equal(result.colors[1].border, 'rgb(208, 59, 59)');
          assert.notEqual(result.colors[2].background, result.colors[1].background);
          await page.screenshot({ path: path.join(__dirname, `severities-${width}-${colorScheme}.png`) });
          await warn.locator('summary').click();
          assert.equal(await warn.locator('.notice-content').isVisible(), true);
          assert.equal(await warn.locator('.notice-content p').innerText(), '<check> 詳細紀錄');
          assert.equal(await warn.locator('.notice-content li').innerText(), '可重跑後補證據');
          await page.locator('#notices > details.info > summary').click();
          result.documentWidth = await page.evaluate(() => document.documentElement.scrollWidth);
          assert.equal(result.documentWidth, width);

          // 同時驗證正常 Clipboard API、回退成功／失敗／拋錯，暫存 textarea 必須全部移除。
          for (const mode of ['clipboard', 'fallback', 'false', 'throw']) {
            await page.goto('about:blank');
            await page.setContent(synthetic({ title: '複製測試', prompt: '<synthetic prompt>' }, []));
            await page.evaluate(mode => {
              window.copiedPrompt = '';
              Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
                writeText: async text => {
                  if (mode !== 'clipboard') throw new Error('denied');
                  window.copiedPrompt = text;
                }
              } });
              document.execCommand = () => {
                if (mode === 'throw') throw new Error('command failed');
                window.copiedPrompt = document.querySelector('textarea').value;
                return mode === 'fallback';
              };
            }, mode);
            await page.locator('#alert > details > summary').click();
            await page.locator('#copyBtn').click();
            await page.waitForFunction(() => document.querySelector('#copied').textContent.length > 0);
            assert.equal(await page.locator('textarea').count(), 0);
            const message = await page.locator('#copied').innerText();
            if (['clipboard', 'fallback'].includes(mode)) {
              assert.match(message, /已複製/);
              assert.equal(await page.evaluate(() => window.copiedPrompt), '<synthetic prompt>');
            } else {
              assert.match(message, /手動選取/);
            }
          }
          assert.deepEqual(errors, []);
          result.pageErrors = errors.length;
          result.keyboardAndPointer = 'passed';
          result.clipboardPaths = '4 passed';
          result.passed = true;
        } catch (error) {
          result.error = error.stack;
          throw error;
        } finally {
          await context.close();
        }
      }
    }
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(__dirname, 'browser-result.json'), JSON.stringify(results, null, 2) + '\r\n');
  }
  console.log(JSON.stringify(results, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });
