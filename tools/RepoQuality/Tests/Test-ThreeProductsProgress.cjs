/*
檔案整體說明
目的與定位：驗證共通層 1.3 進度 renderer 接受使用者核定的 02A 實機待補與完成狀態，避免如實記錄欠債時頁首卻產生資料錯誤。
主要功能與流程：讀取真實兩線資料及 HTML 最後一段原始 script，以單案 VM 和最小 DOM 執行；注入待補、完成及非法狀態的正負控制，確認待補不計為完成、完成仍須時間與 SHA、未知值仍顯示警告。
使用邊界與維護：只使用本機檔案與 detached 合成資料；不讀 User Secrets、不載入即時檔、不啟動瀏覽器計時器、不連任何服務。每次 render 都建立獨立 VM／DOM，測試結束由程序回收；離線頁面驗證不代表產品或實機驗收通過。
*/
'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const folder = path.resolve(__dirname, '../../../文件/規劃設計書/產品規劃書/只有三大產品共通的底層/實作計畫-三大產品共通層-1.3');
const html = fs.readFileSync(path.join(folder, '實作進度.html'), 'utf8');
const script = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));

// 每次建立真實資料的獨立快照，DOM 只收集可見輸出，不執行時間回呼或外部 script。
function render(change = () => {}) {
  const elements = new Map();
  const context = { window: {}, document: { getElementById(id) {
    if (!elements.has(id)) elements.set(id, { innerHTML: '', textContent: '' });
    return elements.get(id);
  } }, setTimeout() {} };
  vm.createContext(context);
  for (const lane of ['A', 'B']) vm.runInContext(fs.readFileSync(path.join(folder, `實作進度資料-線${lane}.js`), 'utf8'), context);
  change(context.window.PROGRESS_LANES);
  vm.runInContext(script, context);
  return elements;
}

test('02A pending state renders without warning and never counts as complete', () => {
  const elements = render(lanes => {
    for (const lane of Object.values(lanes)) for (const item of Object.values(lane.packages)) {
      item.status = '未開始'; item.finished = ''; item.commit = '';
    }
    lanes.A.packages.F3.status = '自動測試通過（實機待補）';
  });
  assert.equal(elements.get('warnings')?.innerHTML || '', '');
  assert.match(elements.get('table').innerHTML, /自動測試通過（實機待補）/);
  assert.match(elements.get('kpi').innerHTML, /0 \/ 15 包/);
});

test('02A complete state requires both finish time and commit', () => {
  const missing = render(lanes => {
    lanes.A.packages.F3.status = '完成'; lanes.A.packages.F3.finished = ''; lanes.A.packages.F3.commit = '';
  });
  assert.match(missing.get('warnings').innerHTML, /缺完成時間或提交 SHA/);
  const complete = render(lanes => {
    lanes.A.packages.F3.status = '完成'; lanes.A.packages.F3.finished = '2026-11-27 10:35'; lanes.A.packages.F3.commit = 'synthetic-evidence';
  });
  assert.equal(complete.get('warnings')?.innerHTML || '', '');
});

test('unknown progress state still fails closed with a visible warning', () => {
  const elements = render(lanes => { lanes.A.packages.F3.status = 'synthetic-unknown'; });
  assert.match(elements.get('warnings').innerHTML, /不是允許的值/);
});

test('actual lane snapshots render without a data warning', () => {
  const elements = render();
  assert.equal(elements.get('warnings')?.innerHTML || '', '');
});
