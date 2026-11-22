/*
 * 檔案整體說明
 * 目的與定位：驗證 PseudoLocaleAudit 的偽語系包覆與可視截斷判斷契約。
 * 主要流程：以合成繁中文字串及元素幾何／overflow 值呼叫純規則函式，斷言未包覆
 * 字元數、已包覆字元數，以及捲動容器與真正裁切元素的分類。測試不啟動瀏覽器、
 * Host、SDK 或外部服務；輸入不含帳密、token、cookie、個資或真實頁面本文。
 * 使用邊界與維護：每案只建立區域純值，沒有共享可變狀態或需 Dispose 的資源。
 * 修改規則時須維持只輸出必要計數／分類，不得為方便除錯而保存頁面原文；以
 * `node --test tools/PseudoLocaleAudit/Tests/PseudoAuditRules.Tests.cjs` 驗證。
 */
'use strict';
const assert = require('node:assert/strict');
const { test } = require('node:test');
const {
  countUnwrappedChinese,
  isClippedText,
  shouldInspectTextNode
} = require('../PseudoAuditRules.cjs');

test('PseudoAudit_UnwrappedCjkCharacters_AreCounted', () => {
  assert.equal(countUnwrappedChinese('教會［名詞］新朋友'), 5);
});

test('PseudoAudit_BracketedCjkCharacters_AreNotCounted', () => {
  assert.equal(countUnwrappedChinese('［教會名詞與新朋友］'), 0);
});

test('PseudoAudit_ScrollContainerOverflow_IsNotClassifiedAsClipping', () => {
  assert.equal(isClippedText({
    scrollWidth: 240, clientWidth: 120, scrollHeight: 20, clientHeight: 20,
    overflowX: 'auto', overflowY: 'visible'
  }), false);
});

test('PseudoAudit_HiddenOverflowBeyondClientExtent_IsClassifiedAsClipping', () => {
  assert.equal(isClippedText({
    scrollWidth: 240, clientWidth: 120, scrollHeight: 20, clientHeight: 20,
    overflowX: 'hidden', overflowY: 'visible'
  }), true);
});

test('PseudoAudit_VerticalClipping_IsDetectedWhenHorizontalAxisCanScroll', () => {
  assert.equal(isClippedText({
    scrollWidth: 240, clientWidth: 120, scrollHeight: 40, clientHeight: 20,
    overflowX: 'auto', overflowY: 'hidden'
  }), true);
});

test('PseudoAudit_DynamicDataMarkedByView_IsExcludedFromResourceCopyAudit', () => {
  assert.equal(shouldInspectTextNode({ pseudoLocaleExempt: 'dynamic' }), false);
  assert.equal(shouldInspectTextNode({ pseudoLocaleExempt: null }), true);
});
