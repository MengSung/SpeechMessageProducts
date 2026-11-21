/*
 * 檔案整體說明
 * 目的與定位：統一管理一次偽語系頁面稽核的登入、頁面訪問與最終清理順序。
 * 主要流程：先驗證固定頁面清單與注入 runtime 契約，登入後逐頁稽核；不論登入、
 * 導覽或量測失敗，finally 都只呼叫一次 cleanup。對外只丟固定錯誤碼，避免原始例外
 * 文字可能含 URL、帳密、Cookie 或頁面資料；成功結果只保留數量及逐頁摘要。
 * 資源與隔離：此協調器不擁有 Browser、Host、SDK、秘密或 timer；runtime 是唯一 owner，
 * 必須讓頁面操作有界並由 cleanup 關閉 Context／Browser、停止 Host、清理 fixture、
 * 還原秘密租約。cleanup 未回報 remaining=0 與 secretsRestored=true 一律視為失敗。
 * 本檔不保存跨次狀態；以 PseudoAuditLifecycle.Tests.cjs 注入登入、逾時及清理故障驗證。
 */
'use strict';

const manifest = require('./audit-urls.json');
const PAGE_CASES = Object.freeze([
  ...manifest.cases.map(item => Object.freeze({ ...item }))
]);

function fixedError(code) {
  const error = new Error(code);
  error.code = code;
  return error;
}

/** 執行固定頁面集合並保證 runtime owner 清理一次；任何清理狀態未知都拒絕成功。 */
async function runPseudoAudit({ pages, runtime }) {
  if (!Array.isArray(pages) || pages.length !== PAGE_CASES.length
      || pages.some((page, index) => JSON.stringify(page) !== JSON.stringify(PAGE_CASES[index]))
      || !runtime || typeof runtime.login !== 'function'
      || typeof runtime.auditPage !== 'function' || typeof runtime.cleanup !== 'function') {
    throw fixedError('PSEUDO_AUDIT_CONFIGURATION_INVALID');
  }

  let failure;
  let authenticated = false;
  const results = [];
  let cleanup;
  try {
    try {
      const login = await runtime.login();
      authenticated = login?.authenticated === true;
      if (!authenticated) throw fixedError('PSEUDO_AUDIT_LOGIN_FAILED');
    } catch {
      throw fixedError('PSEUDO_AUDIT_LOGIN_FAILED');
    }
    for (const page of pages) {
      try {
        const result = await runtime.auditPage(page);
        if (!result || !Number.isSafeInteger(result.unwrappedChinese) || result.unwrappedChinese < 0
            || !Number.isSafeInteger(result.clippedText) || result.clippedText < 0) {
          throw fixedError('PSEUDO_AUDIT_RESULT_INVALID');
        }
        results.push({ page, unwrappedChinese: result.unwrappedChinese, clippedText: result.clippedText,
          ...(Number.isInteger(result.status) ? { status: result.status } : {}),
          ...(typeof result.evidence === 'string' ? { evidence: result.evidence } : {}) });
      } catch (error) {
        if (error?.code && /^PSEUDO_AUDIT_[A-Z0-9_]+$/.test(error.code)) throw fixedError(error.code);
        throw fixedError('PSEUDO_AUDIT_PAGE_FAILED');
      }
    }
  } catch (error) {
    failure = error?.code && /^PSEUDO_AUDIT_[A-Z_]+$/.test(error.code)
      ? fixedError(error.code) : fixedError('PSEUDO_AUDIT_OPERATION_FAILED');
  } finally {
    try {
      cleanup = await runtime.cleanup({ authenticated });
      if (!cleanup || cleanup.remaining !== 0 || cleanup.secretsRestored !== true) {
        throw fixedError('PSEUDO_AUDIT_CLEANUP_FAILED');
      }
    } catch {
      failure = fixedError('PSEUDO_AUDIT_CLEANUP_FAILED');
    }
  }

  if (failure) throw failure;
  return {
    pages: results,
    unwrappedChinese: results.reduce((total, result) => total + result.unwrappedChinese, 0),
    clippedText: results.reduce((total, result) => total + result.clippedText, 0),
    cleanup
  };
}

module.exports = { PAGE_CASES, runPseudoAudit };
