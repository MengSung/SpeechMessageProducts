ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-acceptance-progress

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1驗收紀錄/進度統計的增量唯讀審查。禁止執行、擴讀檔案/機密、修改或再呼叫模型。請檢查以下報告有無過度宣稱；進度卡片/phase是否與WP相同；測試是否真正抓出舊零值。SDK產品本輪未變，前run20261105-140350兩模型無Critical/Warning，dualModelComplete=true、quotaBlocked=false、degradedFallback=false；本地29快照核對，SDK新鮮96/96+101/101。全方案1836、CI1145、各新模組10份coverage均過；無hosted/LINE/D365寫入證據，不能要求其為SDK WP條件。新一致性測試先紅0≠1，修後5/5。請分Critical/Warning/Info，不重複要求改核定net462產品/net48測試矩陣。

## tools/RepoQuality/Tests/Test-ProgressPage.cjs
```
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
  assert.match(activePage.get('alert').innerHTML, /P1-02 複審完成，接續 P1-04/);
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
  assert.match(getWorkPackageRow(elements, 'P1-04'), /四個舊方法來源已定位/);
  assert.match(elements.get('p1-wps').innerHTML, /P1-08[\s\S]*P1-10/);
  const p1_12 = getWorkPackageRow(elements, 'P1-12');
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
});

```

## wp-reports/P1-03-acceptance.md
```
# P1-03 LINE SDK 雙目標正式驗收

日期：2026-11-05。狀態：本工作包驗收完成；**整個 P1 尚未完成**。

## 需求 → 實作 → 測試 → 執行證據

| 需求 | 實作位置 | 決定性測試與結果 |
| --- | --- | --- |
| 六類訊息先鎖舊行為，再雙目標逐字一致 | `Line.Messaging.Tests/LineSdkGoldenTests.cs`、`Golden/*.json` | Text、Flex、Template、Image、QuickReply、RichMenu；舊 net10 capture 6/6、搬後兩宿主各 6/6；不自動更新 golden |
| 產品 net462／net10，Framework 測試宿主 net48 | 兩個 SDK csproj、`LineSdkFoundationTests.cs` | 反射驗載入產品是 net462，不把 net48 當部署輸出；本次雙宿主全套 96/96、101/101 |
| Sender 長度 1～20、https 圖示 | `Line.Messaging/Messages/Sender/Sender.cs`、`LineMessageModelTests.cs` | 邊界、過長、非 https 拒絕與序列化 |
| Background 線性漸層 | `Line.Messaging/Flex/Style/Background.cs`、`LineMessageModelTests.cs` | 實際 JSON 欄位與既有 Flex 相容 |
| webhookEventId／redelivery | `Line.Messaging/Webhooks/`、`LineSdkFoundationTests.cs` | 官方格式形狀的合成 JSON 反序列化，舊 payload 缺選用欄位仍相容 |
| 傳輸無關 raw-byte HMAC 與固定時間比對 | `Line.Messaging/LineSignature.cs` | 獨立 HMAC 向量、合法／篡改／格式錯誤／空 secret；HMAC 確定性 Dispose |
| 保留並接妥原 HTTP webhook 入口 | `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs`、`WebhookRawSignatureTests.cs` | 先 raw bytes 驗簽再 strict UTF-8；錯誤 charset 不得重編碼，tamper 拒絕 |
| 共用 HttpClient 不跨 channel 洩漏 | `ChannelHttpTransport.cs`、Messaging／LIFF client 與 lifecycle tests | 每 request Authorization，不改 DefaultRequestHeaders；A/B 1000 次交錯、排空及 fault assertions |
| stream／response／request／content 唯一 owner | `BorrowedStreamContent.cs`、`ContentStream.cs` 及 lifecycle tests | 成功／傳輸失敗／Dispose race／重複 Dispose；借入 stream/client 不關閉，response 只轉交單一 owner |
| 失敗不可洩漏遠端 body／token／使用者 URL | `Extensions/HttpResponseMessageExtensions.cs`、client 及 lifecycle tests | HTTP 故障固定安全訊息，原始失敗仍傳回 host，不在 SDK 新建 LINE notifier |

全部要求均有實際案例；新增／保留測試未刪除以換取通過。
目前只有三份 SDK 來源使用 `#if`，未達正式計畫超過 30 檔的停止條件。

## TDD 與相容性證據

- `artifacts/p1-03/capture/`：舊 net10 六類合成 golden 捕獲 6/6。
  更早 `p1-03-golden-baseline.log` 曾因草稿與實際輸出不一致而 5 紅／1 綠；
  它不是成功證據，未刪除或改寫。
- `artifacts/p1-03/dual-green/`：兩目標各 6/6 逐字比對。
- `artifacts/p1-03/features-red/`：18 失敗／8 通過；
  `features-green/`、`complete/` 保存功能實作後綠燈。
- `artifacts/p1-03/lifecycle-red/`：23/23 失敗；
  `liff-red/`：11/11 失敗；`webhook-raw-red/`：1 紅／1 綠。
  對應安全修補後結果在 `security-green/`，沒有把 Mock 通過稱為 LINE 實機通過。
- 最新重新建置與完整 SDK 測試：
  `artifacts/p1-03-final-sdk.log`、`artifacts/p1-03-final-sdk/`；
  net48 載 net462 **96/96**、net10 **101/101**，0 失敗／略過。

## 審查收斂

最終 run：
`.ccg/dual-model-runs/20261105-140350-integration-p1-03-final-reviewer/`。
機器摘要為 `dualModelComplete=true`、`quotaBlocked=false`、
`degradedFallback=false`；兩模型均無新增 Critical／Warning。

- 舊 C1：raw 驗簽未接原 HTTP 入口，已修且直接入口測試證實，兩模型確認關閉。
- 舊 W2：content／response 重複 Dispose，已改單一 owner，兩模型確認關閉。
- 舊 W1：net48 測試與 net462 產品不同是 D21 核定矩陣，不是缺陷。
- disposed 的 `TryComputeLength` 改回 false 建議未提供可達故障：
  request 在整個 await 完成後才 Dispose；維持明確 ObjectDisposed 防禦誤用。
- Claude 的既有 `VerifyContentPreparationAsync` JSON catch 行為 Info
  不在本工作包異動區，保留後續追蹤，不冒稱本輪已修。
- 外模沒有執行測試、也未重新計算 hashes；主會話已本地比對 29 份固定快照，
  27 份來源／golden 正規化文字相同，2 份 csproj 僅 review 副本省略公開作者 metadata，
  實際產品仍未改。`artifacts/p1-03-review-snapshot-integrity.json`。

## 完整守門與資源契約

本輪完整方案、過渡 filter、Debug tracer、打包、各新模組／目標 coverage、
編碼及 duplicates 的逐項證據沿用同輪 `wp-reports/P1-02-acceptance.md`：
完整方案 **1836/1836**，本地 filter **1145/1145**；不是 hosted Actions。
SDK 是既有專案，未將整個歷史 SDK 宣稱為新模組 80% coverage；必要新增功能、
安全及生命週期各有直接斷言。原 NoWarn 不擴大，沒有降低既有門檻。

SDK 不保存 Session、HttpContext、租戶快取或背景佇列；
transport 由組合根管理，SDK 的 per-client credential 不寫共享 headers。
操作結束釋放 request/response；下載 response 最長由回傳 ContentStream
唯一持有至 caller Dispose；borrowed transport/stream 的最終 Dispose 由 caller 負責。
Dispose 停止新請求，不擅自取消另一 client 的共用 HttpClient。
真正傳輸失敗保持傳回 host 最終共用通知邊界，不在 SDK 自行重複通知。

修改區使用繁中維護註解、UTF-8 無 BOM／CRLF／final CRLF；
交付前再跑 Changed 編碼與重複守門，原 timestamp 模型證據不改 bytes。
本工作包不需要 VM／LINE 實際寫入，沒有實機推播或 hosted CI 證據。
未改正式計畫，不需 Word 修正報告。本工作包已知阻斷性缺陷：無。

```

## 進度單一資料
```json
{
  "snapshot": "2026-11-05 14:15（P1-02／03正式驗收；繼續P1-04）",
  "headline": "階段 1 目前 <strong>2／12 工作包列為驗收完成</strong>（P1-02、P1-03）。全方案1,836/1,836、ChurchReport612/612，本地過渡CI1,145/1,145；SDK最新重跑96/96、101/101，雙模型最終複審無Critical／Warning。Core五組已搬移，P1-04其餘候選及P1-01、05～12仍待完成。",
  "tiles": [
    {
      "label": "全計畫進度",
      "value": "20",
      "unit": "/ 83 工作包",
      "sub": "P0 18／18；P1 2／12 已驗收，2包收斂中",
      "meter": {
        "done": 20,
        "active": 2,
        "total": 83
      }
    },
    {
      "label": "階段 0 進度",
      "value": "18",
      "unit": "/ 18 工作包",
      "sub": "歷史階段 0 驗收完成",
      "meter": {
        "done": 18,
        "active": 0,
        "total": 18
      }
    },
    {
      "label": "階段 1 驗收",
      "value": "2",
      "unit": "/ 12 工作包",
      "sub": "P1-02／03正式驗收；P1-01、04～12尚未完成",
      "meter": {
        "done": 2,
        "active": 2,
        "total": 12
      }
    },
    {
      "label": "最新完整本機 Release",
      "value": "1,836 / 1,836",
      "unit": "0 失敗、0 略過",
      "sub": "16份TRX，ChurchReport612/612；本地filter14份TRX=1145/1145"
    },
    {
      "label": "最新完整本機建置",
      "value": "0 錯誤",
      "unit": "完整方案與Core雙目標建置成功",
      "sub": "Core strict 0警告；既有host nullable與核定Obsolete提示保留，沒有新增NoWarn"
    }
  ],
  "p1Wps": [
    {
      "id": "P1-01",
      "name": "清理孤兒專案與檔案",
      "status": "active",
      "note": "51候選引用稽核、15原效能案例加1並行案例保留、smoke2秒可控時鐘與本機全量守門已有證據；DevExpress合法新來源／hosted證據尚未取得，待正式報告與審查收斂，不標全包完成。"
    },
    {
      "id": "P1-02",
      "name": "架構測試（R4：相依只能往下）",
      "status": "done",
      "note": "真實MSBuild逐目標/NuGet assets、12模型反例、10 XML/實際方案與2程序故障注入共24案通過；Localization全套67/67另含原43案。20261105-113854 Gemini＋Claude 複審 dualModelComplete=true，Critical／Warning 均無；本輪00§8、每目標coverage、打包、編碼與duplicates均過。正式驗收 wp-reports/P1-02-acceptance.md。"
    },
    {
      "id": "P1-03",
      "name": "Line.Messaging 雙目標與 Webhook 安全",
      "status": "done",
      "note": "六類golden先舊net10綠、雙宿主逐字相同；Sender／Background／webhook欄位與raw-byteHMAC及舊HTTP入口均完成。最新96/96、101/101，29份快照本地核對；最終雙模型20261105-140350 dualModelComplete=true、無降級／Critical／Warning，舊C1／W2關閉。正式驗收wp-reports/P1-03-acceptance.md。"
    },
    {
      "id": "P1-04",
      "name": "ToolUtility.Core 雙目標抽取與四個資料操作",
      "status": "active",
      "note": "四個舊方法來源已定位且已實作；Fee、EntityQuery、Appointment、Lessons、MeetingStatistics五組已搬Core。net48實載net462211/211、net10 289/289，Core覆蓋98.78%／100%；全方案1836/1836，雙模型及補證完成，無Critical/High。其餘候選純化／抽取尚未全部完成，不標全包完成。"
    },
    {
      "id": "P1-05",
      "name": "SpeechMessage.Localization 完整版",
      "status": "todo",
      "note": "尚未開始。需完成五層字典解析、具名參數、語言 resolver、關鍵字正規化與衝突守門，並驗證教會 A／B 1,000 次並行不串資料。"
    },
    {
      "id": "P1-06",
      "name": "教會時鐘與 IANA／Windows 時區對照",
      "status": "todo",
      "note": "尚未開始。依賴 P1-05；需驗證明確 UTC／當地時間、DST 模糊與無效時間拒絕、生日純日期不移日及雙目標一致。"
    },
    {
      "id": "P1-07",
      "name": "Money 與幣別格式化",
      "status": "todo",
      "note": "尚未開始。依賴 P1-05；需驗證幣別白名單、TWD 顯示政策、格式語系不改幣別及跨幣別運算拒絕。"
    },
    {
      "id": "P1-08",
      "name": "教會覆寫快照快取",
      "status": "todo",
      "note": "尚未開始。依賴 P1-04、P1-05、P1-10；須先完成租戶 resolver，再實作有界快取、single-flight、完整候選驗證、原快照保留、取消隔離與 Dispose／drain 基線測試。"
    },
    {
      "id": "P1-09",
      "name": "I18nCheck v2 完整資源檢查",
      "status": "todo",
      "note": "尚未開始。需逐規則補正反例，檢查 key／參數／重複、Sender 與 ChatBar 長度、偽語系、跨語系關鍵字衝突及英文完成度報告。"
    },
    {
      "id": "P1-10",
      "name": "SpeechMessage.Tenancy 與 LINE channel resolver",
      "status": "todo",
      "note": "尚未開始。依賴 P1-04；集中既有九處設定解析、保留 Jesus 後備並安全告警，加入租戶 A／B 並行隔離，且先於 P1-08。"
    },
    {
      "id": "P1-11",
      "name": "Deployment 結構 schema／plan／apply／verify",
      "status": "todo",
      "note": "尚未開始。依賴 P1-04；先完成唯讀 metadata 匯出與離線 plan／verify，apply 僅新增、核對 fingerprint／plan hash／白名單與冪等性，不改既有結構或輸出業務資料／秘密。"
    },
    {
      "id": "P1-12",
      "name": "goodshepherdback 結構驗收",
      "status": "you",
      "note": "歷史計畫 D24 指定使用者 apply；最新對話另有條件授權必要可逆測試變更，並非任意寫入授權。須確認隔離測試環境、適當備份與可驗證復原後才可執行；目前未確認，維持只讀。不可逆操作與正式資料仍不在授權範圍；不得把 mock／離線驗證寫成實機通過。"
    }
  ],
  "phases": [
    {
      "id": "階段 0",
      "name": "準備、守門與可行性驗證",
      "est": "5～8 天",
      "total": 18,
      "done": 18,
      "active": 0,
      "current": false
    },
    {
      "id": "階段 1",
      "name": "基礎層與國際化地基",
      "est": "12～16 天",
      "total": 12,
      "done": 2,
      "active": 2,
      "current": true
    },
    {
      "id": "階段 2",
      "name": "共用送訊＋對外待辦",
      "est": "10～13 天",
      "total": 12,
      "done": 0,
      "active": 0
    },
    {
      "id": "階段 3",
      "name": "LINE Webhook 主機",
      "est": "12～17 天",
      "total": 9,
      "done": 0,
      "active": 0
    },
    {
      "id": "階段 4",
      "name": "金流＋幣別",
      "est": "6～8 天",
      "total": 6,
      "done": 0,
      "active": 0
    },
    {
      "id": "階段 5",
      "name": "Plugin／Workflow 重建（36 個 DLL → 2 個）",
      "est": "20～27 天",
      "total": 13,
      "done": 0,
      "active": 0
    },
    {
      "id": "階段 6",
      "name": "ChurchReport 改用共用層",
      "est": "6～8 天",
      "total": 6,
      "done": 0,
      "active": 0
    },
    {
      "id": "階段 7",
      "name": "D365 9.1 切換（逐組織）",
      "est": "每個組織 1～2 天＋觀察 1 週",
      "total": 2,
      "done": 0,
      "active": 0
    },
    {
      "id": "階段 8",
      "name": "收尾、偽語系驗收、教會設定網頁",
      "est": "4～6 天",
      "total": 5,
      "done": 0,
      "active": 0
    }
  ]
}
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.