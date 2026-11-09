/*
檔案整體說明

目的與定位：保護現行實作計畫 3.5 的 84 個工作包、固定五項完成定義及同階段單一
資訊卡，並驗證真實頁面的渲染與狀態統計。舊頁的歷史斷言仍由 Test-ProgressPage.cjs
唯讀執行，本檔不把頁面通過當成產品、D365 或 LINE 實機驗收。

主要功能與流程：讀取 HTML 的 DATA 與原始 script，每案建立專有資料副本及 DOM 替身，
執行實際 renderer。用真實資料核對唯一 ID、狀態、最多兩包、驗收證據、逐筆時區，
再注入混合狀態及惡意字串，確認摘要、階段列、工作包表及各文字區塊均安全且一致。
跳脫案例自帶合成進行卡與待使用者事項，不依賴當前是否有工作或缺少憑證；
另驗空清單與沒有待辦時的摘要，不會為了滿足測試而變造真實進度。

使用邊界與維護：不修改 DATA、產品設定或歷史證據，不建置、不連網、不讀秘密。
所有 Map、事件與 localStorage 替身屬於單次案例，無背景工作、計時器或需 Dispose
的資源。新增狀態或欄位須同步檢查真實 schema；不以固定舊測試數限制合法進度。
*/
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../../..');
const html = fs.readFileSync(path.join(root, '文件/規劃設計書/實作計畫-3.5/實作進度.html'), 'utf8');
const script = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));
const start = script.indexOf('const DATA = ');
const end = script.indexOf('\n};', start) + 3;
const original = JSON.parse(script.slice(start + 'const DATA = '.length, end - 1));
const timestamp = /^(?:補記 )?\d{4}-\d{2}-\d{2} \d{2}:\d{2} Asia\/Taipei \(UTC\+08:00\)/;
const escape = value => String(value).replace(/[&<>"']/g,
  c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

// 只模擬本頁使用的同步 DOM 契約；textContent 保留原文字，innerHTML 則必須由 renderer 跳脫。
function render(change = () => {}) {
  const elements = new Map();
  class Element {
    constructor() { this.innerHTML = ''; this.textContent = ''; this.listeners = {}; this.attributes = {}; }
    addEventListener(name, callback) { this.listeners[name] = callback; }
    setAttribute(name, value) { this.attributes[name] = value; }
    getAttribute(name) { return this.attributes[name]; }
  }
  for (const [, id] of html.matchAll(/\bid="([^"]+)"/g)) elements.set(id, new Element());
  const buttons = ['all', 'done', 'active', 'todo', 'you'].map(f => {
    const button = new Element(); button.dataset = { f }; return button;
  });
  const data = JSON.parse(JSON.stringify(original));
  change(data);
  vm.runInNewContext(script.slice(0, start) + 'const DATA = ' + JSON.stringify(data) + ';' + script.slice(end), {
    document: {
      getElementById: id => elements.get(id),
      querySelectorAll: selector => { assert.equal(selector, '#wpfilters button'); return buttons; },
      documentElement: new Element()
    },
    localStorage: { getItem: () => null, setItem: () => {} },
    matchMedia: () => ({ matches: false })
  });
  return { elements, buttons, data };
}

test('Plan35_Data_Has84UniquePackagesAndSupportedStatuses', () => {
  assert.equal(original.planVersion, '3.5');
  assert.equal(original.wps.length, 84);
  assert.equal(new Set(original.wps.map(w => w.id)).size, 84);
  for (const w of original.wps) assert.ok(['done', 'active', 'todo', 'you'].includes(w.status), w.id);
  assert.deepEqual(original.phases.map(p => original.wps.filter(w => w.phaseId === p.id).length),
    [18, 12, 12, 9, 6, 13, 7, 2, 5]);
  assert.equal(original.wps.filter(w => /^P2-09/.test(w.id)).length, 1, 'a/b still count as one package');
  assert.equal(original.wps.filter(w => w.id === 'P6-07').length, 1);
});

test('Plan35_ActiveLimitAndFiveDodItems_AgreeWithData', () => {
  const active = original.wps.filter(w => w.status === 'active');
  if (active.length > 2) assert.ok(original.notices.some(n =>
    n.level === 'warn' && /上限|超過/.test(n.title + n.body)), 'over-limit transition needs a visible warning');
  // P2-00 正在切換時可記錄超額舊狀態；切換完成後絕不可留下三包。
  if (original.milestones.find(m => m.id === 'P2-00').status === 'done') assert.ok(active.length <= 2);
  assert.deepEqual(original.active.map(a => a.id).sort(), active.map(a => a.id).sort());
  for (const a of original.active) {
    assert.equal(a.dod.length, 5, a.id);
    for (const item of a.dod) {
      assert.ok(['ok', 'no', 'na'].includes(item[1]));
      assert.ok(item[2].trim(), `${a.id}: every DoD result needs a plain-language reason`);
    }
  }
});

test('Plan35_AcceptedPackages_HaveTimeAndCommitEvidence', () => {
  for (const w of original.wps.filter(w => w.status === 'done')) {
    assert.match(w.doneAt, /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}(?: Asia\/Taipei \(UTC\+08:00\))?$/, w.id);
    assert.match(w.commit, /^[a-f0-9]{8,40}$/, w.id);
  }
});

test('Plan35_InformationalHistory_HasOneCardPerPhaseAndVisibleTaipeiTimestamps', () => {
  assert.match(original.lastUpdated, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\+08:00$/);
  assert.match(original.snapshot, timestamp);
  const phases = new Set();
  const { elements } = render();
  const visible = elements.get('infos').innerHTML;
  for (const n of original.notices.filter(n => n.level === 'info')) {
    assert.ok(!phases.has(n.phaseId), n.phaseId); phases.add(n.phaseId);
    assert.match(n.body, timestamp);
    assert.ok(visible.includes(escape(n.body)));
    assert.match(n.lastUpdated, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\+08:00$/);
    for (const step of n.steps) {
      assert.match(step, timestamp);
      assert.ok(visible.includes(escape(step)));
    }
  }
  assert.equal(original.notices.filter(n => n.id === 'phase-2-summary').length, 1);
  assert.doesNotMatch(visible, /<details[^>]*\bopen(?:[\s=>])/);
});

test('Plan35_RenderedTotalsAndFilters_AreDerivedFromStatuses', () => {
  const { elements, buttons, data } = render(d => {
    d.wps.filter(w => w.phaseId === '階段 2').forEach((w, i) => {
      w.status = ['done', 'active', 'you'][i] || 'todo';
    });
    d.active = [];
  });
  const count = status => data.wps.filter(w => w.status === status).length;
  assert.ok(elements.get('tiles').innerHTML.includes(`${count('done')}<span class="u">／84`));
  assert.ok(elements.get('tiles').innerHTML.includes(`${count('active')}<span class="u">包（上限 2）`));
  assert.match(elements.get('stages').innerHTML, /階段 2：已驗收 1、進行中 1、等你 1、未開始 9/);
  assert.equal((elements.get('wplist').innerHTML.match(/<td class="id">/g) || []).length, 84);
  buttons.find(b => b.dataset.f === 'active').listeners.click();
  assert.equal((elements.get('wplist').innerHTML.match(/<td class="id">/g) || []).length, count('active'));
  elements.get('wpq').listeners.input({ target: { value: 'no-such-package' } });
  assert.match(elements.get('wplist').innerHTML, /沒有符合的工作包/);
});

test('Plan35_EmptyActiveList_RendersWithoutCreatingWork', () => {
  const { elements } = render(d => {
    d.active = [];
    for (const w of d.wps) if (w.status === 'active') w.status = 'todo';
  });
  assert.match(elements.get('active').innerHTML, /目前沒有進行中的工作包/);
  assert.doesNotMatch(elements.get('active').innerHTML, /class="act"/);
  assert.match(elements.get('tiles').innerHTML, /0<span class="u">包（上限 2）/);
});

test('Plan35_Renderer_EscapesAllDisplayedTextSurfaces', () => {
  const attack = '<img src=x onerror="attack">&\'';
  const { elements } = render(d => {
    d.headline = attack;
    d.notices.push({ id: 'synthetic-warning', level: 'warn', title: attack, body: attack, steps: [] });
    d.active = [{ id: 'synthetic-active', startedAt: '', timebox: '', next: '', dod: [['', 'ok', '']] }];
    // 此案例要覆蓋「下一個需要你」文字出口，不能依賴真實使用者仍欠缺設定。
    d.userActions = [{ when: '', text: '', note: '', status: 'you' }];
    for (const w of d.wps) for (const key of ['name', 'deps', 'live', 'timebox', 'doneAt', 'commit', 'note']) w[key] = attack;
    for (const p of d.phases) for (const key of ['name', 'planned', 'actual']) p[key] = attack;
    for (const n of d.notices) { n.title = attack; n.body = attack; n.steps = [attack]; n.lastUpdated = attack; }
    for (const a of d.active) { a.startedAt = attack; a.timebox = attack; a.next = attack; a.dod.forEach(x => { x[0] = attack; x[2] = attack; }); }
    for (const a of d.userActions) for (const key of ['when', 'text', 'note']) a[key] = attack;
    for (const m of d.milestones) { m.name = attack; m.note = attack; }
    for (const g of d.gates) for (const key of ['item', 'at', 'note']) g[key] = attack;
    d.proposals = [{ id: 'PROP-X', problem: attack, proposal: attack, impact: attack, status: attack }];
    d.later = [{ id: 'LATER-X', text: attack, source: attack, addedAt: attack }];
    d.decisions = [{ at: attack, text: attack }];
    d.timeline = d.history33 = [{ t: attack, text: attack }];
  });
  assert.equal(elements.get('headline').textContent, attack, 'textContent must remain plain text');
  for (const id of ['tiles', 'alerts', 'you', 'stages', 'active', 'flow', 'wplist', 'miles',
    'gates', 'later', 'props', 'infos', 'decisions', 'timeline', 'hist33']) {
    const content = elements.get(id).innerHTML;
    assert.ok(content.includes(escape(attack)), id);
    assert.doesNotMatch(content, /<img src=x|<script>/, id);
  }
});

test('Plan35_NoPendingUserAction_ShowsNoneWithoutInventingRequest', () => {
  const { elements, data } = render(d => {
    d.userActions = [{ when: '2026-11-07', text: '合成已完成事項', note: '', status: 'done' }];
  });
  assert.match(elements.get('tiles').innerHTML, /目前沒有/);
  assert.equal(data.userActions.length, 1);
  assert.equal(data.userActions[0].status, 'done');
});

test('Plan35_Page_UsesUtf8WithoutBomAndOnlyCrlf', () => {
  const bytes = fs.readFileSync(path.join(root, '文件/規劃設計書/實作計畫-3.5/實作進度.html'));
  assert.notDeepEqual([...bytes.subarray(0, 3)], [239, 187, 191]);
  assert.equal(new TextDecoder('utf-8', { fatal: true }).decode(bytes), html);
  assert.ok(html.endsWith('\r\n'));
  assert.doesNotMatch(html.replace(/\r\n/g, ''), /[\r\n]/);
});

// 切換屬非工作包里程碑；核對版本、已完成階段與封存邊界，不能多算一包或動到設計版本。
test('Plan35_Switch_PreservesStage2AndArchivesOnlyTheOldPlan', () => {
  const phase2 = original.phases.find(p => p.id === '階段 2');
  assert.equal(phase2.actual, '約 2.0 天（2026-11-07～11-09）');
  assert.equal(Object.hasOwn(phase2, 'current'), false);
  for (const id of ['PROP-002', 'PROP-003', 'PROP-005', 'PROP-006', 'PROP-007']) {
    assert.equal(original.proposals.find(p => p.id === id).status, '已寫入 3.5（2026-11-09）', id);
  }
  assert.equal(original.milestones.filter(m => m.id === 'P3-00').length, 1);
  assert.equal(original.wps.some(w => w.id === 'P3-00'), false);
  assert.match(html, /<title>實作進度 3\.5<\/title>/);
  assert.match(html, /依設計 v3\.4/);
  assert.equal(fs.existsSync(path.join(root, '文件/規劃設計書/實作計畫-3.4')), false);
  assert.ok(fs.existsSync(path.join(root, '文件/規劃設計書/_封存_請勿依據/實作計畫-3.4/00_總覽與執行規則.md')));
  assert.ok(fs.existsSync(path.join(root, '文件/規劃設計書/實作計畫-3.5/驗收/P2_推播鏈驗收步驟.md')));
});
