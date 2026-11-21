/*
 * 檔案整體說明
 * 目的與定位：階段6單次頁面前後對照的Node入口，按06A固定四情境驅動本機Edge，
 * 將真正頁面原有的POST送到同一個瀏覽器session，保留每個JSON欄位及截圖供比對。
 * 主要功能與流程：runBrowserRound接收當輪SDK建立的合成fixture及本機站址；依序開
 * 課程、主日底線、主日at、小組QR，再以當次隨機帳密登入週報頁並收集載入回應。
 * normalize只替換明確ID映射及明列時間欄位，其他文字、欄位與數值原樣比較。
 * 使用邊界與維護：此模組不讀既有會員帳密、不保存token／cookie／登入帳密至檔案。
 * Playwright只從repo外已安裝路徑載入，不下載瀏覽器。每輪browser/context由呼叫
 * 擁有，以巢狀finally確保context關閉失敗時browser仍關閉；fixture及Host子程序由上層單一owner負責建立、停止及逆序清理。
 * 本檔不代表已完成實機；只有兩輪回應、寫入讀回、推播及殘留全檢查後才可驗收。
 * 故障只回固定代碼，禁止把Playwright原始錯誤、request body或秘密輸出到終端。
 */
'use strict';
const path = require('node:path');
const fs = require('node:fs/promises');
const { isDeepStrictEqual } = require('node:util');
const { launchEdge } = require('../Shared/EdgeBrowser.cjs');

/** 只接受localhost測試站，禁止腳本被用來操作正式站或任意遠端網址。 */
function requireLocalOrigin(value) {
  const url = new URL(value);
  if (url.protocol !== 'http:' || !['localhost', '127.0.0.1'].includes(url.hostname)
      || url.username || url.password || url.pathname !== '/') throw new Error('P6_LOCAL_ORIGIN_REQUIRED');
  return url.origin;
}

/** SDK與回應的明確ID代號及時間欄位正規化；不任意抹掉文字、排序陣列或刪欄位。 */
function normalize(value, identities, timeFields = new Set(), key = '') {
  if (value === null || value === undefined) return value;
  if (timeFields.has(key)) return { written: value !== '' && value !== null };
  if (Array.isArray(value)) return value.map(item => normalize(item, identities, timeFields));
  if (typeof value === 'object') return Object.fromEntries(Object.entries(value)
    .map(([name, item]) => [name, normalize(item, identities, timeFields, name)]));
  if (typeof value !== 'string') return value;
  let text = value;
  // result/onboardtype本來就是「時間＋結果文字」；只替換明確時間片段，仍逐字比較簽到／錯誤等本文。
  if (key === 'result' || key === 'onboardtype') text = text.replace(
    /\d{4}\/\d{1,2}\/\d{1,2}\s+(?:上午|下午)\s*\d{1,2}:\d{2}:\d{2}/g, '<time-written>');
  for (const [id, alias] of identities) text = text.replaceAll(id, alias);
  return text;
}

/** 純值逐欄比較；差異包含完整結構，呼叫端僅保存已移除秘密且合成的結果。 */
function compareRounds(before, after) {
  return { equal: isDeepStrictEqual(before, after), before, after };
}

/** 單輪依原路由與表單欄位送出請求；登入帳密只用於當次POST，任何回應不得含帳密。 */
async function runBrowserRound({ origin, fixture, outputDirectory, label, skipLogin = false }) {
  origin = requireLocalOrigin(origin);
  if (!['before', 'after'].includes(label)) throw new Error('P6_INVALID_ROUND');
  const browser = await launchEdge({ headless: true });
  let context;
  try {
    context = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'zh-TW', timezoneId: 'Asia/Taipei' });
    const page = await context.newPage();
    page.setDefaultTimeout(30000);
    // 真LINE登入不能在桌面Edge重現；保留原頁面未登入行為，簽到用同session原POST。
    // 不替換LIFF程式、不模擬登入、不攔截或偽造產品JSON。
    const cases = [
      ['lesson', '/QrCodeView', '/QrCode/QrCodeGetLineId', fixture.lessonQr],
      ['meeting-underscore', '/SundayQrCodeView', '/QrCode/SundayQrCodeGetLineId', fixture.meetingUnderscoreQr],
      ['meeting-at', '/SundayQrCodeView', '/QrCode/SundayQrCodeGetLineId', fixture.meetingAtQr],
      ['small-group', '/SmallGroupQrCodeView', '/QrCode/SmallGroupQrCodeGetLineId', fixture.smallGroupQr]
    ];
    await fs.mkdir(outputDirectory, { recursive: true });
    const results = [];
    for (const [name, view, action, qr] of cases) {
      const url = new URL(view, origin);
      url.searchParams.set('QrCodeId', qr);
      const response = await page.goto(url.href, { waitUntil: 'domcontentloaded' });
      if (!response || response.status() !== 200) throw new Error('P6_QR_PAGE_FAILED');
      await page.screenshot({ path: path.join(outputDirectory, `${label}-${name}.png`), fullPage: true });
      const post = await context.request.post(origin + action, { form: {
        DisplayName: fixture.displayName, UserLineId: fixture.lineUserId, GroupId: '', RoomId: '', ViewType: 'user'
      } });
      if (post.status() !== 200) throw new Error('P6_QR_POST_FAILED');
      let body;
      try { body = await post.json(); } catch { throw new Error('P6_QR_JSON_REQUIRED'); }
      if (!body || typeof body !== 'object' || typeof body.result !== 'string'
          || body.result.startsWith('錯誤')) throw new Error('P6_QR_BUSINESS_FAILURE');
      if (fixture.account && JSON.stringify(body).includes(fixture.account)) throw new Error('P6_RESPONSE_ACCOUNT_LEAK');
      if (fixture.password && JSON.stringify(body).includes(fixture.password)) throw new Error('P6_RESPONSE_PASSWORD_LEAK');
      results.push({ name, status: post.status(), body });
    }
    let weekly = { skipped: skipLogin };
    if (!skipLogin) {
      const response = await context.request.post(origin + '/Authentication/ProcessLogin', {
        form: { Account: fixture.account, Password: fixture.password }
      });
      if (response.status() !== 200) throw new Error('P6_LOGIN_HTTP_FAILED');
      const payload = await response.json();
      if (typeof payload.message !== 'string' || !payload.message.endsWith('成功!')
          || typeof payload.DisplayViewType !== 'string' || typeof payload.ActiveListId !== 'string')
        throw new Error('P6_LOGIN_REJECTED');
      const loaded = [];
      const pending = new Set();
      const onResponse = response => {
        if (!response.url().startsWith(origin + '/SmallGroup/')) return;
        if (!(response.headers()['content-type'] || '').includes('json')) return;
        const task = response.json().then(body => loaded.push({
          path: new URL(response.url()).pathname, status: response.status(), body
        }));
        pending.add(task);
        task.then(() => pending.delete(task), () => pending.delete(task));
      };
      page.on('response', onResponse);
      try {
        const view = await page.goto(origin + '/SmallGroup/IntegrateView/' + encodeURIComponent(payload.ActiveListId), { waitUntil: 'networkidle' });
        if (!view || view.status() !== 200 || page.url().includes('/Login')) throw new Error('P6_WEEKLY_PAGE_FAILED');
        await Promise.all([...pending]);
        const text = await page.locator('body').innerText();
        if (text.includes(fixture.password) || text.includes(fixture.account)) throw new Error('P6_PAGE_CREDENTIAL_LEAK');
        await page.screenshot({ path: path.join(outputDirectory, `${label}-weekly.png`), fullPage: true });
        weekly = { skipped: false, login: payload, text, loaded };
      } finally { page.off('response', onResponse); await Promise.allSettled([...pending]); }
    }
    return { qr: results, weekly };
  } finally {
    // Context關閉本身失敗時仍須由browser owner關閉程序；不能讓第一個例外略過最終釋放。
    try { if (context) await context.close(); }
    finally { await browser.close(); }
  }
}

module.exports = { requireLocalOrigin, normalize, compareRounds, runBrowserRound };

if (require.main === module) require('./P6AcceptanceOwner.cjs').runAcceptance(path.resolve(__dirname, '../../..')).catch(() => {
  console.log('P6_ACCEPTANCE_PREPARATION_FAILED'); process.exitCode = 1;
});
