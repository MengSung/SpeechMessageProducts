// 驗證真實進度頁的渲染程式；小型 DOM 測試替身只記錄可見內容與事件，不執行外部連線。
// 注入無警示、無提示詞與正常提示詞三種資料，保護空值容錯與既有複製功能。
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../../..');
const html = fs.readFileSync(path.join(root, '文件/規劃設計書/實作計畫-3.3/實作進度.html'), 'utf8');
const script = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));
const dataStart = script.indexOf('const DATA = {');
const dataEnd = script.indexOf('\n};', dataStart) + 3;
const original = vm.runInNewContext(script.slice(dataStart, dataEnd) + '\nDATA;');

// 每個案例持有自己的 DOM 與資料副本，避免警示事件或內容從上一個案例滲入。
function render(alert) {
  const elements = new Map();
  class Element {
    constructor(id) { this.id = id; this.hidden = false; this.listeners = new Map(); this.children = []; this.content = ''; }
    set innerHTML(value) {
      for (const id of this.children) elements.delete(id);
      this.children = [];
      this.content = String(value);
      for (const match of this.content.matchAll(/\bid="([^"]+)"/g)) {
        elements.set(match[1], new Element(match[1]));
        this.children.push(match[1]);
      }
    }
    get innerHTML() { return this.content; }
    addEventListener(name, listener) { this.listeners.set(name, listener); }
  }
  for (const match of html.slice(0, html.lastIndexOf('<script>')).matchAll(/\bid="([^"]+)"/g)) {
    elements.set(match[1], new Element(match[1]));
  }
  const data = JSON.parse(JSON.stringify(original));
  data.alert = alert;
  const source = script.slice(0, dataStart) + 'const DATA = ' + JSON.stringify(data) + ';' + script.slice(dataEnd);
  vm.runInNewContext(source, { document: { getElementById: id => elements.get(id) || null }, navigator: {} });
  return elements;
}

function getWorkPackageRow(elements, id) {
  const rows = elements.get('p1-wps').innerHTML.match(/<li class="wp [^"]+">[\s\S]*?<\/li>/g) || [];
  return rows.find(row => row.includes(`<span class="id">${id}</span>`)) || '';
}

test('No alert hides the warning region and still renders the page', () => {
  const elements = render(null);
  assert.equal(elements.get('alert').hidden, true);
  assert.equal(elements.has('copyBtn'), false);
  assert.match(elements.get('meta').innerHTML, /1\.0\.0\.11\.Integration\.P1\.Worktree/);
  // 完成數必須等於實際工作包狀態，不把 P1 初始的零永久寫死而造成後續合法更新失敗。
  const accepted = original.p1Wps.filter(wp => wp.status === 'done').length;
  assert.match(elements.get('headline').innerHTML, new RegExp(`${accepted}／12 工作包`));
  assert.match(elements.get('tiles').innerHTML, /階段 1 驗收/);
  assert.match(elements.get('wps').innerHTML, /P0-05/);

  const activePage = render(original.alert);
  assert.match(activePage.get('alert').innerHTML, /D365 預設只讀/);
  assert.match(activePage.get('alert').innerHTML, /P1-04 Constants 已抽取/);
  assert.match(activePage.get('alert').innerHTML, /P1-03 已正式驗收/);
  assert.match(activePage.get('alert').innerHTML, /20261105-140350/);
});

test('Alert without prompt omits copy button and full prompt area', () => {
  const elements = render({ title: 'Pending input', body: 'Local check only', steps: [] });
  assert.equal(elements.get('alert').hidden, false);
  assert.match(elements.get('alert').innerHTML, /Pending input/);
  assert.equal(elements.has('copyBtn'), false);
  assert.equal(elements.has('promptText'), false);
});

test('Alert with prompt preserves escaped prompt and copy handler', () => {
  const elements = render({ title: 'Review', body: 'Synthetic text', steps: [], prompt: '<sample>' });
  assert.equal(elements.get('alert').hidden, false);
  assert.equal(elements.get('copyBtn').listeners.has('click'), true);
  assert.match(elements.get('alert').innerHTML, /&lt;sample&gt;/);
});

test('Stage 1 progress lists all twelve packages and preserves the live acceptance boundary', () => {
  const elements = render(original.alert);
  const ids = [...elements.get('p1-wps').innerHTML.matchAll(/<span class="id">(P1-\d+)<\/span>/g)]
    .map(match => match[1]);
  assert.deepEqual(ids, Array.from({ length: 12 }, (_, index) => `P1-${String(index + 1).padStart(2, '0')}`));
  const p1_02 = getWorkPackageRow(elements, 'P1-02');
  assert.ok(p1_02);
  assert.match(p1_02, /class="wp is-done"/);
  assert.match(p1_02, /20261105-113854 Gemini＋Claude 複審 dualModelComplete=true，Critical／Warning 均無/);
  assert.doesNotMatch(p1_02, /最新版雙模型複審尚未完成/);
  const p1_03 = getWorkPackageRow(elements, 'P1-03');
  assert.match(p1_03, /class="wp is-done"/);
  assert.match(p1_03, /20261105-140350 dualModelComplete=true/);
  // 這是人工驗收後的固定里程碑，不以彼此相等就推論實際執行或實機通過。
  assert.match(fs.readFileSync(path.join(root, 'wp-reports/P1-03-acceptance.md'), 'utf8'),
    /狀態：本工作包驗收完成/);
  assert.match(getWorkPackageRow(elements, 'P1-04'), /四個舊方法來源已定位/);
  assert.match(elements.get('p1-wps').innerHTML, /P1-08[\s\S]*P1-10/);
  const p1_12 = getWorkPackageRow(elements, 'P1-12');
  assert.match(p1_12, /class="wp is-you"/);
  assert.doesNotMatch(p1_12, /undefined/);
  assert.match(p1_12, /歷史計畫 D24 指定使用者 apply/);
  assert.match(p1_12, /最新對話另有條件授權必要可逆測試變更/);
  assert.match(p1_12, /目前未確認，維持只讀/);
  assert.match(p1_12, /不可逆操作與正式資料仍不在授權範圍/);
});

test('Summary cards and phase meters agree with accepted work packages', () => {
  // 頁首、卡片與階段表必須共用同一驗收數；不能只改文字而留下零進度的矛盾。
  const accepted = original.p1Wps.filter(wp => wp.status === 'done').length;
  const active = original.p1Wps.filter(wp => wp.status === 'active').length;
  const stage = original.phases.find(phase => phase.id === '階段 1');
  const tile = original.tiles.find(item => item.label === '階段 1 驗收');
  assert.equal(stage.done, accepted);
  assert.equal(stage.active, active);
  assert.equal(Number(tile.value), accepted);
  assert.equal(tile.meter.done, accepted);
  assert.equal(tile.meter.active, active);
  const totalDone = original.phases.reduce((sum, phase) => sum + phase.done, 0);
  const total = original.tiles.find(item => item.label === '全計畫進度');
  assert.equal(Number(total.value), totalDone);
  assert.equal(total.meter.done, totalDone);
  assert.equal(total.meter.active, original.phases.reduce((sum, phase) => sum + phase.active, 0));
});
