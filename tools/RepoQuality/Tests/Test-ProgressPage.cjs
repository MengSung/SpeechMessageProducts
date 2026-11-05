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

test('No alert hides the warning region and still renders the page', () => {
  const elements = render(null);
  assert.equal(elements.get('alert').hidden, true);
  assert.equal(elements.has('copyBtn'), false);
  assert.match(elements.get('meta').innerHTML, /1\.0\.0\.11\.Integration\.P1\.Worktree/);
  assert.match(elements.get('headline').innerHTML, /0／12 工作包/);
  assert.match(elements.get('tiles').innerHTML, /階段 1 驗收/);
  assert.match(elements.get('wps').innerHTML, /P0-05/);

  const activePage = render(original.alert);
  assert.match(activePage.get('alert').innerHTML, /D365 預設只讀/);
  assert.match(activePage.get('alert').innerHTML, /P1-01～03/);
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
  assert.match(elements.get('p1-wps').innerHTML, /P1-02[\s\S]*尚未完成複審/);
  assert.match(elements.get('p1-wps').innerHTML, /P1-08[\s\S]*P1-10/);
  assert.match(elements.get('p1-wps').innerHTML, /P1-12[\s\S]*由使用者在目標組織執行審核後的 apply/);
});
