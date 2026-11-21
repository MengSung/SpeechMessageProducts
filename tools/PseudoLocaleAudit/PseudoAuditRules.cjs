/*
 * 檔案整體說明
 * 目的與定位：提供偽語系頁面稽核的純函式規則，供瀏覽器 adapter 與離線測試共用。
 * 主要流程：逐字元追蹤全形括號 `［`／`］` 範圍，計算未包覆的 CJK 字元數；再依
 * scroll/client 尺寸與 computed overflow 分辨正常捲動和實際裁切。輸入輸出均為純值，
 * 不連網、不存 DOM、不讀取頁面或設定，也不將敏感本文寫入診斷。
 * 使用邊界與維護：函式不保留呼叫資料，沒有共享快取或可變全域狀態；缺漏或非法幾何
 * 值 fail closed 為固定錯誤碼。容器設定為 auto／scroll 的延伸內容是可捲動而非截斷。
 * 以 PseudoAuditRules.Tests.cjs 的合成 CJK 與幾何案例驗證，瀏覽器驗收另由入口負責。
 */
'use strict';

const CJK = /[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]/u;
const CLIPPING = new Set(['hidden', 'clip']);

/** 計算未位於偽語系全形括號區間的 CJK 字元數，不回傳原文或字元片段。 */
function countUnwrappedChinese(value) {
  if (typeof value !== 'string') throw new TypeError('PSEUDO_AUDIT_TEXT_INVALID');
  let wrapped = false;
  let count = 0;
  for (const character of value) {
    if (character === '［') {
      wrapped = true;
      continue;
    }
    if (character === '］') {
      wrapped = false;
      continue;
    }
    if (!wrapped && CJK.test(character)) count++;
  }
  return count;
}

/** 只把 hidden／clip 邊界外溢列為裁切；auto／scroll 的內容可由使用者捲動檢視。 */
function isClippedText(metrics) {
  if (!metrics || typeof metrics !== 'object') throw new TypeError('PSEUDO_AUDIT_GEOMETRY_INVALID');
  const dimensions = ['scrollWidth', 'clientWidth', 'scrollHeight', 'clientHeight'];
  if (dimensions.some(key => !Number.isFinite(metrics[key]) || metrics[key] < 0)
      || typeof metrics.overflowX !== 'string' || typeof metrics.overflowY !== 'string') {
    throw new TypeError('PSEUDO_AUDIT_GEOMETRY_INVALID');
  }
  const horizontalOverflow = metrics.scrollWidth > metrics.clientWidth + 1;
  const verticalOverflow = metrics.scrollHeight > metrics.clientHeight + 1;
  const horizontalClipped = horizontalOverflow && CLIPPING.has(metrics.overflowX);
  const verticalClipped = verticalOverflow && CLIPPING.has(metrics.overflowY);
  const ellipsisClipped = horizontalOverflow && metrics.textOverflow === 'ellipsis';
  return horizontalClipped || verticalClipped || ellipsisClipped;
}

module.exports = { countUnwrappedChinese, isClippedText };
