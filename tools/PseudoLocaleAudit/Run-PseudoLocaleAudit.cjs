/*
 * 檔案整體說明
 * 目的與定位：執行 P8-04a 固定四案例的 Development ChurchReport／Edge 偽語系頁面驗收。
 * 主要流程：以 Shared owner 讀取核准測試識別、建立 D34 驗證過的 SDK fixture bridge、暫借 User Secrets 測試 token、建立一次性管理員會友並啟動 localhost Host；登入只存在管理員瀏覽器 Context，匿名 403 案使用另一個全新 Context。逐頁只將文字節點的 CJK 包覆計數及實際裁切計數傳回，頁面原文只短暫保留在量測程序記憶體，不寫報告；截圖遮罩可變資料欄位。
 * 輸入輸出：工作樹根目錄固定取自工具位置，不接受命令列 URL、組織或登入值。輸出目錄只含固定路徑、HTTP 狀態、數量、資源清理狀態及遮罩截圖；錯誤只輸出固定代碼。
 * 資源與隔離：本次呼叫是 Browser、Context、Host、SDK fixture 與秘密租約的唯一 owner；finally 依序關 Context／Browser、Host，再由 bridge 逆序刪除本輪資料及還原 User Secrets。所有等待有界；任何清理狀態未知即非零結束，不宣稱零殘留。
 * 使用邊界與驗證：只允許 localhost ChurchReport Development 與部署白名單 goodshepherdback，絕不連正式組織。離線規則與 lifecycle 測試不等同實機通過；只有結果檔顯示四頁完成、未包覆中文與裁切均為零，且清理成功，才可驗收。
 */
'use strict';

const fs = require('node:fs/promises');
const path = require('node:path');
const { randomBytes } = require('node:crypto');
const { PAGE_CASES, runPseudoAudit } = require('./PseudoAuditLifecycle.cjs');
const { countUnwrappedChinese, isClippedText } = require('./PseudoAuditRules.cjs');
const { readSettings, startHost } = require('../PageAcceptance/Shared/DevelopmentHost.cjs');
const { createFixtureBridge } = require('../PageAcceptance/Shared/FixtureBridge.cjs');
const { launchEdge } = require('../PageAcceptance/Shared/EdgeBrowser.cjs');
const { createRoundFixture } = require('../PageAcceptance/P6/P6Fixtures.cjs');

function fixedFailure(code) {
  const error = new Error(code);
  error.code = code;
  return error;
}

function safeTimestamp() {
  return new Date().toISOString().replaceAll(':', '-');
}

async function createRuntime(root) {
  let settings;
  let credentials;
  let bridge;
  let host;
  let browser;
  let adminContext;
  let adminPage;
  const contexts = [];
  let fixture;
  let secretsPrepared = false;
  let pseudoLocalePrepared = false;
  let origin = 'http://localhost:5168';
  const observedResults = [];
  const failures = [];
  const preflight = {
    adminFixtureVerified: false,
    adminJobTitleMatches: false,
    adminCultureMatches: false,
    adminChurchMatches: false,
    authenticationCookiePresent: false
  };
  const evidenceRoot = path.join(root, 'artifacts', 'p8', 'pseudo-audit-' + safeTimestamp());
  await fs.mkdir(evidenceRoot, { recursive: true });

  async function timed(operation, timeoutMs = 30000) {
    let timer;
    try {
      return await Promise.race([
        operation(),
        new Promise((_, reject) => {
          timer = setTimeout(() => reject(fixedFailure('PSEUDO_AUDIT_PAGE_TIMEOUT')), timeoutMs);
        })
      ]);
    } finally {
      clearTimeout(timer);
    }
  }

  return {
    diagnostics() { return { ...preflight }; },
    async login() {
      try {
        settings = await readSettings();
        credentials = {
          account: 'P8-Audit-' + randomBytes(12).toString('hex'),
          password: randomBytes(24).toString('base64url')
        };
        bridge = await createFixtureBridge(root);
        const lease = await bridge.request({ operation: 'prepareSecrets' });
        if (lease.backupProtected !== true || lease.backupVerified !== true) throw fixedFailure('PSEUDO_AUDIT_SECRET_LEASE_FAILED');
        secretsPrepared = true;
        const pseudo = await bridge.request({ operation: 'leasePseudoLocale' });
        if (pseudo.enabled !== true) throw fixedFailure('PSEUDO_AUDIT_CULTURE_LEASE_FAILED');
        pseudoLocalePrepared = true;
        fixture = await createRoundFixture(bridge, settings, credentials, {
          churchJobTitle: '牧師傳道',
          preferredLanguage: 'qps-ploc'
        });
        const adminRecord = await bridge.request({
          operation: 'retrieve',
          entity: 'contact',
          id: fixture.ids.login,
          fields: ['new_church_jobtitle', 'new_preferredlanguage', 'parentcustomerid']
        });
        preflight.adminJobTitleMatches = adminRecord.fields.new_church_jobtitle === '牧師傳道';
        preflight.adminCultureMatches = adminRecord.fields.new_preferredlanguage === 'qps-ploc';
        preflight.adminChurchMatches = adminRecord.fields.parentcustomerid?.id === settings.churchId;
        if (!preflight.adminJobTitleMatches || !preflight.adminCultureMatches || !preflight.adminChurchMatches)
          throw fixedFailure('PSEUDO_AUDIT_ADMIN_FIXTURE_INVALID');
        preflight.adminFixtureVerified = true;
        host = await startHost(root, 5168, { allowPseudoLocale: true });
        origin = host.origin;
        browser = await launchEdge({ headless: true });
        adminContext = await browser.newContext({
          viewport: { width: 1440, height: 1000 },
          locale: 'qps-ploc',
          timezoneId: 'Asia/Taipei'
        });
        contexts.push(adminContext);
        adminPage = await adminContext.newPage();
        adminPage.setDefaultTimeout(20000);
        const login = await timed(() => adminContext.request.post(host.origin + '/Authentication/ProcessLogin', {
          form: { Account: credentials.account, Password: credentials.password }
        }));
        if (!login.ok()) throw fixedFailure('PSEUDO_AUDIT_LOGIN_FAILED');
        let payload;
        try { payload = await login.json(); } catch { throw fixedFailure('PSEUDO_AUDIT_LOGIN_FAILED'); }
        if (typeof payload?.message !== 'string' || !payload.message.endsWith('成功!'))
          throw fixedFailure('PSEUDO_AUDIT_LOGIN_FAILED');
        const authenticationCookies = await adminContext.cookies(host.origin);
        if (!authenticationCookies.some(cookie => cookie.name === '.ChurchReport.Auth'))
          throw fixedFailure('PSEUDO_AUDIT_AUTH_COOKIE_MISSING');
        preflight.authenticationCookiePresent = true;
        return { authenticated: true, ...preflight };
      } catch (error) {
        if (error?.code?.startsWith('PSEUDO_AUDIT_')) throw error;
        throw fixedFailure('PSEUDO_AUDIT_LOGIN_FAILED');
      }
    },

    async auditPage(pageCase) {
      let phase = 'navigation';
      let observedStatus = null;
      try {
        let page = adminPage;
        if (pageCase.access === 'non-admin') {
          const context = await browser.newContext({
            viewport: { width: 1440, height: 1000 },
            locale: 'qps-ploc',
            timezoneId: 'Asia/Taipei'
          });
          contexts.push(context);
          page = await context.newPage();
          page.setDefaultTimeout(20000);
        }
        const url = new URL(pageCase.path, origin);
        const response = await timed(() => page.goto(url.href, { waitUntil: 'domcontentloaded' }));
        if (!response) throw fixedFailure('PSEUDO_AUDIT_PAGE_FAILED');
        observedStatus = response.status();
        phase = 'authorization';
        if (pageCase.access === 'non-admin') {
          if (response.status() !== 403 || response.headers().location)
            throw fixedFailure('PSEUDO_AUDIT_FORBIDDEN_CONTRACT_FAILED');
          if (await page.locator('h1').count() === 0) throw fixedFailure('PSEUDO_AUDIT_FORBIDDEN_CONTRACT_FAILED');
        } else if (response.status() !== 200 || !page.url().startsWith(origin + pageCase.path)
            || await page.locator('h1').count() === 0) {
          throw fixedFailure(response.status() === 403
            ? 'PSEUDO_AUDIT_ADMIN_FORBIDDEN'
            : response.status() === 200 ? 'PSEUDO_AUDIT_PAGE_CONTENT_INVALID' : 'PSEUDO_AUDIT_ADMIN_HTTP_FAILURE');
        }

        phase = 'measure';
        const nodes = await timed(() => page.locator('body').evaluate(body => {
          const walker = document.createTreeWalker(body, NodeFilter.SHOW_TEXT);
          const collected = [];
          for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const text = node.nodeValue || '';
            if (!text.trim()) continue;
            const element = node.parentElement;
            if (!element) continue;
            const style = getComputedStyle(element);
            if (style.display === 'none' || style.visibility === 'hidden' || element.getClientRects().length === 0) continue;
            const box = element;
            collected.push({
              text,
              metrics: {
                scrollWidth: box.scrollWidth,
                clientWidth: box.clientWidth,
                scrollHeight: box.scrollHeight,
                clientHeight: box.clientHeight,
                overflowX: style.overflowX,
                overflowY: style.overflowY,
                textOverflow: style.textOverflow
              }
            });
          }
          return collected;
        }));
        let unwrappedChinese = 0;
        let clippedText = 0;
        for (const node of nodes) {
          unwrappedChinese += countUnwrappedChinese(node.text);
          if (isClippedText(node.metrics)) clippedText++;
          node.text = '';
        }
        nodes.length = 0;

        const evidence = `${pageCase.id}.png`;
        phase = 'screenshot';
        const mask = ['header', 'nav', '.navbar', '.user-menu',
          ...(pageCase.id === 'forbidden' ? [] : ['table td', 'input', 'textarea'])];
        await page.screenshot({
          path: path.join(evidenceRoot, evidence),
          fullPage: true,
          mask: mask.map(selector => page.locator(selector))
        });
        const result = { unwrappedChinese, clippedText, status: response.status(), evidence };
        observedResults.push({ id: pageCase.id, path: pageCase.path, access: pageCase.access, ...result });
        return result;
      } catch (error) {
        failures.push({ id: pageCase.id, phase, status: observedStatus,
          code: error?.code && /^PSEUDO_AUDIT_[A-Z0-9_]+$/.test(error.code)
            ? error.code : 'PSEUDO_AUDIT_PAGE_FAILED' });
        if (error?.code === 'PSEUDO_AUDIT_PAGE_TIMEOUT') throw error;
        if (error?.code === 'PSEUDO_AUDIT_FORBIDDEN_CONTRACT_FAILED') throw error;
        if (error?.code === 'PSEUDO_AUDIT_ADMIN_FORBIDDEN'
            || error?.code === 'PSEUDO_AUDIT_ADMIN_HTTP_FAILURE'
            || error?.code === 'PSEUDO_AUDIT_PAGE_CONTENT_INVALID') throw error;
        throw fixedFailure(phase === 'navigation' ? 'PSEUDO_AUDIT_NAVIGATION_FAILED'
          : phase === 'measure' ? 'PSEUDO_AUDIT_MEASUREMENT_FAILED'
            : phase === 'screenshot' ? 'PSEUDO_AUDIT_SCREENSHOT_FAILED' : 'PSEUDO_AUDIT_PAGE_FAILED');
      }
    },

    async cleanup() {
      let failed = false;
      for (const context of contexts.reverse()) {
        try { await context.close(); } catch { failed = true; }
      }
      contexts.length = 0;
      if (browser) {
        try { await browser.close(); } catch { failed = true; }
        browser = undefined;
      }
      if (host) {
        try { await host.stop(); } catch { failed = true; }
        host = undefined;
      }
      let bridgeResult = { cleanup: true, remaining: 0, secretsRestored: true, pseudoLocaleRestored: true };
      if (bridge) {
        if (pseudoLocalePrepared) {
          try {
            const restored = await bridge.request({ operation: 'restorePseudoLocale' });
            if (restored.restored !== true) failed = true;
          } catch { failed = true; }
        }
        try { bridgeResult = await bridge.close(); } catch { failed = true; }
        bridge = undefined;
      }
      credentials = undefined;
      if (settings) {
        settings.lineUserId = '';
        settings.testToken = '';
        settings = undefined;
      }
      fixture = undefined;
      const cleanup = {
        remaining: bridgeResult?.remaining ?? null,
        secretsRestored: bridgeResult?.secretsRestored === true,
        hostStopped: !host,
        browserClosed: !browser,
        fixtureOwnerClosed: bridgeResult?.cleanup === true,
        secretsLeasePrepared: secretsPrepared,
        pseudoLocaleRestored: bridgeResult?.pseudoLocaleRestored === true,
        evidenceDirectory: path.relative(root, evidenceRoot).replaceAll(path.sep, '/')
      };
      try {
        await fs.writeFile(path.join(evidenceRoot, 'result.json'), JSON.stringify({
          generatedAt: new Date().toISOString(),
          organization: 'goodshepherdback',
          origin,
          results: observedResults,
          failures,
          preflight,
          unwrappedChinese: observedResults.reduce((sum, item) => sum + item.unwrappedChinese, 0),
          clippedText: observedResults.reduce((sum, item) => sum + item.clippedText, 0),
          cleanup: { ...cleanup, cleanupVerified: !failed }
        }, null, 2), 'utf8');
      } catch { failed = true; }
      if (failed || cleanup.remaining !== 0 || !cleanup.secretsRestored
          || !cleanup.hostStopped || !cleanup.browserClosed || !cleanup.fixtureOwnerClosed
          || !cleanup.pseudoLocaleRestored)
        throw fixedFailure('PSEUDO_AUDIT_CLEANUP_FAILED');
      return { remaining: 0, secretsRestored: true, ...cleanup };
    }
  };
}

async function main() {
  const root = path.resolve(__dirname, '../..');
  let runtime;
  try {
    const mode = process.argv[2];
    if (mode && mode !== '--preflight-only') throw fixedFailure('PSEUDO_AUDIT_ARGUMENT_REJECTED');
    runtime = await createRuntime(root);
    if (mode === '--preflight-only') {
      let login;
      let cleanup;
      let failure;
      try { login = await runtime.login(); }
      catch (error) { failure = error?.code || 'PSEUDO_AUDIT_LOGIN_FAILED'; }
      try { cleanup = await runtime.cleanup(); }
      catch { failure = 'PSEUDO_AUDIT_CLEANUP_FAILED'; }
      process.stdout.write(JSON.stringify({ mode, login: login || null, cleanup: cleanup || null,
        diagnostics: runtime.diagnostics(), failure: failure || null }) + '\n');
      if (failure || !cleanup) process.exitCode = 1;
      return;
    }
    const result = await runPseudoAudit({ pages: PAGE_CASES, runtime });
    const output = {
      pages: result.pages,
      unwrappedChinese: result.unwrappedChinese,
      clippedText: result.clippedText,
      cleanup: result.cleanup
    };
    process.stdout.write(JSON.stringify(output) + '\n');
    if (result.unwrappedChinese !== 0 || result.clippedText !== 0) process.exitCode = 1;
  } catch (error) {
    process.stdout.write((error?.code && /^PSEUDO_AUDIT_[A-Z_]+$/.test(error.code)
      ? error.code : 'PSEUDO_AUDIT_OPERATION_FAILED') + '\n');
    process.exitCode = 1;
  }
}

if (require.main === module) main();
module.exports = { createRuntime };
