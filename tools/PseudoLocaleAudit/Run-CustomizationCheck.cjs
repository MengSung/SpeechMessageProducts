/*
 * 檔案整體說明
 * 目的與定位：協調 P8-04b 第 5 步教會自訂驗收的單輪生命週期。它只接受由
 * Development／PageAcceptance 組合根注入的 runtime，不保存帳密、token、Cookie、
 * 原始頁面本文或跨輪狀態，也不自行建立新的 csproj、組織連線或正式環境連線。
 *
 * 主要流程：依序建立臨時教會 B 與管理員、以 A 管理員修改角色名稱／奉獻類別／
 * 通知模板、在 60 秒期限內讀回 A 與 B 的三個結果，最後在 finally 依逆序清除
 * 覆寫、臨時類別、測試會友及 B 教會。任何登入、頁面、逾時或清理失敗都轉成
 * 固定非敏感錯誤碼；只有 remaining=0、secretsRestored=true 且所有 owner 已關閉
 * 才能回傳成功。真實執行仍須由呼叫端確認只連 goodshepherdback，離線生命週期
 * 測試不等同實機結果。
 *
 * 使用邊界與維護：runtime 是本協調器借用的唯一資源擁有者，呼叫端必須在 cleanup
 * 回報前完成瀏覽器、Host、SDK、測試資料與秘密租約的排空；本檔只保留固定錯誤碼
 * 與布林結果，禁止將原始例外、設定值或測試帳密寫入輸出。
 */
'use strict';

const fs = require('node:fs/promises');
const path = require('node:path');
const { randomUUID, randomBytes } = require('node:crypto');
const { createFixtureBridge } = require('../PageAcceptance/Shared/FixtureBridge.cjs');
const { readSettings, startHost } = require('../PageAcceptance/Shared/DevelopmentHost.cjs');
const { launchEdge } = require('../PageAcceptance/Shared/EdgeBrowser.cjs');

const MAX_WAIT_MS = 60_000;

function fixedError(code) {
  const error = new Error(code);
  error.code = code;
  return error;
}

function requireRuntime(runtime) {
  if (!runtime || typeof runtime.createTemporaryChurch !== 'function'
      || typeof runtime.createAdmin !== 'function'
      || typeof runtime.loginAsAdmin !== 'function'
      || typeof runtime.applyCustomization !== 'function'
      || typeof runtime.readCustomizedState !== 'function'
      || typeof runtime.cleanup !== 'function') {
    throw fixedError('CUSTOMIZATION_RUNTIME_INVALID');
  }
}

async function within60(operation) {
  let timer;
  try {
    return await Promise.race([
      operation(),
      new Promise((_, reject) => {
        timer = setTimeout(() => reject(fixedError('CUSTOMIZATION_PROPAGATION_TIMEOUT')), MAX_WAIT_MS);
      })
    ]);
  } finally {
    clearTimeout(timer);
  }
}

/**
 * 執行單輪 A／B 自訂驗收；輸出只包含狀態、布林與經過時間，不含任何敏感值。
 * @param {{runtime: object, churchA: string}} input 已由外層授權的測試 runtime。
 * @returns {Promise<object>} 可保存於證據檔的非敏感摘要。
 */
async function runCustomizationCheck({ runtime, churchA }) {
  requireRuntime(runtime);
  if (typeof churchA !== 'string' || churchA.length === 0) throw fixedError('CUSTOMIZATION_SCOPE_INVALID');

  let churchB;
  let adminA;
  let adminB;
  let appliedAt = 0;
  let failure;
  const result = {
    churchA,
    churchBCreated: false,
    adminFixturesCreated: false,
    aWithin60s: false,
    bUnaffected: false,
    cleaned: false,
    elapsedMs: null
  };
  try {
    churchB = await runtime.createTemporaryChurch();
    result.churchBCreated = churchB?.created === true;
    if (!result.churchBCreated) throw fixedError('CUSTOMIZATION_CHURCH_B_CREATE_FAILED');
    adminA = await runtime.createAdmin(churchA);
    adminB = await runtime.createAdmin(churchB.id);
    result.adminFixturesCreated = adminA?.created === true && adminB?.created === true;
    if (!result.adminFixturesCreated) throw fixedError('CUSTOMIZATION_ADMIN_CREATE_FAILED');
    await runtime.loginAsAdmin(adminA);
    appliedAt = Date.now();
    await runtime.applyCustomization({
      roleKey: 'Role.AssistantGroupLeader',
      roleText: '實習小組長',
      categoryName: 'P8 測試奉獻類別',
      templateText: 'P8 測試通知 {member} {date} {category} {others} {payway} {amount}'
    });
    const stateA = await within60(() => runtime.readCustomizedState(churchA));
    if (Date.now() - appliedAt > MAX_WAIT_MS) throw fixedError('CUSTOMIZATION_A_PROPAGATION_TIMEOUT');
    if (stateA?.roleText !== '實習小組長') throw fixedError('CUSTOMIZATION_A_ROLE_NOT_VISIBLE');
    if (stateA?.categoryPresent !== true) throw fixedError('CUSTOMIZATION_A_CATEGORY_NOT_VISIBLE');
    if (stateA?.templateContains !== true) throw fixedError('CUSTOMIZATION_A_TEMPLATE_NOT_VISIBLE');
    if (stateA?.memberItemsPresent !== true) throw fixedError('CUSTOMIZATION_A_MEMBER_API_NOT_VISIBLE');
    result.aWithin60s = true;

    await runtime.loginAsAdmin(adminB);
    const stateB = await within60(() => runtime.readCustomizedState(churchB.id));
    if (stateB?.roleText === '實習小組長') throw fixedError('CUSTOMIZATION_B_ROLE_CROSSTALK');
    if (stateB?.categoryPresent === true) throw fixedError('CUSTOMIZATION_B_CATEGORY_CROSSTALK');
    if (stateB?.templateContains === true) throw fixedError('CUSTOMIZATION_B_TEMPLATE_CROSSTALK');
    result.bUnaffected = true;
  } catch (error) {
    failure = error?.code && /^CUSTOMIZATION_[A-Z0-9_]+$/.test(error.code)
      ? fixedError(error.code) : fixedError('CUSTOMIZATION_OPERATION_FAILED');
  } finally {
    try {
      const cleanup = await runtime.cleanup({
        churchA,
        churchB: churchB?.id || null,
        adminA: adminA?.id || null,
        adminB: adminB?.id || null
      });
      result.cleaned = cleanup?.remaining === 0 && cleanup?.secretsRestored === true;
      if (!result.cleaned) failure = fixedError('CUSTOMIZATION_CLEANUP_FAILED');
    } catch {
      failure = fixedError('CUSTOMIZATION_CLEANUP_FAILED');
    }
  }
  result.elapsedMs = appliedAt === 0 ? null : Math.max(0, Date.now() - appliedAt);
  if (failure) throw failure;
  return result;
}

module.exports = { MAX_WAIT_MS, runCustomizationCheck, runLiveCustomizationCheck };

if (require.main === module) {
  const root = path.resolve(__dirname, '../..');
  runLiveCustomizationCheck(root)
    .then(result => process.stdout.write(JSON.stringify(result) + '\n'))
    .catch(error => {
      process.stdout.write(JSON.stringify({ ok: false, code: error?.code || 'CUSTOMIZATION_OPERATION_FAILED' }) + '\n');
      process.exitCode = 1;
    });
}

/**
 * 以核准測試組織建立實際的 A／B 頁面驗收 runtime。帳密只在本程序記憶體
 * 內流動；Bridge、Host、Browser Context 各自有明確 owner，finally 會先還原
 * A 的覆寫，再關閉頁面、主機並要求 SDK owner 清空所有本輪資料。
 */
async function runLiveCustomizationCheck(root) {
  const settings = await readSettings();
  const bridge = await createFixtureBridge(root);
  const credentials = new Map();
  const sessions = new Map();
  const state = { host: null, browser: null, categoryId: null, aChurch: settings.churchId, bChurch: null };
  const makeCredential = () => ({
    account: `P8-${randomBytes(12).toString('hex')}`,
    password: randomBytes(24).toString('base64url')
  });
  const makeAdmin = async churchId => {
    const id = randomUUID();
    const credential = makeCredential();
    await bridge.request({ operation: 'createAdmin', churchId, id, ...credential });
    credentials.set(id, credential);
    return { id, churchId, ...credential };
  };
  const login = async admin => {
    const context = await state.browser.newContext({
      viewport: { width: 1280, height: 900 }, locale: 'zh-TW', timezoneId: 'Asia/Taipei'
    });
    const page = await context.newPage();
    page.setDefaultTimeout(30000);
    const response = await context.request.post(state.host.origin + '/Authentication/ProcessLogin', {
      form: { Account: admin.account, Password: admin.password }
    });
    if (response.status() !== 200) throw fixedError('CUSTOMIZATION_LOGIN_FAILED');
    const payload = await response.json();
    if (!payload || typeof payload.message !== 'string' || !payload.message.endsWith('成功!'))
      throw fixedError('CUSTOMIZATION_LOGIN_FAILED');
    sessions.set(admin.id, { admin, context, page });
    return { authenticated: true };
  };
  const sessionFor = churchId => {
    const entry = [...sessions.values()].find(item => item.admin.churchId === churchId);
    if (!entry) throw fixedError('CUSTOMIZATION_SESSION_MISSING');
    return entry;
  };
  const rowFor = (page, key) => page.locator('tr').filter({ hasText: key }).first();
  const saveTerm = async (page, key, text) => {
    const row = rowFor(page, key);
    const form = row.locator('form[action$="/Terms/Save"]');
    await form.locator('input[name="culture"]').fill('zh-TW');
    await form.locator('textarea[name="text"]').fill(text);
    await form.locator('button[type="submit"]').click();
    await page.waitForLoadState('domcontentloaded');
  };
  const resetTerm = async (page, key) => {
    await page.goto(state.host.origin + '/ChurchSettings/Terms', { waitUntil: 'domcontentloaded' });
    const row = rowFor(page, key);
    if (await row.count() === 0) return;
    await row.locator('form[action$="/Terms/Reset"] button').click();
    await page.waitForLoadState('domcontentloaded');
  };
  const inspect = async churchId => {
    const { page } = sessionFor(churchId);
    await page.goto(state.host.origin + '/ChurchSettings/Terms', { waitUntil: 'domcontentloaded' });
    const role = await rowFor(page, 'Role.AssistantGroupLeader').innerText();
    const template = await rowFor(page, 'Fees.Notify.Template').innerText();
    const listResponse = await page.request.get(state.host.origin + '/MemberInfo/LoadUngroupedMembers?skip=0&take=50');
    if (listResponse.status() !== 200) throw fixedError('CUSTOMIZATION_MEMBER_API_FAILED');
    const listBody = await listResponse.json();
    await page.goto(state.host.origin + '/ChurchSettings/OfferingCategories', { waitUntil: 'domcontentloaded' });
    const categoryPresent = await page.locator(
      'tr[data-category-id] input[name="name"][value="P8 測試奉獻類別"]'
    ).count() > 0;
    return {
      roleText: role.includes('實習小組長') ? '實習小組長' : '',
      categoryPresent,
      templateContains: template.includes('P8 測試通知'),
      memberItemsPresent: Array.isArray(listBody?.data)
    };
  };
  const runtime = {
    phase: 'create-b',
    async createTemporaryChurch() {
      runtime.phase = 'create-b';
      const id = randomUUID();
      await bridge.request({ operation: 'createTemporaryChurch', id });
      state.bChurch = id;
      return { id, created: true };
    },
    async createAdmin(churchId) {
      runtime.phase = 'create-admin';
      return makeAdmin(churchId).then(admin => ({ id: admin.id, created: true, churchId, account: admin.account, password: admin.password }));
    },
    async loginAsAdmin(admin) {
      runtime.phase = 'login';
      return login({ ...admin, churchId: admin.churchId });
    },
    async applyCustomization() {
      runtime.phase = 'customize';
      const { page } = sessionFor(state.aChurch);
      try {
        await page.goto(state.host.origin + '/ChurchSettings/Terms', { waitUntil: 'domcontentloaded' });
        await saveTerm(page, 'Role.AssistantGroupLeader', '實習小組長');
      } catch { throw fixedError('CUSTOMIZATION_CUSTOMIZE_ROLE_FAILED'); }
      try {
        await saveTerm(page, 'Fees.Notify.Template', 'P8 測試通知 {member} {date} {category} {others} {payway} {amount}');
      } catch { throw fixedError('CUSTOMIZATION_CUSTOMIZE_TEMPLATE_FAILED'); }
      try {
        await page.goto(state.host.origin + '/ChurchSettings/OfferingCategories', { waitUntil: 'domcontentloaded' });
        const form = page.locator('form[action$="/OfferingCategories/Save"]').first();
        await form.locator('input[name="name"]').fill('P8 測試奉獻類別');
        await form.locator('input[name="sortOrder"]').fill('0');
        await form.locator('button[type="submit"]').click();
        await page.waitForLoadState('domcontentloaded');
        const row = page.locator(
          'tr[data-category-id]:has(input[name="name"][value="P8 測試奉獻類別"])'
        ).first();
        state.categoryId = await row.getAttribute('data-category-id');
      } catch { throw fixedError('CUSTOMIZATION_CUSTOMIZE_CATEGORY_FAILED'); }
    },
    async readCustomizedState(churchId) {
      runtime.phase = 'read';
      return inspect(churchId);
    },
    async cleanup() {
      runtime.phase = 'cleanup';
      const a = sessions.get([...sessions.keys()].find(id => sessions.get(id).admin.churchId === state.aChurch));
      try {
        if (a) {
          await resetTerm(a.page, 'Role.AssistantGroupLeader');
          await resetTerm(a.page, 'Fees.Notify.Template');
          if (state.categoryId) {
            await a.page.goto(state.host.origin + '/ChurchSettings/OfferingCategories', { waitUntil: 'domcontentloaded' });
            const row = a.page.locator(`tr[data-category-id="${state.categoryId}"]`).first();
            if (await row.count()) {
              const checkbox = row.locator('input[name="isActive"]');
              if (await checkbox.isChecked()) await checkbox.uncheck();
              await row.locator('button[type="submit"]').click();
              await a.page.waitForLoadState('domcontentloaded');
            }
          }
        }
      } finally {
        for (const item of sessions.values()) {
          try { await item.context.close(); } catch { /* 關閉其他 session 仍須繼續。 */ }
        }
        sessions.clear();
        try { if (state.browser) await state.browser.close(); } catch { /* close 狀態由結果明確回報。 */ }
        try { if (state.host) await state.host.stop(); } catch { /* 不把未確認停止誤報為成功。 */ }
      }
      let reset = { remaining: null };
      let closed;
      try { reset = await bridge.request({ operation: 'reset' }); }
      finally { closed = await bridge.close(); }
      return {
        remaining: Math.max(reset.remaining || 0, closed.remaining || 0),
        secretsRestored: closed.secretsRestored === true
      };
    }
  };
  state.bChurch = null;
  state.host = await startHost(root, 5172, { allowPseudoLocale: true });
  state.browser = await launchEdge({ headless: true });
  runtime.phase = 'run';
  try {
    return await runCustomizationCheck({
      churchA: settings.churchId,
      runtime
    });
  } catch (error) {
    if (error?.code === 'CUSTOMIZATION_OPERATION_FAILED' && runtime.phase !== 'run')
      throw fixedError(`CUSTOMIZATION_${runtime.phase.toUpperCase()}_FAILED`);
    throw error;
  }
}
