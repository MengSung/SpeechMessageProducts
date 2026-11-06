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
function render(alert, notices = [], updateData = () => {}) {
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
  data.notices = notices;
  updateData(data);
  const source = script.slice(0, dataStart) + 'const DATA = ' + JSON.stringify(data) + ';' + script.slice(dataEnd);
  vm.runInNewContext(source, { document: { getElementById: id => elements.get(id) || null }, navigator: {} });
  return elements;
}

function getWorkPackageRow(elements, id) {
  const rows = elements.get('planned-phases').innerHTML.match(/<li class="wp [^"]+">[\s\S]*?<\/li>/g) || [];
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
  assert.match(elements.get('planned-phases').innerHTML, /P0-05/);

  // 使用固定合成進度驗證 renderer，不把已完成的 Constants 歷史文案綁在每次更新的資訊本文。
  // P1-12 的真實只讀／不可逆邊界仍由下方獨立驗收案例逐字守門。
  const activePage = render({ title: 'Current slice', body: 'D365 預設只讀；合成進度', steps: [] });
  assert.match(activePage.get('alert').innerHTML, /D365 預設只讀/);
  assert.match(activePage.get('alert').innerHTML, /合成進度/);
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
  const ids = [...elements.get('planned-phases').innerHTML.matchAll(/<span class="id">(P1-\d+)<\/span>/g)]
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
  // 驗收里程碑與狀態是契約；P1-04 的階段性說明會持續更新，不綁定歷史文案。
  assert.match(getWorkPackageRow(elements, 'P1-01'), /class="wp is-done"/);
  assert.match(fs.readFileSync(path.join(root, 'wp-reports/P1-01-acceptance.md'), 'utf8'),
    /P1-01 的孤兒清理、測試整合及本機驗收已完成/);
  assert.match(getWorkPackageRow(elements, 'P1-04'), /class="wp is-done"/);
  assert.match(fs.readFileSync(path.join(root, 'wp-reports/P1-04-acceptance.md'), 'utf8'),
    /狀態：P1-04 本機驗收完成/);
  // 固定里程碑須有實際執行證據；不能只把畫面狀態由 active 換成 done。
  const p104Evidence = path.join(root, '.ccg/tasks/integration-p1/evidence/p104-final');
  const execution = JSON.parse(fs.readFileSync(path.join(p104Evidence, 'execution-evidence.json'), 'utf8'));
  assert.deepEqual(execution.releaseSolution, { trxCount: 16, passed: 3906, failed: 0, skipped: 0 });
  const required = JSON.parse(fs.readFileSync(path.join(p104Evidence, 'required-methods-and-product-tests.json'), 'utf8'));
  assert.deepEqual(Object.keys(required.methods).sort(),
    ['GetNotYetPaidFee', 'GetPaidFee', 'RetrieveEntityNoCache', 'RetrieveFeeByFetchXmlOrderNumber'].sort());
  for (const cases of Object.values(required.methods)) assert.ok(cases.length >= 3);
  assert.equal(required.productRegistration.length, 8);
  assert.match(elements.get('planned-phases').innerHTML, /P1-08[\s\S]*P1-10/);
  const p1_12 = getWorkPackageRow(elements, 'P1-12');
  assert.match(p1_12, /class="wp is-you"/);
  assert.doesNotMatch(p1_12, /undefined/);
  assert.match(p1_12, /歷史計畫 D24 指定使用者 apply/);
  assert.match(p1_12, /最新對話另有條件授權必要可逆測試變更/);
  assert.match(p1_12, /目前未確認，維持只讀/);
  assert.match(p1_12, /不可逆操作與正式資料仍不在授權範圍/);
});

test('Stages 2 through 8 render every planned package once and keep all packages not started', () => {
  const expected = [
    ['階段 2', 12],
    ['階段 3', 9],
    ['階段 4', 6],
    ['階段 5', 13],
    ['階段 6', 6],
    ['階段 7', 2],
    ['階段 8', 5]
  ];
  const packages = original.plannedWps || [];
  assert.equal(packages.length, 53, 'the plan defines 53 packages across stages 2-8');
  const ids = Array.from(packages, wp => wp.id);
  assert.equal(new Set(ids).size, 53, 'work package IDs must be unique');
  const packageById = new Map(Array.from(packages, wp => [wp.id, wp]));
  const planEvidence = {
    'P2-01': 'ExceptionOutputOptions',
    'P2-02': 'MemberNotificationService',
    'P2-03': 'ChannelTokenProtector',
    'P2-04': 'TestChannelToken',
    'P2-05': '{{t:Key}}',
    'P2-06': 'PushUtility 5 份',
    'P2-07': 'RowVersion',
    'P2-08': 'qps-ploc',
    'P2-09': 'ZXing.Net',
    'P2-10': 'new_textchangedon',
    'P2-11': 'mode=member',
    'P2-12': 'P2_推播鏈驗收步驟.md',
    'P3-01': '103 個關鍵字',
    'P3-02': '/api/line/webhook/{channelId}',
    'P3-03': 'LineSignature.Verify',
    'P3-04': 'ConnectionStrings:Platform',
    'P3-05': 'Channel<WebhookEnvelope>',
    'P3-06': 'Authorization Bearer',
    'P3-07': 'Routing_AllLegacyKeywords_SameIntent',
    'P3-08': 'OutboundLetterPlugin',
    'P3-09': 'P3_Webhook驗收步驟.md',
    'P4-01': 'HashCode',
    'P4-02': 'Money_TwdRoundTrip_NoDecimalLoss',
    'P4-03': 'AllotQueryHandler',
    'P4-04': 'Payments:Sinopac:Profiles:',
    'P4-05': 'Payments.AspNetCore',
    'P4-06': '永豐測試環境',
    'P5-01': 'PluginShellBase',
    'P5-02': 'PluginHarness',
    'P5-03': 'shadow 指令',
    'P5-04': 'ChurchLocaleChangedPlugin',
    'P5-05': 'WeeklyReportStatisticsPlugin',
    'P5-06': 'PresentRecordPlugin',
    'P5-07': 'LessonProgressionPlugin',
    'P5-08': 'AppointmentSigningActivity',
    'P5-09': 'MeetingQrCodeActivity',
    'P5-10': 'ContactAnniversaryActivity',
    'P5-11': 'FeeAllotPlugin',
    'P5-12': 'OutboundLetterPlugin',
    'P5-13': 'Line.NotifyBot',
    'P6-01': 'SmallGroupQrCodeUtility',
    'P6-02': 'GoodShepherd.Core/WeeklyReports',
    'P6-03': 'P2-06',
    'P6-04': 'AddToolUtility',
    'P6-05': 'I18nCheck',
    'P6-06': 'Microsoft.AspNetCore.* 2.2.x',
    'P7-01': '--production',
    'P7-02': 'P7_組織切換手冊.md',
    'P8-01': 'new_offeringcategory',
    'P8-02': 'Role.PastorCouple',
    'P8-03': 'ChurchTermWhitelist',
    'P8-04': 'PseudoLocaleAudit',
    'P8-05': 'Test-TextEncoding -Scope All'
  };
  assert.equal(Object.keys(planEvidence).length, 53);

  for (const [phaseId, count] of expected) {
    const phasePackages = Array.from(packages).filter(wp => wp.phaseId === phaseId);
    assert.equal(phasePackages.length, count, `${phaseId} package count must match plan 3.3`);
    assert.ok(phasePackages.every(wp => wp.status === 'todo'), `${phaseId} must remain not started`);
    assert.ok(phasePackages.every(wp => wp.scope && wp.verification), `${phaseId} packages need scope and acceptance evidence`);
    const phase = Array.from(original.phases).find(item => item.id === phaseId);
    assert.equal(phase.total, count);
    assert.equal(phase.done, 0);
    assert.equal(phase.active, 0);
  }

  for (const [id, marker] of Object.entries(planEvidence)) {
    const wp = packageById.get(id);
    assert.ok(wp, `${id} must be present`);
    assert.ok(`${wp.name} ${wp.scope} ${wp.verification} ${wp.prerequisite || ''}`.includes(marker),
      `${id} must preserve plan-specific detail: ${marker}`);
  }

  const rendered = render(original.alert).get('planned-phases').innerHTML;
  assert.ok(rendered);
  for (const id of ids) {
    assert.match(rendered, new RegExp(`<span class="id">${id}</span>`));
  }
});

test('Planned phase disclosures show a visible expanded-state indicator', () => {
  const rendered = render(original.alert).get('planned-phases').innerHTML;
  assert.match(rendered,
    /<details class="planned-phase">[\s\S]*?<summary>[\s\S]*?<span class="planned-phase-toggle" aria-hidden="true"><\/span>/);
  assert.match(html, /\.planned-phase-toggle::before\s*\{[^}]*content:\s*["']\+["']/);
  assert.match(html, /\.planned-phase\[open\] \.planned-phase-toggle::before\s*\{[^}]*content:\s*["']-["']/);
});

// 若重新鋪開 P0/P1、只用 CSS 隱藏舊清單、漏整合任何階段，或預設展開明細，必須失敗。
test('All nine stages share one closed work-package disclosure list without standalone P0/P1 sections', () => {
  const elements = render(original.alert);
  const rendered = elements.get('planned-phases').innerHTML;
  const ids = [...rendered.matchAll(/<span class="planned-phase-id">([^<]+)<\/span>/g)]
    .map(match => match[1]);
  assert.deepEqual(ids, Array.from({ length: 9 }, (_, i) => `階段 ${i}`));
  assert.equal((rendered.match(/<details class="planned-phase">/g) || []).length, 9);
  assert.doesNotMatch(rendered, /<details[^>]*\bopen(?:[\s=>])/);
  assert.doesNotMatch(html, /<h2>階段 [01] 工作包/);
  assert.doesNotMatch(html, /\bid="(?:wps|p1-wps)"/);
  assert.equal(elements.has('wps'), false);
  assert.equal(elements.has('p1-wps'), false);
  assert.match(html, /<h2>階段 0～8 工作包/);
});

// 單獨計數人工操作，審查只是證據而非第十九包；摘要必須涵蓋所有狀態，不能漏掉 P1-12。
test('Unified summaries exclude review milestones and account for user-operated work', () => {
  const rendered = render(original.alert).get('planned-phases').innerHTML;
  const summaries = [...rendered.matchAll(/<summary>([\s\S]*?)<\/summary>/g)]
    .map(match => match[1]);
  assert.equal(summaries.length, 9);
  const groups = [original.wps.filter(wp => !wp.milestone), original.p1Wps,
    ...original.phases.slice(2).map(phase => original.plannedWps.filter(wp => wp.phaseId === phase.id))];
  for (const [index, packages] of groups.entries()) {
    const done = packages.filter(wp => wp.status === 'done').length;
    const active = packages.filter(wp => wp.status === 'active').length;
    const todo = packages.filter(wp => wp.status === 'todo').length;
    const you = packages.filter(wp => wp.status === 'you').length;
    assert.equal(done + active + todo + you, original.phases[index].total);
    assert.ok(summaries[index].includes(`${done}/${packages.length} 驗收`));
    assert.ok(summaries[index].includes(`${active} 進行中`));
    assert.ok(summaries[index].includes(`${todo} 未開始`));
    if (you) assert.ok(summaries[index].includes(`${you} 待你操作`));
  }
  assert.match(summaries[1], /目前階段/);
  assert.match(rendered, /class="wp is-milestone"/);

  // 固定合成四項完成、六項未開始及兩項人工操作，驗證計數不隨正式進度改變而漂移。
  const changed = render(null, [], data => {
    data.p1Wps.forEach((wp, index) => { wp.status = index < 4 ? 'done' : index < 10 ? 'todo' : 'you'; });
  })
    .get('planned-phases').innerHTML;
  assert.match(changed, /6 未開始、2 待你操作/);
});

// 所有原始欄位都要渲染且只出現一列；注入同名與 HTML 內容，保護身分保留與輸出跳脫。
test('Integrated P0/P1 entries preserve every evidence field and never merge same-name records', () => {
  const elements = render(original.alert);
  const escape = value => String(value).replace(/[&<>"]/g,
    c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  for (const wp of [...original.wps, ...original.p1Wps].filter(wp => !wp.milestone)) {
    const row = getWorkPackageRow(elements, wp.id);
    assert.ok(row, `${wp.id} must remain visible inside its disclosure`);
    assert.ok(row.includes(`class="wp is-${wp.status}"`));
    for (const key of ['name', 'note', 'commit', 'time']) {
      if (wp[key]) assert.ok(row.includes(escape(wp[key])), `${wp.id} must preserve ${key}`);
    }
    assert.equal(elements.get('planned-phases').innerHTML
      .split(`<span class="id">${wp.id}</span>`).length - 1, 1);
  }
  const changed = render(null, [], data => {
    data.wps[0].name = '<shared-name>';
    data.p1Wps[0].name = '<shared-name>';
    data.wps[0].note = '<script>synthetic</script>&"';
  }).get('planned-phases').innerHTML;
  assert.equal(changed.split('&lt;shared-name&gt;').length - 1, 2);
  assert.match(changed, /&lt;script&gt;synthetic&lt;\/script&gt;&amp;&quot;/);
  assert.doesNotMatch(changed, /<shared-name>|<script>synthetic/);
});

test('Future work packages preserve external-operation and isolation stop boundaries', () => {
  const packageById = new Map(Array.from(original.plannedWps || [], wp => [wp.id, wp]));
  const text = id => {
    const wp = packageById.get(id);
    assert.ok(wp, `${id} must be present`);
    return `${wp.scope} ${wp.verification} ${wp.prerequisite || ''}`;
  };

  assert.match(text('P2-04'), /TestChannelToken/);
  assert.match(text('P2-04'), /TestLineUserId/);
  assert.match(text('P2-04'), /秘密不得記錄/);
  assert.match(text('P3-04'), /平台 SQL/);
  assert.match(text('P3-04'), /不得.*記憶體|記憶體.*不得/);
  assert.match(text('P3-09'), /使用者.*測試主機/);
  assert.match(text('P5-02'), /不修改.*舊.*原檔/);
  assert.match(text('P5-03'), /goodshepherdback/);
  assert.match(text('P5-03'), /apply、switch、smoke.*使用者/);
  assert.match(text('P7-01'), /Codex.*不連.*正式組織/);
  assert.match(text('P7-02'), /apply／switch 由使用者審核及執行/);
  assert.match(text('P8-01'), /同名.*保留/);
  assert.match(text('P8-01'), /List_DuplicateId_PublicationRejected/);
  assert.match(text('P8-01'), /重複 ID.*拒絕發布並診斷/);
  assert.match(text('P8-01'), /使用者審核執行/);
  assert.match(text('P8-02'), /中文文字.*停止|停止.*中文文字/);
  assert.match(text('P8-03'), /Save_ChurchIdFromRequestIgnored/);
  assert.match(text('P8-03'), /伺服器依登入者判定/);
  assert.match(text('P8-03'), /不接受 request 的教會 ID/);
  assert.match(text('P8-04'), /人工登入/);
  assert.match(text('P8-04'), /不保存.*帳密|帳密.*不保存/);
  assert.match(text('P8-05'), /v3\.3.*已完成/);
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
  assert.equal(original.phases.reduce((sum, phase) => sum + phase.total, 0), 83);
  assert.equal(total.meter.total, 83);
  assert.match(html, /9 個階段、83 個工作包/);
  assert.equal(Number(total.value), totalDone);
  assert.equal(total.meter.done, totalDone);
  assert.equal(total.meter.active, original.phases.reduce((sum, phase) => sum + phase.active, 0));
});

// 若資訊再次套用警告語意、詳細內容預設展開，或警告被資訊收合容器吞掉，以下案例必須失敗。
test('Informational history defaults to a closed neutral disclosure', () => {
  const elements = render({ title: '<progress>', body: '<history>', steps: ['old result'] });
  const content = elements.get('alert').innerHTML;
  assert.match(content, /<details class="notice info"[^>]*>/);
  assert.doesNotMatch(content, /<details[^>]*\bopen(?:[\s=>])/);
  assert.match(content, /一般資訊/);
  assert.match(content, /&lt;progress&gt;/);
  assert.match(content, /&lt;history&gt;/);
  assert.match(content, /old result/);
  assert.doesNotMatch(content, /role="alert"/);
});

test('Current warnings and errors remain separate from collapsed history', () => {
  const elements = render({ title: 'History', steps: ['past warning resolved'] }, [
    { level: 'warn', title: 'Pending evidence', body: '<check>' },
    { level: 'error', title: 'Build blocked', body: 'real failure', steps: ['fix required'] }
  ]);
  const issues = elements.get('notices');
  assert.ok(issues, 'current notices need a region separate from informational history');
  assert.equal(issues.hidden, false);
  assert.match(issues.innerHTML, /<article class="notice warn" role="status">/);
  assert.match(issues.innerHTML, /注意事項[\s\S]*Pending evidence/);
  assert.match(issues.innerHTML, /<article class="notice error" role="alert">/);
  assert.match(issues.innerHTML, /錯誤／阻擋[\s\S]*Build blocked/);
  assert.match(issues.innerHTML, /&lt;check&gt;/);
  assert.doesNotMatch(issues.innerHTML, /<details[^>]*\bopen(?:[\s=>])/);
  assert.doesNotMatch(issues.innerHTML, /past warning resolved/);
  assert.doesNotMatch(elements.get('alert').innerHTML, /Build blocked/);
});

test('Empty notices hide their region and unknown severity cannot inject markup', () => {
  const empty = render(null);
  assert.ok(empty.get('notices'));
  assert.equal(empty.get('notices').hidden, true);
  const elements = render(null, [null, { level: '\" onclick=\"attack', title: '<unknown>', body: 'retained' }]);
  const content = elements.get('notices').innerHTML;
  assert.match(content, /<details class="notice info"/);
  assert.match(content, /&lt;unknown&gt;/);
  assert.doesNotMatch(content, /onclick/);
});
