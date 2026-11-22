/*
 * 檔案整體說明
 * 目的與定位：保護 PseudoLocaleAudit 在登入、頁面等待與清理故障時的單一 owner
 * 生命週期契約。以注入的合成 runtime 執行真正生命週期協調器，不連線、不讀 User
 * Secrets、不啟動 Edge／Host／SDK，也不輸出秘密或任意例外本文。
 * 主要流程：驗證固定路由清單、分別注入登入拒絕、頁面逾時及清理失敗，並檢查一次性
 * 管理員 fixture 先建立 owned contact 再寫職稱；所有斷言只讀固定路徑、事件順序與錯誤碼。
 * 測試不建立跨案例狀態或外部資源。
 * 使用邊界與維護：每案 runtime 與計數器由測試函式區域擁有，沒有 timer、程序或
 * stream；未來新增 owner 資源時須加入故障注入與確定釋放斷言。執行本檔可重現驗收。
 */
'use strict';
const assert = require('node:assert/strict');
const { test } = require('node:test');
const { runPseudoAudit, PAGE_CASES } = require('../PseudoAuditLifecycle.cjs');
const {
  classifyLoginTransportFailure,
  summarizeLoginResponse
} = require('../Run-PseudoLocaleAudit.cjs');
const {
  allRestorationChecksPassed,
  assistantRoleRemainsUnmapped,
  deactivateCategoryRow,
  getTermRestoreActions,
  isPriorP8TestOverride,
  mappedRoleItemMatchesOverride,
  runCleanupStages,
  submitTermChange,
  runCustomizationCheck
} = require('../Run-CustomizationCheck.cjs');
const { createRoundFixture } = require('../../PageAcceptance/P6/P6Fixtures.cjs');
const { buildDevelopmentHostEnvironment } = require('../../PageAcceptance/Shared/DevelopmentHost.cjs');
const pages = PAGE_CASES;

test('DevelopmentHost_ChurchOverridesRequireExplicitDevelopmentOptIn', () => {
  const inherited = { ExistingSetting: 'preserved', SPEECHMESSAGE_R2_WRITE_GOLDEN: 'true' };
  const defaults = buildDevelopmentHostEnvironment(5172, {}, inherited);
  const enabled = buildDevelopmentHostEnvironment(5172, {
    allowPseudoLocale: true,
    enableChurchOverrides: true
  }, inherited);

  assert.equal(defaults.Localization__ChurchOverrides__Enabled, undefined);
  assert.equal(enabled.Localization__ChurchOverrides__Enabled, 'true');
  assert.equal(enabled.Localization__AllowPseudoLocale, 'true');
  assert.equal(enabled.ExistingSetting, 'preserved');
  assert.equal(enabled.SPEECHMESSAGE_R2_WRITE_GOLDEN, undefined);
});

test('PseudoAudit_LoginDiagnostics_StoresOnlyStatusAndBooleanFacts', () => {
  const summary = summarizeLoginResponse(200, '登入機密姓名 成功!', ['.ChurchReport.Auth']);

  assert.deepEqual(summary, {
    loginHttpStatus: 200,
    loginResponseAccepted: true,
    authenticationCookiePresent: true
  });
  assert.equal(JSON.stringify(summary).includes('機密姓名'), false);
  assert.deepEqual(
    summarizeLoginResponse(401, '帳號或密碼錯誤', []),
    {
      loginHttpStatus: 401,
      loginResponseAccepted: false,
      authenticationCookiePresent: false
    }
  );
});

test('PseudoAudit_LoginTransportFailure_UsesFixedNonSensitiveCategory', () => {
  assert.equal(
    classifyLoginTransportFailure(Object.assign(new Error('secret response body'), {
      code: 'PSEUDO_AUDIT_PAGE_TIMEOUT'
    })),
    'timeout'
  );
  assert.equal(classifyLoginTransportFailure(new Error('private URL and credentials')), 'request-error');
});

test('PseudoAudit_RouteManifest_UsesActualRoutesAndForbiddenContext', () => {
  assert.equal(require('../audit-urls.json').schemaVersion, 1);
  assert.deepEqual(PAGE_CASES, [
    { id: 'terms', path: '/ChurchSettings/Terms', access: 'admin' },
    { id: 'offering-categories', path: '/ChurchSettings/OfferingCategories', access: 'admin' },
    { id: 'audit', path: '/ChurchSettings/Audit', access: 'admin' },
    { id: 'forbidden', path: '/ChurchSettings/Terms', access: 'non-admin' }
  ]);
});

test('PseudoAudit_AdminFixtureRole_IsAppliedAfterOwnedFixtureAssembly', async () => {
  const events = [];
  const bridge = {
    async request(value) {
      events.push(value);
      return value.operation === 'update' ? { ok: true, roleValuePersisted: true } : { ok: true };
    }
  };
  const settings = {
    churchId: '11111111-1111-4111-8111-111111111111',
    contactId: '22222222-2222-4222-8222-222222222222',
    lineUserId: 'synthetic-line-user',
    sunday: '2026-11-22T00:00:00.000Z'
  };
  const credentials = { account: 'synthetic-account', password: 'synthetic-password' };
  await createRoundFixture(bridge, settings, credentials, {
    churchJobTitle: '牧師傳道',
    preferredLanguage: 'qps-ploc'
  });
  assert.equal(events[0].operation, 'create');
  assert.equal(events[0].entity, 'contact');
  assert.equal(Object.hasOwn(events[0].fields, 'new_church_jobtitle'), false);
  assert.equal(events.filter(event => event.operation === 'addMember').length, 2);
  assert.equal(events.at(-1).operation, 'update');
  assert.equal(events.at(-1).entity, 'contact');
  assert.equal(events.at(-1).id, events[0].id);
  assert.deepEqual(events.at(-1).fields, { new_church_jobtitle: '牧師傳道' });
});

test('PseudoAuditLifecycle_LoginFailure_AlwaysRunsCleanup', async () => {
  let cleaned = 0;
  await assert.rejects(runPseudoAudit({
    pages,
    runtime: {
      login: async () => { throw new Error('synthetic login rejection'); },
      auditPage: async () => ({ unwrappedChinese: 0, clippedText: 0 }),
      cleanup: async () => { cleaned++; return { remaining: 0, secretsRestored: true }; }
    }
  }), error => error.code === 'PSEUDO_AUDIT_LOGIN_FAILED');
  assert.equal(cleaned, 1);
});

test('PseudoAuditLifecycle_PageTimeout_AlwaysRunsCleanup', async () => {
  let cleaned = 0;
  await assert.rejects(runPseudoAudit({
    pages,
    runtime: {
      login: async () => ({ authenticated: true }),
      auditPage: async page => {
        if (page.id === 'terms') throw Object.assign(new Error('synthetic timeout'), { code: 'PSEUDO_AUDIT_PAGE_TIMEOUT' });
        return { unwrappedChinese: 0, clippedText: 0 };
      },
      cleanup: async () => { cleaned++; return { remaining: 0, secretsRestored: true }; }
    }
  }), error => error.code === 'PSEUDO_AUDIT_PAGE_TIMEOUT');
  assert.equal(cleaned, 1);
});

test('PseudoAuditLifecycle_CleanupFailure_NeverReportsSuccess', async () => {
  let cleaned = 0;
  await assert.rejects(runPseudoAudit({
    pages,
    runtime: {
      login: async () => ({ authenticated: true }),
      auditPage: async () => ({ unwrappedChinese: 0, clippedText: 0 }),
      cleanup: async () => { cleaned++; throw new Error('synthetic cleanup failure'); }
    }
  }), error => error.code === 'PSEUDO_AUDIT_CLEANUP_FAILED');
  assert.equal(cleaned, 1);
});

test('CustomizationLifecycle_TemporaryChurchFailure_AlwaysCleansBAndAdmins', async () => {
  const events = [];
  await assert.rejects(
    runCustomizationCheck({
      churchA: 'church-a',
      runtime: {
        async createTemporaryChurch() {
          events.push('church-b.create');
          return { id: 'church-b', created: true };
        },
        async createAdmin(churchId) {
          events.push(`admin.create:${churchId}`);
          return { id: `admin-${churchId}`, created: true };
        },
        async loginAsAdmin() {
          events.push('login');
          return { authenticated: true };
        },
        async applyCustomization() {
          events.push('customize');
          throw new Error('synthetic customization failure');
        },
        async readCustomizedState() {
          throw new Error('must not read after customize failure');
        },
        async cleanup(input) {
          events.push(`cleanup:${input.churchB}:${input.adminB}`);
          return {
            remaining: 0,
            secretsRestored: true,
            stateRestored: true,
            ownersClosed: true
          };
        }
      }
    }),
    error => error.code === 'CUSTOMIZATION_OPERATION_FAILED'
  );
  assert.deepEqual(events, [
    'church-b.create',
    'admin.create:church-a',
    'admin.create:church-b',
    'login',
    'customize',
    'cleanup:church-b:admin-church-b'
  ]);
});

test('CustomizationCleanup_ExistingOverridesAreSavedRatherThanReset', () => {
  assert.deepEqual(getTermRestoreActions({
    culture: 'zh-TW',
    text: '原有教會自訂文字'
  }), [{
    operation: 'save',
    culture: 'zh-TW',
    text: '原有教會自訂文字'
  }]);
  assert.deepEqual(getTermRestoreActions({
    culture: 'zh-TW',
    text: ''
  }), [{ operation: 'reset', culture: 'zh-TW' }]);
  assert.deepEqual(getTermRestoreActions({
    culture: '*',
    text: '原有全語系覆寫'
  }), [
    { operation: 'reset', culture: 'zh-TW' },
    { operation: 'save', culture: '*', text: '原有全語系覆寫' }
  ]);
});

test('CustomizationTermSave_NavigatesToTermsBeforeLocatingForm', async () => {
  const events = [];
  const page = {
    async goto(url) { events.push(`goto:${url}`); },
    locator(selector) {
      events.push(`locator:${selector}`);
      return {
        filter() {
          return {
            first() {
              return {
                async count() { return 1; },
                locator(action) {
                  events.push(`form:${action}`);
                  return {
                    locator(field) {
                      return {
                        async fill(value) { events.push(`fill:${field}:${value}`); },
                        async click(options) {
                          events.push(`click:${field}`);
                          assert.equal(options?.noWaitAfter, true);
                        }
                      };
                    }
                  };
                }
              };
            }
          };
        }
      };
    },
    async waitForResponse(predicate) {
      events.push('waitForResponse');
      assert.equal(predicate({
        url: () => 'http://localhost:5172/ChurchSettings/Terms/Save',
        request: () => ({ method: () => 'POST' })
      }), true);
      return { status: () => 302 };
    },
    async waitForLoadState(state) { events.push(`wait:${state}`); }
  };

  await submitTermChange({
    page,
    origin: 'http://localhost:5172',
    key: 'Role.AssistantGroupLeader',
    operation: 'save',
    culture: 'zh-TW',
    text: '原有覆寫'
  });

  assert.equal(events[0], 'goto:http://localhost:5172/ChurchSettings/Terms');
  assert.ok(events.indexOf('goto:http://localhost:5172/ChurchSettings/Terms')
    < events.findIndex(event => event.startsWith('locator:')));
  assert.ok(events.includes('waitForResponse'));
});

test('CustomizationTermSave_PreservesResolvedFormCulture', async () => {
  const events = [];
  const form = {
    locator(selector) {
      events.push(`form.locator:${selector}`);
      return {
        async fill(value) { events.push(`fill:${selector}:${value}`); },
        async click(options) { events.push(`click:${selector}:${options.noWaitAfter}`); }
      };
    }
  };
  const row = {
    async count() { return 1; },
    locator() { return form; }
  };
  const page = {
    async goto() {},
    locator() {
      return { filter() { return { first: () => row }; } };
    },
    async waitForResponse(predicate) {
      assert.equal(predicate({
        url: () => 'http://localhost:5172/ChurchSettings/Terms/Save',
        request: () => ({ method: () => 'POST' })
      }), true);
      return { status: () => 302 };
    }
  };

  await submitTermChange({
    page,
    origin: 'http://localhost:5172',
    key: 'Role.GroupLeader',
    operation: 'save',
    text: '本次教會覆寫'
  });

  assert.equal(events.some(event => event.includes('input[name="culture"]')), false);
});

test('CustomizationTermSave_HttpFailureUsesFixedStatusCode', async () => {
  const page = {
    async goto() {},
    locator() {
      return {
        filter() {
          return {
            first() {
              return {
                async count() { return 1; },
                locator() {
                  return {
                    locator() {
                      return {
                        async fill() {},
                        async click() {}
                      };
                    }
                  };
                }
              };
            }
          };
        }
      };
    },
    async waitForResponse(predicate) {
      assert.equal(predicate({
        url: () => 'http://localhost:5172/ChurchSettings/Terms/Save',
        request: () => ({ method: () => 'POST' })
      }), true);
      return { status: () => 500 };
    }
  };

  await assert.rejects(submitTermChange({
    page,
    origin: 'http://localhost:5172',
    key: 'Role.GroupLeader',
    operation: 'save',
    culture: 'zh-TW',
    text: '合成覆寫'
  }), error => error.code === 'CUSTOMIZATION_TERM_SAVE_HTTP_500');
});

test('CustomizationTermReset_SetsHiddenCultureWithoutFilling', async () => {
  const events = [];
  const form = {
    locator(selector) {
      if (selector === 'input[name="culture"]') {
        return {
          async fill() { throw new Error('hidden input cannot be filled'); },
          async evaluate(callback, value) {
            events.push(`culture:${value}`);
            callback({ value: '' }, value);
          }
        };
      }
      return { async click(options) { events.push(`submit:${options.noWaitAfter}`); } };
    }
  };
  const row = {
    async count() { return 1; },
    locator() { return form; }
  };
  const page = {
    async goto(url) { events.push(`goto:${url}`); },
    locator() {
      return {
        filter() {
          return {
            first: () => row
          };
        }
      };
    },
    async waitForResponse(predicate) {
      assert.equal(predicate({
        url: () => 'http://localhost:5172/ChurchSettings/Terms/Reset',
        request: () => ({ method: () => 'POST' })
      }), true);
      return { status: () => 302 };
    }
  };

  await submitTermChange({
    page,
    origin: 'http://localhost:5172',
    key: 'Role.GroupLeader',
    operation: 'reset',
    culture: 'zh-TW'
  });

  assert.ok(events.includes('culture:zh-TW'));
  assert.ok(events.includes('submit:true'));
});

test('CustomizationCleanup_FailedStageDoesNotSkipRemainingRestoration', async () => {
  const events = [];
  const result = await runCleanupStages([
    {
      name: 'restore-role',
      async run() {
        events.push('restore-role');
        const error = new Error('synthetic failure must not escape');
        error.code = 'CUSTOMIZATION_TERM_SAVE_HTTP_500';
        throw error;
      }
    },
    { name: 'restore-template', async run() { events.push('restore-template'); } },
    { name: 'deactivate-category', async run() { events.push('deactivate-category'); } }
  ]);

  assert.deepEqual(events, ['restore-role', 'restore-template', 'deactivate-category']);
  assert.deepEqual(result, {
    failedStages: ['restore-role'],
    failedCodes: [{ stage: 'restore-role', code: 'CUSTOMIZATION_TERM_SAVE_HTTP_500' }]
  });
});

test('CustomizationCleanup_CategoryDeactivationIsReadBackBeforeSuccess', async () => {
  const events = [];
  const checkboxState = { checked: true };
  const persistedState = { checked: true };
  const row = {
    async count() { return 1; },
    locator(selector) {
      events.push(`row.locator:${selector}`);
      if (selector === 'input[name="isActive"]') {
        const checkbox = {
          async isChecked() { return checkboxState.checked; },
          async uncheck() {
            events.push('checkbox.uncheck');
            checkboxState.checked = false;
          }
        };
        return {
          first() {
            events.push('checkbox.first');
            return checkbox;
          }
        };
      }
      return {
        async click(options) {
          events.push('save.click');
          assert.equal(options?.noWaitAfter, true);
          persistedState.checked = checkboxState.checked;
        }
      };
    }
  };
  let categoryPageLoads = 0;
  const page = {
    async goto(url) {
      events.push(`goto:${url}`);
      categoryPageLoads++;
      if (categoryPageLoads > 1) checkboxState.checked = persistedState.checked;
    },
    locator(selector) {
      events.push(`locator:${selector}`);
      return { first: () => row };
    },
    async waitForResponse(predicate) {
      assert.equal(predicate({
        url: () => 'http://localhost:5172/ChurchSettings/OfferingCategories/Save',
        request: () => ({ method: () => 'POST' })
      }), true);
      return { status: () => 302 };
    },
    async waitForLoadState(state) { events.push(`wait:${state}`); }
  };

  const result = await deactivateCategoryRow({
    page,
    origin: 'http://localhost:5172',
    id: '11111111-1111-4111-8111-111111111111'
  });

  assert.equal(result, true);
  assert.equal(categoryPageLoads, 2, 'the saved state must be verified after a fresh GET');
  assert.equal(checkboxState.checked, false);
  assert.ok(events.includes('save.click'));
  assert.equal(events.filter(event => event === 'checkbox.first').length, 2);
});

test('CustomizationCleanup_CategoryStillActiveAfterSave_FailsClosed', async () => {
  const checkboxState = { checked: true };
  const row = {
    async count() { return 1; },
    locator(selector) {
      if (selector === 'input[name="isActive"]') {
        const checkbox = {
          async isChecked() { return checkboxState.checked; },
          async uncheck() { checkboxState.checked = false; }
        };
        return { first: () => checkbox };
      }
      return {
        async click(options) {
          assert.equal(options?.noWaitAfter, true);
        }
      };
    }
  };
  let pageLoads = 0;
  const page = {
    async goto() {
      pageLoads++;
      if (pageLoads > 1) checkboxState.checked = true;
    },
    locator() { return { first: () => row }; },
    async waitForResponse(predicate) {
      assert.equal(predicate({
        url: () => 'http://localhost:5172/ChurchSettings/OfferingCategories/Save',
        request: () => ({ method: () => 'POST' })
      }), true);
      return { status: () => 302 };
    },
    async waitForLoadState() {}
  };

  await assert.rejects(deactivateCategoryRow({
    page,
    origin: 'http://localhost:5172',
    id: '11111111-1111-4111-8111-111111111111'
  }), error => error.code === 'CUSTOMIZATION_CATEGORY_DEACTIVATION_NOT_VISIBLE');
});

test('CustomizationCleanup_CategorySaveHttpFailure_ReportsOnlyStatus', async () => {
  const row = {
    async count() { return 1; },
    locator(selector) {
      if (selector === 'input[name="isActive"]') {
        const checkbox = {
          async isChecked() { return true; },
          async uncheck() {}
        };
        return { first: () => checkbox };
      }
      return { async click() {} };
    }
  };
  const page = {
    async goto() {},
    locator() { return { first: () => row }; },
    async waitForResponse(predicate) {
      assert.equal(predicate({
        url: () => 'http://localhost:5172/ChurchSettings/OfferingCategories/Save',
        request: () => ({ method: () => 'POST' })
      }), true);
      return { status: () => 500 };
    },
    async waitForLoadState() {}
  };

  await assert.rejects(deactivateCategoryRow({
    page,
    origin: 'http://localhost:5172',
    id: '11111111-1111-4111-8111-111111111111'
  }), error => error.code === 'CUSTOMIZATION_CATEGORY_SAVE_HTTP_500');
});

test('CustomizationCleanup_CategoryResponseTimeout_UsesFixedPhaseCode', async () => {
  const row = {
    async count() { return 1; },
    locator(selector) {
      if (selector === 'input[name="isActive"]') {
        const checkbox = {
          async isChecked() { return true; },
          async uncheck() {}
        };
        return { first: () => checkbox };
      }
      return { async click() {} };
    }
  };
  const page = {
    async goto() {},
    locator() { return { first: () => row }; },
    async waitForResponse() {
      const error = new Error('response details must not escape');
      error.name = 'TimeoutError';
      throw error;
    }
  };

  await assert.rejects(deactivateCategoryRow({
    page,
    origin: 'http://localhost:5172',
    id: '11111111-1111-4111-8111-111111111111'
  }), error => error.code === 'CUSTOMIZATION_CATEGORY_AWAIT_SUBMIT_RESPONSE_TIMEOUT');
});

test('CustomizationCleanup_CompletedRestorationDoesNotRequireNoChanges', async () => {
  assert.equal(allRestorationChecksPassed({
    noChangesStarted: false,
    baselineCaptured: true,
    rolePreviewRestored: true,
    roleOverrideRestored: true,
    templateOverrideRestored: true,
    memberItemsRestored: true,
    categoryInactive: true
  }), true);

  const states = new Map([
    ['church-a', {
      rolePreview: '小組帶領者',
      roleValue: '小組長',
      mappedRoleOverrideMatches: true,
      assistantRoleUnmapped: true,
      categoryPresent: true,
      categoryActive: true,
      templateContains: true
    }],
    ['church-b', {
      rolePreview: '小組長',
      roleValue: '小組長',
      roleText: '',
      mappedRoleOverrideMatches: false,
      assistantRoleUnmapped: true,
      categoryPresent: false,
      templateContains: false
    }]
  ]);
  const result = await runCustomizationCheck({
    churchA: 'church-a',
    runtime: {
      async createTemporaryChurch() { return { id: 'church-b', created: true }; },
      async createAdmin(churchId) { return { id: `admin-${churchId}`, created: true, churchId }; },
      async loginAsAdmin() { return { authenticated: true }; },
      async applyCustomization() {},
      async readCustomizedState(churchId) { return states.get(churchId); },
      async cleanup() {
        return {
          remaining: 0,
          secretsRestored: true,
          stateRestored: true,
          ownersClosed: true
        };
      }
    }
  });

  assert.equal(result.aWithin60s, true);
  assert.equal(result.bUnaffected, true);
  assert.equal(result.cleaned, true);
});

test('CustomizationRoleItems_UnmappedAssistantRoleStaysWithoutCodeAndOverride', () => {
  assert.equal(assistantRoleRemainsUnmapped([
    { value: '實習小組長', text: '實習小組長', code: '' }
  ]), true);
  assert.equal(assistantRoleRemainsUnmapped([
    { value: '實習小組長', text: '小組長', code: '' }
  ]), false);
  assert.equal(assistantRoleRemainsUnmapped([
    { value: '實習小組長', text: '實習小組長', code: 'Role.AssistantGroupLeader' }
  ]), false);
  assert.equal(assistantRoleRemainsUnmapped([
    { value: '副小組長', text: '副小組長', code: '' }
  ]), false);
});

test('CustomizationRoleItems_MappedOverridePreservesD365Value', () => {
  const items = [
    { value: '小組長', text: '小組帶領者', code: 'Role.GroupLeader' },
    { value: '副小組長', text: '副小組長', code: '' }
  ];

  assert.equal(mappedRoleItemMatchesOverride(
    items, 'Role.GroupLeader', '小組長', '小組帶領者'
  ), true);
  assert.equal(mappedRoleItemMatchesOverride(
    items, 'Role.GroupLeader', '小組帶領者', '小組帶領者'
  ), false);
  assert.equal(mappedRoleItemMatchesOverride(
    items, 'Role.AssistantGroupLeader', '副小組長', '實習小組長'
  ), false);
});

test('CustomizationCleanup_RecognizesOnlyExactPriorTestOverrides', () => {
  assert.equal(isPriorP8TestOverride('Role.GroupLeader', '小組帶領者'), true);
  assert.equal(isPriorP8TestOverride('Role.AssistantGroupLeader', '實習小組長'), true);
  assert.equal(isPriorP8TestOverride(
    'Fees.Notify.Template',
    'P8 測試通知 {member} {date} {category} {others} {payway} {amount}'
  ), true);
  assert.equal(isPriorP8TestOverride('Role.GroupLeader', '實習小組長'), false);
  assert.equal(isPriorP8TestOverride('Role.GroupLeader', '教會自訂小組長'), false);
  assert.equal(isPriorP8TestOverride('Role.AssistantGroupLeader', '自訂小組領袖'), false);
});

test('CustomizationLifecycle_BRolePreviewCrossTalk_FailsClosed', async () => {
  const states = new Map([
    ['church-a', {
      rolePreview: '小組帶領者',
      roleValue: '小組長',
      mappedRoleOverrideMatches: true,
      assistantRoleUnmapped: true,
      categoryPresent: true,
      categoryActive: true,
      templateContains: true
    }],
    ['church-b', {
      rolePreview: '小組帶領者',
      roleValue: '小組長',
      roleText: '小組帶領者',
      mappedRoleOverrideMatches: false,
      assistantRoleUnmapped: true,
      categoryPresent: false,
      templateContains: false
    }]
  ]);
  await assert.rejects(runCustomizationCheck({
    churchA: 'church-a',
    runtime: {
      async createTemporaryChurch() { return { id: 'church-b', created: true }; },
      async createAdmin(churchId) { return { id: `admin-${churchId}`, created: true, churchId }; },
      async loginAsAdmin() { return { authenticated: true }; },
      async applyCustomization() {},
      async readCustomizedState(churchId) { return states.get(churchId); },
      async cleanup() {
        return {
          remaining: 0,
          secretsRestored: true,
          stateRestored: true,
          ownersClosed: true
        };
      }
    }
  }), error => {
    assert.equal(error.code, 'CUSTOMIZATION_B_ROLE_CROSSTALK');
    assert.equal(error.operationChecks?.bRoleUnchanged, false);
    return true;
  });
});
