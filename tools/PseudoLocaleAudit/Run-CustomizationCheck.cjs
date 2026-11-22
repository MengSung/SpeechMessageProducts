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
const ROLE_KEY = 'Role.GroupLeader';
const ROLE_CODE = 'Role.GroupLeader';
const ROLE_D365_VALUE = '小組長';
const ROLE_OVERRIDE_TEXT = '小組帶領者';
const TEST_CATEGORY_PREFIX = 'P8 測試奉獻類別 ';
const TEST_TEMPLATE_TEXT =
  'P8 測試通知 {member} {date} {category} {others} {payway} {amount}';

function isPriorP8TestOverride(key, text) {
  return (key === ROLE_KEY && text === ROLE_OVERRIDE_TEXT)
    || (key === 'Role.AssistantGroupLeader' && text === '實習小組長')
    || (key === 'Fees.Notify.Template' && text === TEST_TEMPLATE_TEXT);
}

function getTermRestoreActions(override, testCulture = 'zh-TW') {
  if (!override || typeof override.culture !== 'string' || typeof override.text !== 'string')
    throw fixedError('CUSTOMIZATION_BASELINE_INVALID');
  if (typeof testCulture !== 'string' || testCulture.length === 0)
    throw fixedError('CUSTOMIZATION_BASELINE_INVALID');
  if (override.culture === testCulture) {
    return [override.text.length === 0
      ? { operation: 'reset', culture: testCulture }
      : { operation: 'save', culture: testCulture, text: override.text }];
  }
  const actions = [{ operation: 'reset', culture: testCulture }];
  if (override.text.length > 0)
    actions.push({ operation: 'save', culture: override.culture, text: override.text });
  return actions;
}

function assistantRoleRemainsUnmapped(items) {
  if (!Array.isArray(items)
      || !items.every(item => item && typeof item.value === 'string'
        && typeof item.text === 'string' && typeof item.code === 'string')
      || items.some(item => item.code === 'Role.AssistantGroupLeader'))
    return false;
  const internLeaderItems = items.filter(item => item.value === '實習小組長');
  return internLeaderItems.length === 1
    && internLeaderItems[0].text === '實習小組長'
    && internLeaderItems[0].code === '';
}

function mappedRoleItemMatchesOverride(items, code, d365Value, overrideText) {
  if (!Array.isArray(items)) return false;
  const matches = items.filter(item => item?.code === code);
  return matches.length === 1
    && matches[0].value === d365Value
    && matches[0].text === overrideText;
}

async function submitTermChange({ page, origin, key, operation, culture, text }) {
  if (!['save', 'reset'].includes(operation))
    throw fixedError('CUSTOMIZATION_TERM_OPERATION_INVALID');
  await page.goto(origin + '/ChurchSettings/Terms', { waitUntil: 'domcontentloaded' });
  const row = page.locator('tr').filter({ hasText: key }).first();
  if (await row.count() === 0) return false;
  const form = row.locator(`form[action$="/Terms/${operation === 'save' ? 'Save' : 'Reset'}"]`);
  if (culture !== undefined && operation === 'save')
    await form.locator('input[name="culture"]').fill(culture);
  if (culture !== undefined && operation === 'reset')
    await form.locator('input[name="culture"]').evaluate((input, value) => {
      input.value = value;
    }, culture);
  let submit;
  if (operation === 'save') {
    await form.locator('textarea[name="text"]').fill(text);
    submit = form.locator('button[type="submit"]');
  } else {
    submit = form.locator('button');
  }
  const responsePromise = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && response.url().split('?')[0].endsWith(
      `/ChurchSettings/Terms/${operation === 'save' ? 'Save' : 'Reset'}`));
  await submit.click({ noWaitAfter: true });
  const response = await responsePromise;
  if (response.status() >= 400)
    throw fixedError(`CUSTOMIZATION_TERM_${operation.toUpperCase()}_HTTP_${response.status()}`);
  return true;
}

async function deactivateCategoryRow({ page, origin, id }) {
  if (!/^[0-9a-f-]{36}$/i.test(id || ''))
    throw fixedError('CUSTOMIZATION_CATEGORY_ID_INVALID');
  const url = origin + '/ChurchSettings/OfferingCategories';
  let phase = 'LOAD_LIST';
  try {
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    phase = 'FIND_ROW';
    let row = page.locator(`tr[data-category-id="${id}"]`).first();
    if (await row.count() === 0) throw fixedError('CUSTOMIZATION_CATEGORY_ROW_MISSING');
    let checkbox = row.locator('input[name="isActive"]').first();
    if (await checkbox.isChecked()) {
      phase = 'WAIT_SUBMIT_RESPONSE';
      const responsePromise = page.waitForResponse(response =>
        response.request().method() === 'POST'
        && response.url().split('?')[0].endsWith('/ChurchSettings/OfferingCategories/Save'));
      phase = 'UNCHECK';
      await checkbox.uncheck();
      phase = 'SUBMIT';
      await row.locator('button[type="submit"]').click({ noWaitAfter: true });
      phase = 'AWAIT_SUBMIT_RESPONSE';
      const response = await responsePromise;
      if (response && response.status() >= 400)
        throw fixedError(`CUSTOMIZATION_CATEGORY_SAVE_HTTP_${response.status()}`);
    }

    phase = 'RELOAD_LIST';
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    phase = 'VERIFY_ROW';
    row = page.locator(`tr[data-category-id="${id}"]`).first();
    if (await row.count() === 0) throw fixedError('CUSTOMIZATION_CATEGORY_ROW_MISSING');
    checkbox = row.locator('input[name="isActive"]').first();
    if (await checkbox.isChecked())
      throw fixedError('CUSTOMIZATION_CATEGORY_DEACTIVATION_NOT_VISIBLE');
    return true;
  } catch (error) {
    if (/^CUSTOMIZATION_[A-Z0-9_]+$/.test(error?.code || '')) throw error;
    const suffix = error?.name === 'TimeoutError' ? 'TIMEOUT' : 'FAILED';
    throw fixedError(`CUSTOMIZATION_CATEGORY_${phase}_${suffix}`);
  }
}

async function runCleanupStages(stages) {
  const failedStages = [];
  const failedCodes = [];
  for (const stage of stages) {
    try {
      await stage.run();
    } catch (error) {
      failedStages.push(stage.name);
      failedCodes.push({
        stage: stage.name,
        code: typeof error?.code === 'string'
            && /^CUSTOMIZATION_[A-Z0-9_]+$/.test(error.code)
          ? error.code
          : 'CUSTOMIZATION_CLEANUP_STAGE_FAILED'
      });
    }
  }
  return { failedStages, failedCodes };
}

function allRestorationChecksPassed(checks) {
  return Object.entries(checks)
    .filter(([key]) => key !== 'noChangesStarted')
    .every(([, value]) => value === true);
}

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
  const operationChecks = {};
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
      roleKey: ROLE_KEY,
      roleText: ROLE_OVERRIDE_TEXT,
      categoryName: runtime.categoryName || 'P8 測試奉獻類別',
      templateText: TEST_TEMPLATE_TEXT
    });
    const stateA = await within60(() => runtime.readCustomizedState(churchA));
    if (Date.now() - appliedAt > MAX_WAIT_MS) throw fixedError('CUSTOMIZATION_A_PROPAGATION_TIMEOUT');
    operationChecks.aRolePreviewMatches = stateA?.rolePreview === ROLE_OVERRIDE_TEXT;
    operationChecks.aRoleOverrideVisible = stateA?.roleOverride?.text === ROLE_OVERRIDE_TEXT;
    operationChecks.aMappedRoleOverrideMatches = stateA?.mappedRoleOverrideMatches === true;
    operationChecks.aAssistantRoleUnmapped = stateA?.assistantRoleUnmapped === true;
    operationChecks.aCategoryPresent = stateA?.categoryPresent === true;
    operationChecks.aCategoryActive = stateA?.categoryActive === true;
    operationChecks.aTemplateContains = stateA?.templateContains === true;
    if (stateA?.rolePreview !== ROLE_OVERRIDE_TEXT
        || stateA?.mappedRoleOverrideMatches !== true)
      throw fixedError('CUSTOMIZATION_A_ROLE_NOT_VISIBLE');
    if (stateA?.assistantRoleUnmapped !== true)
      throw fixedError('CUSTOMIZATION_A_MEMBER_ITEMS_CHANGED');
    if (stateA?.categoryPresent !== true || stateA?.categoryActive !== true)
      throw fixedError('CUSTOMIZATION_A_CATEGORY_NOT_VISIBLE');
    if (stateA?.templateContains !== true) throw fixedError('CUSTOMIZATION_A_TEMPLATE_NOT_VISIBLE');
    result.aWithin60s = true;

    await runtime.loginAsAdmin(adminB);
    const stateB = await within60(() => runtime.readCustomizedState(churchB.id));
    operationChecks.bRoleUnchanged = stateB?.rolePreview !== ROLE_OVERRIDE_TEXT
      && stateB?.roleValue === ROLE_D365_VALUE && stateB?.roleText !== ROLE_OVERRIDE_TEXT;
    operationChecks.bAssistantRoleUnmapped = stateB?.assistantRoleUnmapped === true;
    operationChecks.bCategoryAbsent = stateB?.categoryPresent !== true;
    operationChecks.bTemplateUnchanged = stateB?.templateContains !== true;
    if (stateB?.rolePreview === ROLE_OVERRIDE_TEXT
        || stateB?.roleValue !== ROLE_D365_VALUE
        || stateB?.roleText === ROLE_OVERRIDE_TEXT)
      throw fixedError('CUSTOMIZATION_B_ROLE_CROSSTALK');
    if (stateB?.assistantRoleUnmapped !== true)
      throw fixedError('CUSTOMIZATION_B_MEMBER_ITEMS_CROSSTALK');
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
      result.cleaned = cleanup?.remaining === 0
        && cleanup?.secretsRestored === true
        && cleanup?.stateRestored === true
        && cleanup?.ownersClosed === true;
      if (!result.cleaned) {
        const operationCode = failure?.code || null;
        failure = fixedError('CUSTOMIZATION_CLEANUP_FAILED');
        failure.cleanup = {
          operationCode,
          stage: cleanup?.stage || 'unknown',
          remaining: Number.isInteger(cleanup?.remaining) ? cleanup.remaining : null,
          secretsRestored: cleanup?.secretsRestored === true,
          stateRestored: cleanup?.stateRestored === true,
          ownersClosed: cleanup?.ownersClosed === true,
          failedStages: cleanup?.failedStages,
          checks: cleanup?.checks || undefined,
          operationChecks
        };
      }
    } catch {
      failure = fixedError('CUSTOMIZATION_CLEANUP_FAILED');
    }
    if (failure) failure.operationChecks = operationChecks;
  }
  result.elapsedMs = appliedAt === 0 ? null : Math.max(0, Date.now() - appliedAt);
  if (failure) throw failure;
  return result;
}

module.exports = {
  MAX_WAIT_MS,
  allRestorationChecksPassed,
  assistantRoleRemainsUnmapped,
  deactivateCategoryRow,
  getTermRestoreActions,
  isPriorP8TestOverride,
  mappedRoleItemMatchesOverride,
  runCleanupStages,
  runCustomizationCheck,
  runLiveCustomizationCheck,
  submitTermChange
};

if (require.main === module) {
  const root = path.resolve(__dirname, '../..');
  runLiveCustomizationCheck(root)
    .then(result => process.stdout.write(JSON.stringify(result) + '\n'))
    .catch(error => {
      process.stdout.write(JSON.stringify({
        ok: false,
        code: error?.code || 'CUSTOMIZATION_OPERATION_FAILED',
        cleanup: error?.cleanup,
        operationChecks: error?.operationChecks
      }) + '\n');
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
  const baselineA = { value: null };
  const categoryName = `P8 測試奉獻類別 ${randomUUID().slice(0, 8)}`;
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
  const saveTerm = (page, key, text, culture) => submitTermChange({
    page, origin: state.host.origin, key, operation: 'save', culture, text
  });
  const resetTerm = (page, key, culture) => submitTermChange({
    page, origin: state.host.origin, key, operation: 'reset', culture
  });
  const readTermOverride = async (page, key) => {
    const row = rowFor(page, key);
    const form = row.locator('form[action$="/Terms/Save"]');
    return {
      culture: await form.locator('input[name="culture"]').inputValue(),
      text: await form.locator('textarea[name="text"]').inputValue()
    };
  };
  const findCreatedCategoryId = async page => {
    const row = page.locator(
      `tr[data-category-id]:has(input[name="name"][value="${categoryName}"])`
    ).first();
    return await row.count() === 0 ? null : row.getAttribute('data-category-id');
  };
  const clearPriorTestOverrides = async page => {
    for (const [key, testText] of [
      [ROLE_KEY, ROLE_OVERRIDE_TEXT],
      ['Role.AssistantGroupLeader', '實習小組長'],
      ['Fees.Notify.Template', TEST_TEMPLATE_TEXT]
    ]) {
      await page.goto(state.host.origin + '/ChurchSettings/Terms', { waitUntil: 'domcontentloaded' });
      const current = await readTermOverride(page, key);
      if (isPriorP8TestOverride(key, current.text) && current.culture === 'zh-TW')
        await resetTerm(page, key, 'zh-TW');
      if (isPriorP8TestOverride(key, testText) && current.culture !== 'zh-TW')
        throw fixedError('CUSTOMIZATION_PRIOR_OVERRIDE_CULTURE_UNEXPECTED');
    }
  };
  const deactivateTestCategories = async page => {
    await page.goto(state.host.origin + '/ChurchSettings/OfferingCategories', { waitUntil: 'domcontentloaded' });
    const ids = await page.locator('tr[data-category-id]').evaluateAll(rows => rows
      .filter(row => row.querySelector('input[name="name"]')?.value
        ?.startsWith('P8 測試奉獻類別 '))
      .map(row => row.getAttribute('data-category-id')));
    if (!Array.isArray(ids) || ids.some(id => !/^[0-9a-f-]{36}$/i.test(id || '')))
      throw fixedError('CUSTOMIZATION_CATEGORY_ID_INVALID');
    for (const id of ids) {
      await deactivateCategoryRow({ page, origin: state.host.origin, id });
    }
  };
  const inspect = async churchId => {
    const { page } = sessionFor(churchId);
    await page.goto(state.host.origin + '/ChurchSettings/Terms', { waitUntil: 'domcontentloaded' });
    const role = await rowFor(page, ROLE_KEY).innerText();
    const template = await rowFor(page, 'Fees.Notify.Template').innerText();
    const rolePreview = (await rowFor(page, ROLE_KEY).locator('td').nth(4).innerText()).trim();
    const roleOverride = await readTermOverride(page, ROLE_KEY);
    const templateOverride = await readTermOverride(page, 'Fees.Notify.Template');
    const listResponse = await page.request.get(state.host.origin + '/Personal/Api/GetMembershipStatusList');
    if (listResponse.status() !== 200) throw fixedError('CUSTOMIZATION_MEMBER_API_FAILED');
    const listBody = await listResponse.json();
    if (listBody?.success !== true || !Array.isArray(listBody?.items))
      throw fixedError('CUSTOMIZATION_MEMBER_ITEMS_SHAPE_INVALID');
    const memberItems = listBody.items.map(item => ({
      value: typeof item?.value === 'string' ? item.value : '',
      text: typeof item?.text === 'string' ? item.text : '',
      code: typeof item?.code === 'string' ? item.code : ''
    }));
    const baselineItems = baselineA.value?.memberItems;
    const roleItem = memberItems.find(item => item.code === ROLE_CODE);
    await page.goto(state.host.origin + '/ChurchSettings/OfferingCategories', { waitUntil: 'domcontentloaded' });
    const activeTestCategoryCount = await page.locator('tr[data-category-id]').evaluateAll(rows =>
      rows.filter(row => row.querySelector('input[name="name"]')?.value
        ?.startsWith('P8 測試奉獻類別 ')
        && row.querySelector('input[name="isActive"]')?.checked === true).length);
    const categoryPresent = await page.locator(
      `tr[data-category-id] input[name="name"][value="${categoryName}"]`
    ).count() > 0;
    return {
      roleText: role.includes(ROLE_OVERRIDE_TEXT) ? ROLE_OVERRIDE_TEXT : '',
      rolePreview,
      roleValue: roleItem?.value || '',
      mappedRoleOverrideMatches: mappedRoleItemMatchesOverride(
        memberItems, ROLE_CODE, ROLE_D365_VALUE, ROLE_OVERRIDE_TEXT
      ),
      roleOverride,
      templateOverride,
      assistantRoleUnmapped: assistantRoleRemainsUnmapped(memberItems),
      memberItems,
      memberItemsUnchanged: baselineItems === undefined
        || JSON.stringify(memberItems) === JSON.stringify(baselineItems),
      categoryPresent,
      templateContains: template.includes('P8 測試通知'),
      templateText: template,
      categoryActive: await page.locator(
        `tr[data-category-id] input[name="name"][value="${categoryName}"]`
      ).evaluateAll(inputs => inputs.some(input =>
        input.closest('tr')?.querySelector('input[name="isActive"]')?.checked === true)),
      activeTestCategoryCount
    };
  };
  const runtime = {
    phase: 'create-b',
    customizationWriteStarted: false,
    async createTemporaryChurch() {
      runtime.phase = 'create-b';
      state.host = await startHost(root, 5172, {
        allowPseudoLocale: true,
        enableChurchOverrides: true
      });
      state.browser = await launchEdge({ headless: true });
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
    async applyCustomization({ categoryName: requestedCategoryName }) {
      runtime.phase = 'customize';
      const { page } = sessionFor(state.aChurch);
      baselineA.value = await inspect(state.aChurch);
      runtime.customizationWriteStarted = true;
      try {
        await clearPriorTestOverrides(page);
      } catch (error) {
        const code = typeof error?.code === 'string'
          && /^CUSTOMIZATION_[A-Z0-9_]+$/.test(error.code)
          ? error.code
          : 'CUSTOMIZATION_PRIOR_OVERRIDE_CLEANUP_FAILED';
        throw fixedError(code);
      }
      try { await deactivateTestCategories(page); }
      catch (error) {
        const code = typeof error?.code === 'string'
          && /^CUSTOMIZATION_[A-Z0-9_]+$/.test(error.code)
          ? error.code
          : 'CUSTOMIZATION_CATEGORY_CLEANUP_FAILED';
        throw fixedError(code);
      }
      try { baselineA.value = await inspect(state.aChurch); }
      catch { throw fixedError('CUSTOMIZATION_BASELINE_READ_FAILED'); }
      try {
        await page.goto(state.host.origin + '/ChurchSettings/Terms', { waitUntil: 'domcontentloaded' });
        await saveTerm(page, ROLE_KEY, ROLE_OVERRIDE_TEXT);
      } catch { throw fixedError('CUSTOMIZATION_CUSTOMIZE_ROLE_FAILED'); }
      try {
        await saveTerm(page, 'Fees.Notify.Template', TEST_TEMPLATE_TEXT);
      } catch { throw fixedError('CUSTOMIZATION_CUSTOMIZE_TEMPLATE_FAILED'); }
      try {
        await page.goto(state.host.origin + '/ChurchSettings/OfferingCategories', { waitUntil: 'domcontentloaded' });
        const form = page.locator('form[action$="/OfferingCategories/Save"]').first();
        await form.locator('input[name="name"]').fill(requestedCategoryName);
        await form.locator('input[name="sortOrder"]').fill('0');
        await form.locator('button[type="submit"]').click();
        await page.waitForLoadState('domcontentloaded');
        state.categoryId = await findCreatedCategoryId(page);
        if (!state.categoryId) throw fixedError('CUSTOMIZATION_CATEGORY_ID_MISSING');
      } catch { throw fixedError('CUSTOMIZATION_CUSTOMIZE_CATEGORY_FAILED'); }
    },
    async readCustomizedState(churchId) {
      runtime.phase = 'read';
      return inspect(churchId);
    },
    async cleanup() {
      runtime.phase = 'cleanup';
      const a = sessions.get([...sessions.keys()].find(id => sessions.get(id).admin.churchId === state.aChurch));
      let stateRestored = false;
      let termRestoreSucceeded = false;
      let ownersClosed = true;
      const restorationChecks = {
        noChangesStarted: false,
        baselineCaptured: baselineA.value !== null,
        rolePreviewRestored: false,
        roleOverrideRestored: false,
        templateOverrideRestored: false,
        memberItemsRestored: false,
        categoryInactive: false
      };
      let cleanupStage = 'restore-role';
      let cleanupFailureStage = null;
      let failedStages = [];
      let failedCodes = [];
      try {
        if (!runtime.customizationWriteStarted) {
          restorationChecks.noChangesStarted = true;
          termRestoreSucceeded = true;
          stateRestored = true;
        } else if (a) {
          const roleActions = getTermRestoreActions(baselineA.value?.roleOverride);
          const templateActions = getTermRestoreActions(baselineA.value?.templateOverride);
          const restoreTerm = async (key, actions) => {
            for (const action of actions) {
              if (action.operation === 'reset') {
                await resetTerm(a.page, key, action.culture);
                continue;
              }
              await saveTerm(a.page, key, action.text, action.culture);
            }
          };
          let restored;
          const cleanupResult = await runCleanupStages([
            {
              name: `restore-role-${roleActions[0]?.operation || 'invalid'}`,
              run: () => restoreTerm(ROLE_KEY, roleActions)
            },
            {
              name: `restore-template-${templateActions[0]?.operation || 'invalid'}`,
              run: () => restoreTerm('Fees.Notify.Template', templateActions)
            },
            {
              name: 'clear-prior-test-overrides',
              run: () => clearPriorTestOverrides(a.page)
            },
            {
              name: 'deactivate-category',
              run: () => deactivateTestCategories(a.page)
            },
            {
              name: 'verify-restoration',
              run: async () => {
                restored = await inspect(state.aChurch);
                termRestoreSucceeded = true;
                restorationChecks.baselineCaptured = baselineA.value !== null;
                restorationChecks.rolePreviewRestored =
                  restored.rolePreview === baselineA.value?.rolePreview;
                restorationChecks.roleOverrideRestored =
                  JSON.stringify(restored.roleOverride) === JSON.stringify(baselineA.value?.roleOverride);
                restorationChecks.templateOverrideRestored =
                  JSON.stringify(restored.templateOverride) === JSON.stringify(baselineA.value?.templateOverride);
                restorationChecks.memberItemsRestored =
                  JSON.stringify(restored.memberItems) === JSON.stringify(baselineA.value?.memberItems);
                restorationChecks.categoryInactive = restored.activeTestCategoryCount === 0
                  && restored.categoryActive === false;
              }
            }
          ]);
          failedStages = cleanupResult.failedStages;
          failedCodes = cleanupResult.failedCodes;
          cleanupFailureStage = failedStages[0] || null;
          stateRestored = restorationChecks.noChangesStarted
            || (failedStages.length === 0
              && allRestorationChecksPassed(restorationChecks));
        }
      } catch {
        termRestoreSucceeded = false;
        cleanupFailureStage = cleanupStage;
      } finally {
        cleanupStage = 'close-contexts';
        for (const item of sessions.values()) {
          try { await item.context.close(); } catch {
            ownersClosed = false;
            cleanupFailureStage ||= cleanupStage;
          }
        }
        sessions.clear();
        cleanupStage = 'close-browser';
        try { if (state.browser) await state.browser.close(); } catch {
          ownersClosed = false;
          cleanupFailureStage ||= cleanupStage;
        }
        cleanupStage = 'stop-host';
        try { if (state.host) await state.host.stop(); } catch {
          ownersClosed = false;
          cleanupFailureStage ||= cleanupStage;
        }
      }
      let reset = { remaining: null };
      let resetSucceeded = false;
      let closed = { remaining: null, secretsRestored: false, processExited: false, drainTimedOut: true };
      cleanupStage = 'bridge-reset';
      try { reset = await bridge.request({ operation: 'reset' }); }
      catch {
        resetSucceeded = false;
        cleanupFailureStage ||= cleanupStage;
      }
      finally {
        cleanupStage = 'bridge-close';
        try {
          closed = await bridge.close();
        } catch {
          ownersClosed = false;
          cleanupFailureStage ||= cleanupStage;
        }
      }
      resetSucceeded = reset?.ok !== false && reset?.remaining !== null;
      return {
        remaining: resetSucceeded && Number.isInteger(reset.remaining)
          ? Math.max(reset.remaining, closed.remaining ?? Number.MAX_SAFE_INTEGER)
          : Number.MAX_SAFE_INTEGER,
        secretsRestored: closed.secretsRestored === true,
        stateRestored: termRestoreSucceeded && stateRestored,
        ownersClosed: ownersClosed && closed.processExited === true && closed.drainTimedOut === false,
        stage: cleanupFailureStage || 'complete',
          failedStages,
          failedCodes,
        checks: restorationChecks
      };
    }
  };
  state.bChurch = null;
  runtime.phase = 'run';
  try {
    runtime.categoryName = categoryName;
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
