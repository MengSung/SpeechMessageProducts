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
const { createRoundFixture } = require('../../PageAcceptance/P6/P6Fixtures.cjs');
const pages = PAGE_CASES;

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
