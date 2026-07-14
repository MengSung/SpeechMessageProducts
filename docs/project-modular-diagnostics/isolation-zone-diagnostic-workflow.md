# 隔離區分析、診斷與 CCG 零信任審核工作流

## 1. 目的與狀態

本文件定義 SpeechMessageProducts 的標準模組診斷流程。適用對象是
`module-boundaries-and-optimization-map.md` 中全部 35 個葉節點。

工作流的目的：

1. 為每個葉節點建立獨立且可稽核的文件工作區。
2. 只分析該葉節點擁有的程式碼，不混入其他 Primary Owner 的問題。
3. 找出立即性的安全問題、明顯的效能浪費與可加速後續工作的乾淨提取邊界。
4. 每個工作區只派出一個診斷 subagent，由它完整執行安全、效能與提取分析。
5. 不派出任何巢狀 subagent；診斷結果的獨立審核只由 CCG Gemini + Claude
   零信任裁決負責。
6. 只有證據充分，並取得完整雙模型通過或符合本文件降級批准規則的 ISSUE，
   才能保留在最終 `issue.md`。

本文件目前狀態為 `APPROVED_FOR_DIAGNOSTIC_EXECUTION`。

使用者已批准依本流程逐步完成全部 35 個工作區。此批准只涵蓋唯讀分析、
診斷文件與 CCG 審核，不包含任何產品程式碼優化。

## 2. 核心術語

### 2.1 葉節點

葉節點是模組地圖中可以成為 Primary Owner 的最小單位，例如 F01A、B01、
X02A。上層管理領域 F01、F03、F05、B04、B06、X02、X04、X05 不能直接
成為診斷工作區。

### 2.2 隔離區

本工作流中的「隔離區」泛指目前正在分析的葉節點工作範圍。

其中 F03Q、X02Q、X05Q 是正式的 quarantine 葉節點，只能進行：

- responsibility proof；
- 拆分候選分析；
- 移交建議；
- 淘汰建議。

它們不得產生整包 optimization plan。

### 2.3 Gate-blocked 葉節點

模組地圖第 10.1 節標記為 gate-blocked 的葉節點可以分析與診斷，但在建立
可執行 baseline、consumer gate 與 rollback point 以前，不得宣告可以進入
optimization。

### 2.4 ISSUE

ISSUE 是具有穩定 ID、具體程式碼證據、影響、處理價值與審核紀錄的診斷項目。
未經證明的想法只能稱為 candidate 或 hypothesis，不能直接稱為 confirmed ISSUE。

## 3. 工作區建立規則

### 3.1 根目錄

所有模組工作區都建立在：

```text
docs/project-modular-diagnostics/
```

### 3.2 固定資料夾名稱

資料夾名稱必須使用第 12 節登錄的固定名稱：

```text
<LeafID>-<stable-kebab-case-name>/
```

禁止：

- 自由翻譯模組名稱。
- 省略 Leaf ID。
- 使用 F01、B04、X02 等非葉節點 ID。
- 同一葉節點建立兩個不同名稱的工作區。

### 3.3 最低目錄結構

```text
<module-workspace>/
|-- issue.md
|-- review-log.md
`-- evidence/
    |-- scope-manifest.md
    |-- security-analysis.md
    |-- performance-analysis.md
    |-- extraction-analysis.md
    `-- runtime-validation-plan.md
```

規則：

- `issue.md` 是唯一的正式 ISSUE 清單。
- `review-log.md` 記錄唯一診斷 subagent、CCG run、版本、裁決與狀態轉移。
- `scope-manifest.md` 記錄 Primary Owner 檔案、唯讀 dependencies 與 consumers。
- 三份專項分析由同一個工作區診斷 subagent 依序完成。
- 不建立 `skeptic-review.md`；獨立反證、原始檔重開與 ISSUE 裁決由 CCG
  reviewer 執行。
- 沒有 runtime hypothesis 時，可以不建立 `runtime-validation-plan.md`。
- CCG 原始 prompt/stdout/stderr/summary 保留在 `.ccg/dual-model-runs/`，
  `review-log.md` 只記錄 run ID 與路徑。

## 4. 單層 Agent 與 CCG 職責

唯一合法的執行拓撲是：

```text
Lead Codex
`-- 一個 Workspace Diagnostic Subagent
    `-- 直接執行 Start-CcgDualModelRun.ps1
        |-- Gemini backend
        `-- Claude backend
```

這是單層代理加外部模型審核，不是巢狀代理拓撲。Workspace Diagnostic
Subagent 不得呼叫 `spawn_agent`、multi-agent 工具或其他代理派工機制。
Gemini 與 Claude 必須由 CCG self-healing runner 直接呼叫；不得先派出
review subagent，再由 review subagent 呼叫 CCG。

### 4.1 Lead Codex

Lead Codex 負責：

1. 接收使用者對葉節點的批准。
2. 建立模組工作區。
3. 記錄執行前 Git 狀態快照。
4. 對目前工作區派出一個且僅一個 Diagnostic Subagent。
5. 等待該 subagent 完成診斷、CCG 審核與修訂，不平行重做相同診斷。
6. 驗證該 subagent 沒有派出任何巢狀 subagent。
7. 驗證沒有產品程式碼或白名單外檔案被修改。
8. 簡單核對 CCG `summary.json`、最終 ISSUE 狀態與流程紀錄是否一致。
9. 完整雙模型通過時接受 `APPROVED`；符合使用者核准的降級規則時接受
   `APPROVED_DEGRADED`。

Lead Codex 不替 Diagnostic Subagent 補寫診斷內容。證據不足或流程不完整時，
退回同一個 subagent 修訂，或將結果標記為未完成。

### 4.2 Workspace Diagnostic Subagent

每個模組工作區只允許一個 Workspace Diagnostic Subagent。

這個 subagent 不需要且不得取得 multi-agent/spawn 能力，並明確禁止：

- 派出任何 subagent、sub-subagent、review agent 或 investigator agent。
- 將安全、效能或提取分析轉交其他 agent。
- 以額外 agent 取代 CCG reviewer。
- 先派出 review agent，再由該 agent 呼叫 CCG runner。
- 修改產品程式碼、治理檔案或其他模組工作區。

Workspace Diagnostic Subagent 負責：

1. 讀取模組地圖與該葉節點 Primary Owner 規則。
2. 產生 `scope-manifest.md`。
3. 自行完成安全、效能與提取三類診斷。
4. 將三類證據分別寫入 `security-analysis.md`、
   `performance-analysis.md` 與 `extraction-analysis.md`。
5. 刪除沒有原始檔證據的 candidate。
6. 依第 8 節排序公式產生 `issue.md` 初稿。
7. 直接執行專案 CCG self-healing runner 進行獨立審核，不派出審核 agent。
8. 根據 CCG 裁決執行 REWRITE、DELETE 或移入 runtime validation。
9. 完成 `review-log.md`、Git 差異核對與最終狀態。
10. 不得修改產品程式碼。

### 4.3 Security 診斷責任

Diagnostic Subagent 在 `evidence/security-analysis.md` 專查：

- session、cookie、claims、authentication、authorization 與 identity isolation；
- 跨使用者、跨 request、跨 tenant 或 static/shared state 資料洩漏；
- secret、token、credential、PII、付款資料與 log 洩漏；
- callback、redirect、CSRF、SSRF、path、input validation 與 injection；
- unsafe crypto、簽章驗證、重放、idempotency 與 race condition；
- lifetime/disposal 導致的安全狀態殘留。

Diagnostic Subagent 不得因為看到 `static`、`Session`、`HttpClient` 或
`Task.Run` 就直接宣告 Critical。必須追蹤實際資料來源、生命週期、使用者邊界
與可達路徑。

### 4.4 Performance 診斷責任

Diagnostic Subagent 在 `evidence/performance-analysis.md` 專查：

- event、timer、hosted service、cache、stream、client 或 unmanaged resource 洩漏；
- 未釋放 IDisposable、錯誤 lifetime、無界集合與 cache；
- N+1 CRM/HTTP/database call；
- 巢狀或重複迴圈、重複 materialization、重複 serialization；
- 大量 allocation、string concatenation、blocking I/O、sync-over-async；
- request path 上不必要的 reflection、logging、profiling 或重建 client；
- 缺少 batching、column selection、pagination、cache 或 cancellation；
- 只有 runtime measurement 才能證明的效能 hypothesis。

效能 ISSUE 必須說明成本來源。不能只用「可能比較慢」作為證據。

### 4.5 Extraction 診斷責任

Diagnostic Subagent 在 `evidence/extraction-analysis.md` 專查可以形成乾淨
模組的責任：

- 有單一 cohesive responsibility；
- 有清楚 input/output contract；
- 可以列出完整 owning files；
- 沒有隱藏 static/global state；
- 不會建立 circular dependency；
- 有測試 seam 或可建立測試 seam；
- 有至少一個明確 consumer；
- 提取後能縮小後續分析範圍、重複使用或加速優化 loop。

只有「檔案很大」不是提取理由。提取候選必須說明新的邊界、契約、依賴方向、
測試方式與移交到哪個葉節點或新葉節點。

### 4.6 CCG Reviewer

CCG Gemini 與 Claude 是唯一的獨立 reviewer。它們必須重新開啟原始檔並檢查：

- 原始碼是否真的存在被描述的行為；
- 引用的行號是否正確；
- 是否忽略既有 guard、dispose、cache limit、authorization 或 retry；
- 是否把 dependency 的問題誤算成目前葉節點的問題；
- 是否把 hypothesis 寫成 confirmed；
- 是否高估影響或低估修改成本；
- 提取候選是否只有移動檔案，沒有真正建立邊界。

CCG reviewer 是 runner 直接呼叫的外部模型 backend，不是 Codex subagent，
不計入工作區的單一 subagent 限制，也不得被包裝成巢狀 review agent。

### 4.7 Empty-attempt recovery exception

「一個且僅一個 Diagnostic Subagent」仍是正常執行的硬性規則。已發生的空白
或啟動失敗派工不能被改寫成單一派工；只有在下列條件全部具備時，才能以
`RECOVERY_EXCEPTION_ACCEPTED` 收斂歷史狀態：

1. superseded attempt 沒有建立任何正式七檔 diagnostic package，也沒有可用
   CCG finding；
2. 同一工作區的 attempt 時間不重疊；
3. 每個 attempt 的 nested child count 都是 `0`；
4. 最終七檔 package 與後續 CCG 修訂只有一個明確 accepted author；
5. ledger 與 `review-log.md` 永久保留 accepted/superseded attempt ID、
   `NO_DIAGNOSTIC_DELIVERABLE`、`NO_OVERLAP` 與 nested count；
6. 只有模型不可用而從未開始診斷的派工標記為
   `DISPATCH_FAILED_MODEL_UNAVAILABLE`，不得算成 diagnostic author；
7. 這個例外只收斂 agent topology，不改變 CCG/runtime/write-scope 狀態。

任一條件缺少可重開證據時，狀態必須是 `INVALID_AGENT_TOPOLOGY`。不得刪除
失敗 session、隱藏 replacement 歷史，或用新派工假裝過去未曾違規。

## 5. 唯讀與範圍控制

### 5.1 允許讀取

- 目前葉節點 Primary Owner 的所有檔案。
- 為了理解資料流而需要的 dependencies 與 consumers。
- 對應測試、設定、文件、project reference 與 Git 歷史。

### 5.2 允許寫入

只允許寫入：

```text
docs/project-modular-diagnostics/<current-module-workspace>/**
.ccg/dual-model-runs/**
.ccg/tasks/project-modular-analysis-diagnosis-optimization/**
.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/**
```

不得修改：

- 產品 `.cs`、`.csproj`、`.cshtml`、JavaScript、CSS 或設定檔。
- 其他模組工作區。
- 使用者既有的未提交變更。

### 5.3 Git 差異防護

由於 worktree 可能原本就不是 clean，Lead Codex 必須：

1. 在派出 Diagnostic Subagent 前保存 `git status --porcelain` baseline。
2. 在唯一 Diagnostic Subagent 結束後重新取得 status。
3. 比較新增差異，而不是假設所有既有差異都是本次產生。
4. 若出現白名單以外的新差異，該 agent run 立即標為 `INVALID_WRITE_SCOPE`。
5. 不自動 revert 可能屬於使用者的變更；先隔離結果並回報。

## 6. 標準執行流程

### Phase 0：使用者批准

輸入：

- Leaf ID。
- 固定工作區名稱。
- 是否允許執行 runtime measurement。

沒有明確批准不得開始。

### Phase 1：建立工作區

1. 依第 12 節建立固定資料夾。
2. 建立空白 `issue.md`、`review-log.md` 與 `evidence/`。
3. 在 `review-log.md` 記錄：
   - branch/worktree；
   - module map 版本；
   - Leaf ID；
   - 開始時間；
   - Git baseline；
   - 執行模式 `DIAGNOSIS_ONLY`。

### Phase 2：確認範圍

Diagnostic Subagent 先產生 `scope-manifest.md`：

- Primary Owner files。
- 單檔優先例外。
- dependencies。
- consumers。
- tests。
- gate 狀態。
- quarantine 狀態。
- 明確排除的路徑。

Diagnostic Subagent 必須先自查 manifest 與模組地圖一致，再進入診斷。Lead
Codex 在完成後做流程核對；範圍不一致時不得接受最終結果。

### Phase 3：單一 subagent 完整診斷

Diagnostic Subagent 不得派出任何巢狀 agent，並自行完成：

1. Security 診斷，寫入 `evidence/security-analysis.md`。
2. Performance 診斷，寫入 `evidence/performance-analysis.md`。
3. Extraction 診斷，寫入 `evidence/extraction-analysis.md`。
4. 重新開啟所有引用檔案，先刪除無法證明或 ownership 錯誤的 candidate。

三類診斷可以依檔案批次或類別依序執行，但必須由同一個 Diagnostic Subagent
完成。不得用額外 agent 做平行調查、反證或預審。

`review-log.md` 必須記錄：

- 唯一 Diagnostic Subagent 的 agent ID/type；
- prompt 摘要；
- 開始與完成時間；
- 輸出檔案；
- nested agent count 必須是 `0`；
- 是否發生 write-scope violation。

如果 Diagnostic Subagent 派出任何巢狀 agent，該次執行標為
`INVALID_AGENT_TOPOLOGY`，不得繼續使用該次診斷結果。

### Phase 4：建立候選 ISSUE

Diagnostic Subagent 只能從三份專項 evidence 文件建立候選。

候選必須先通過：

- 路徑屬於目前 Primary Owner，或明確標為 cross-module dependency。
- 精確檔案與行號可重新開啟。
- 行為描述與原始碼一致。
- impact 不是純推測。
- confirmed/hypothesis 狀態明確。
- 已由 Diagnostic Subagent 自查既有 guard、反證與 ownership。

未通過者記入 `review-log.md` 的 rejected candidate，不進入 `issue.md`。

### Phase 5：產生並排序 `issue.md`

Diagnostic Subagent 依第 7、8 節格式產生初稿。

主清單只包含 confirmed ISSUE。Hypothesis 放在獨立的
`Runtime Validation Pending` 區段，不得混入正式優先序。

### Phase 6：CCG 零信任審核

CCG 審核由同一個 Diagnostic Subagent 直接執行 self-healing runner。
此階段禁止 `spawn_agent`、sub-subagent 或 review agent；Gemini 與 Claude
backend 的執行不構成代理派工。

每一輪必須：

1. 計算 `issue.md` SHA-256，記錄 review version。
2. 建立 UTF-8 CCG reviewer prompt。
3. 使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role reviewer `
  -Title "<LeafID>-issue-review-r<round>" `
  -PromptFile "<prompt-file>" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

4. CCG reviewer 對每個 ISSUE 回傳：
   - `KEEP`
   - `REWRITE`
   - `DELETE`
   - `NEEDS_RUNTIME_VALIDATION`
5. Reviewer 必須自行重新開啟 issue 引用的原始檔與行號，不得只相信
   `issue.md` 內貼出的摘要或片段。
6. Reviewer 僅允許使用讀取型命令核對證據。Prompt 必須明確禁止：
   - `dotnet restore`、`dotnet build`、`dotnet test`；
   - package restore、code generation、formatting 或 migration；
   - 任何會建立或更新 `bin/**`、`obj/**`、cache、lockfile 或測試輸出的命令。
7. 即使寫入的是 Git ignored 檔案，也視為白名單外寫入並標記
   `INVALID_WRITE_SCOPE`。
8. Diagnostic Subagent 解析 `summary.json`，不得只檢查 process exit code。
9. 將 Gemini 與 Claude 的逐 ISSUE verdict 寫入 `review-log.md`。

### Phase 7：依裁決修訂

| 裁決 | 必要動作 |
|---|---|
| 兩者 `KEEP` | 保留，前提是 Diagnostic Subagent 依 reviewer 意見重新核對路徑與行號 |
| 任一 `REWRITE` | 修改證據、影響、嚴重度、範圍或方案，進入下一輪 |
| 任一 `DELETE` | 從 confirmed 清單移除；有新反證時只能以新版本重新送審 |
| 任一 `NEEDS_RUNTIME_VALIDATION` | 移到 pending 區，建立 runtime validation plan，模組不得 `APPROVED` 或 `APPROVED_DEGRADED` |
| reviewer 無法重開檔案核對 | 該 ISSUE 不得 KEEP |
| reviewer 發現跨模組 ownership | 拆出關聯工作項目，不得在本模組直接修正 |

### Phase 8：收斂或升級

- 同一 ISSUE 最多進行 3 輪 REWRITE。
- 第 3 輪後 Gemini/Claude 仍持續分歧，狀態改為
  `HUMAN_DECISION_REQUIRED`，工作流保持未完成並交由使用者裁決。
- 這不是放棄 ISSUE，也不是批准 ISSUE。
- 使用者提供新決策或 runtime evidence 後，從下一輪繼續。

### Phase 9：完成

完整通過後：

1. `issue.md` 狀態改為 `APPROVED`、`APPROVED_DEGRADED` 或
   `NO_ACTION_REQUIRED`。
2. `review-log.md` 記錄通過的 CCG run ID、兩個 backend 與 issue hash。
3. 再次驗證 Git 新增差異只在白名單。
4. 向使用者列出保留、刪除、待 runtime 驗證與跨模組轉交的數量。
5. 不自動開始 optimization；等待使用者批准 ISSUE 執行順序。

## 7. `issue.md` 強制格式

```markdown
# <LeafID> <Module Name> Diagnostic Issues

Status: DRAFT | DEGRADED_REVIEW_PENDING | RUNTIME_VALIDATION_PENDING |
        HUMAN_DECISION_REQUIRED | APPROVED | APPROVED_DEGRADED |
        NO_ACTION_REQUIRED
Module: <LeafID>
Workspace: <fixed-folder-name>
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: READY | BLOCKED | QUARANTINE
Issue document SHA-256: <hash>

## Executive Summary

## Ranked Confirmed Issues

### <LeafID>-SEC-001 <Title>

- Category: Security | Performance | Extraction
- Severity: Critical | High | Medium | Low
- Priority: P0 | P1 | P2 | P3
- Priority score: 0-100
- Confirmed: true
- Evidence confidence: 0-20
- Impact score: 0-25
- Likelihood/frequency score: 0-15
- Security urgency score: 0-15
- Performance gain score: 0-10
- Loop leverage score: 0-10
- Ease/reversibility score: 0-5
- Effort: XS | S | M | L | XL
- Primary owner: <LeafID>
- Cross-module: false | <related LeafID/task>
- Gate blocked: true | false
- Files:
  - path/to/file.cs:line
- Evidence:
- Control/data/lifetime flow:
- Impact:
- Why this is necessary:
- Recommended action:
- Validation:
- Rollback boundary:
- Extraction contract: N/A | input/output/dependency/test seam/consumer
- CCG round history:
  - Round 1: Gemini <verdict>; Claude <verdict>; source rechecked <true/false>

## Runtime Validation Pending

## Deleted Or Rejected Candidates

## Cross-Module Handoffs

## Final CCG Approval
```

規則：

- 七個 metadata 欄位必須各自位於文件頂部且使用範本中的精確欄位名稱；
  `Status:` 不得改寫成 `Final status:` 或放入清單，`Gate status:` 的值只能是
  `READY`、`BLOCKED` 或 `QUARANTINE`，原因另寫在正文。
- 頂部 `Gate status:` 是模組層級的 optimization admission gate，必須與
  module map 及 `evidence/scope-manifest.md` 一致。每筆 issue 的
  `Gate blocked:` 是該 action 自身是否另有依賴阻塞；它不能覆蓋或放寬
  模組層級 gate。即使某筆 issue 為 `Gate blocked: false`，只要模組頂部
  gate 不是 `READY`，該 issue 仍不得進入 optimization implementation。
- ISSUE ID 在 REWRITE 過程中保持不變。
- 類別代碼使用 `SEC`、`PERF`、`EXT`。
- confirmed ISSUE 必須有至少一個精確 `path:line`。
- 只靠 runtime 才能確認的項目必須填 `Confirmed: false`，並移到 pending。
- quarantine 或 gate-blocked 葉節點不得宣稱已有可執行 optimization plan。
- 若所有 candidate 都被刪除，允許產生 `NO_ACTION_REQUIRED`，但仍需雙模型批准。

### 7.1 Canonical issue hash

`Issue document SHA-256` 不得直接計算包含自身 hash 值的原始檔案，否則會形成
自我引用。統一使用下列 canonical 規則：

1. 以 UTF-8 解碼整份 `issue.md`。
2. 將換行統一為 LF。
3. 將第一個 `Issue document SHA-256:` 欄位整行正規化為
   `Issue document SHA-256:`，冒號後不保留空白或值。
4. 對正規化後的 UTF-8（無 BOM）位元組計算 SHA-256。
5. 將 64 位小寫十六進位結果寫回欄位。

任何其他內容或 metadata 改變後都必須重算。CCG `review-log.md` 必須分別記錄
送審版本 hash 與套用 reviewer 修改後的 final hash；不得用 `pending`、空值、
run ID 或描述文字代替 hash。

## 8. ISSUE 價值排序

### 8.1 評分

每個 confirmed ISSUE 的 Priority Score 滿分 100：

| 因素 | 分數 | 判定 |
|---|---:|---|
| Impact | 0-25 | 資料、安全、可用性、資源或維護影響 |
| Evidence confidence | 0-20 | 靜態證據、測試、重現與 reviewer 核對強度 |
| Likelihood/frequency | 0-15 | 是否在常態路徑發生、觸發頻率與 blast radius |
| Security urgency | 0-15 | exploitability、identity/session/secret/付款風險 |
| Performance gain | 0-10 | 可合理預期的 latency、memory、CPU、I/O 改善 |
| Optimization-loop leverage | 0-10 | 是否縮小後續範圍、建立 reusable seam 或解鎖多個模組 |
| Ease/reversibility | 0-5 | 修改是否小、容易測試與回滾 |

### 8.2 優先級

- P0：85-100。
- P1：70-84。
- P2：50-69。
- P3：0-49。
- 已確認且可立即利用的 Critical 安全問題自動是 P0。
- Hypothesis 不參與 confirmed 排序。

### 8.3 排序規則

1. confirmed 永遠優先於 hypothesis。
2. Priority Score 由高到低。
3. 同分時，較高 Severity 優先。
4. 再同分時，較低 Effort、較高 Reversibility 優先。
5. 能解鎖其他模組或多輪優化的 Extraction ISSUE 優先。

## 9. CCG 通過與失敗語意

### 9.1 `APPROVED`

必須同時滿足：

- `summary.json.ok=true`。
- Gemini 與 Claude 都完成且有可用輸出。
- `degradedFallback=false`。
- `quotaBlocked=false`。
- 所有 confirmed ISSUE 都取得兩個 reviewer 的 `KEEP`。
- 沒有 `REWRITE`、`DELETE`、`NEEDS_RUNTIME_VALIDATION`、
  Critical 或 Warning 未處理。
- Diagnostic Subagent 已依 CCG 裁決重新核對所有保留 ISSUE 的路徑與行號。
- `review-log.md` 證明 nested agent count 是 `0`。
- Git 新增差異沒有超出白名單。

### 9.2 `APPROVED_DEGRADED`

使用者允許降級結果作為正式批准。必須同時滿足：

- `summary.json.degradedFallback=true`。
- `summary.json.fallbackAccepted=true`。
- 至少一個 backend 完成且有可用輸出。
- 未完成 backend 的原因是 provider quota、session 或 billing，而不是未修復的
  本機 toolchain failure。
- 所有完成 backend 都對每個保留 ISSUE 給出 `KEEP`。
- 完成 backend 沒有未解決的 Critical 或 Warning。
- Diagnostic Subagent 已依完成 backend 的意見重新開啟所有引用檔案與行號，
  確認證據、owner、severity 與必要性。
- 所有 `REWRITE`、`DELETE`、`NEEDS_RUNTIME_VALIDATION` 已處理。
- `review-log.md` 證明 nested agent count 是 `0`。
- Git 新增差異沒有超出白名單。

`APPROVED_DEGRADED` 是可接受的正式工作流結果，但必須永久保留：

- 完成與失敗的 backend 名稱。
- quota/session/billing 原因。
- `summary.json` 路徑。
- Diagnostic Subagent 的逐 ISSUE 重新核對紀錄。
- Lead Codex 的流程與寫入範圍核對結果。

不得把 `APPROVED_DEGRADED` 改寫成完整雙模型 `APPROVED`。

### 9.3 `DEGRADED_REVIEW_PENDING`

出現以下任一情況時仍不得批准：

- 沒有任何 backend 產生可用輸出。
- `fallbackAccepted=false`。
- 完成 backend 仍有未解決的 Critical/Warning。
- ISSUE 仍有 `REWRITE`、`DELETE` 或 `NEEDS_RUNTIME_VALIDATION`。
- 失敗原因是未修復的本機 toolchain error。
- Diagnostic Subagent 無法依 reviewer 意見重新核對原始碼證據。
- 執行期間曾派出巢狀 agent。

### 9.4 Provider quota/billing

若 `summary.json.quotaBlocked=true`，且錯誤是 quota、session、billing、
insufficient balance 或「餘額不足」：

1. 不進行本機 PATH/toolchain 修復。
2. 不重複消耗相同 provider。
3. 若至少一個 backend 有可用輸出，依 9.2 節完成零信任核對後，可以標記
   `APPROVED_DEGRADED`。
4. 若沒有 backend 有可用輸出，狀態設為 `DEGRADED_REVIEW_PENDING`。
5. 記錄已完成 backend 的建議與未完成原因。
6. Provider 恢復後可以使用相同 issue hash 補做完整 review，但不阻擋已符合
   9.2 節的降級批准。

### 9.5 本機工具失敗

只有 exit code 2 或 summary 指向本機 toolchain failure 時，才依專案
self-healing 規則修復並重跑相同入口。

### 9.6 Runtime validation

`NEEDS_RUNTIME_VALIDATION` 會阻擋 `APPROVED` 與 `APPROVED_DEGRADED`。

`runtime-validation-plan.md` 必須包含：

- 要驗證的 ISSUE ID；
- measurement/reproduction 方法；
- 所需資料與環境；
- 安全限制；
- 成功/失敗門檻；
- 執行者；
- 結果如何改變 KEEP/DELETE verdict。

## 10. ISSUE 類別的最低證據

### 10.1 Security

至少具備：

- source/sink；
- authentication/identity/session boundary；
- 可達 control flow；
- guard 是否存在；
- 受影響資料；
- exploit 或 leakage 條件；
- 為何是目前葉節點責任。

沒有上述證據不得標記 Critical。

### 10.2 Performance

至少具備：

- hot path 或觸發頻率；
- allocation/I/O/CPU/lifetime 成本來源；
- loop 或 call count；
- disposal/cache/batching/cancellation 現況；
- 靜態可證明或需 runtime 驗證；
- 建議改善如何量測。

### 10.3 Extraction

至少具備：

- owning files；
- cohesive responsibility；
- input/output contract；
- dependency direction；
- consumers；
- test seam；
- rollback boundary；
- loop leverage。

## 11. 每輪審核的 Prompt 要求

CCG reviewer prompt 必須要求兩個 backend：

1. 逐 ISSUE 回傳 verdict，不得只給整體摘要。
2. 自行開啟原始檔並核對行號。
3. 找出 exaggerated severity、錯誤 owner、忽略 guard、錯誤 lifetime、
   無法證明的效能改善與沒有 contract 的 extraction。
4. 對不正確或不必要的 ISSUE 明確要求 DELETE。
5. 對方向可能正確但證據不足的 ISSUE 要求 REWRITE 或
   NEEDS_RUNTIME_VALIDATION。
6. 不因另一 reviewer 的結論而改變自己的獨立判斷。
7. 最後列出 unresolved Critical/Warning 與 module verdict。

## 12. 35 個固定工作區

下列名稱是唯一合法的模組工作區名稱。本回合只登錄，不建立資料夾。

### Shared Foundation

| 順序 | Leaf ID | 模組名稱 | 固定工作區資料夾 |
|---:|---|---|---|
| 1 | F01A | Solution、Build 與 CI 治理 | `F01A-solution-build-ci-governance/` |
| 2 | F01B | AI Agent 與開發工作流治理 | `F01B-ai-agent-workflow-governance/` |
| 3 | F01C | 文件、工具與歷史資料 | `F01C-documentation-tooling-history/` |
| 4 | F01D | 共用測試容器治理 | `F01D-shared-test-harness-governance/` |
| 5 | F02 | Dataverse 連線基礎 | `F02-dataverse-connection-foundation/` |
| 6 | F03A | CRM 操作函式庫 | `F03A-crm-operations-library/` |
| 7 | F03B | ToolUtility LINE Adapter | `F03B-toolutility-line-adapter/` |
| 8 | F03Q | ToolUtility 混合 Facade 隔離 | `F03Q-toolutility-mixed-facade-quarantine/` |
| 9 | F04 | LINE Messaging SDK | `F04-line-messaging-sdk/` |
| 10 | F05A | LINE Processor Core | `F05A-line-processor-core/` |
| 11 | F05B | LINE ASP.NET Core Composition Adapter | `F05B-line-aspnetcore-composition-adapter/` |
| 12 | F06 | LINE 通知與回覆工作流 | `F06-line-notification-reply-workflows/` |
| 13 | F07 | LINE RichMenu 引擎 | `F07-line-richmenu-engine/` |
| 14 | F08 | 付款供應商核心 | `F08-payment-provider-core/` |
| 15 | F09 | 可重用付款工作流與宿主 Adapter | `F09-payment-workflows-host-adapter/` |

### ChurchReport Business

| 順序 | Leaf ID | 模組名稱 | 固定工作區資料夾 |
|---:|---|---|---|
| 16 | B01 | 身分、登入、Session 與存取控制 | `B01-identity-session-access-control/` |
| 17 | B02 | 會員、聯絡人、個人資料與新朋友 | `B02-member-contact-profile-onboarding/` |
| 18 | B03 | 小組、層級與週報 | `B03-small-group-hierarchy-reporting/` |
| 19 | B04A | 出席與 Present Record | `B04A-attendance-present-record/` |
| 20 | B04B | 預約與設備 | `B04B-appointment-equipment/` |
| 21 | B04C | 排程與 QR | `B04C-scheduling-qr/` |
| 22 | B05 | 奉獻與產品付款流程 | `B05-donation-product-payment/` |
| 23 | B06A | 清單與參照資料 | `B06A-list-reference-data/` |
| 24 | B06B | 費用管理 | `B06B-fee-management/` |
| 25 | B06C | 教會層級與 Register | `B06C-church-hierarchy-register/` |
| 26 | B07 | ChurchReport 專用 LINE 整合 | `B07-churchreport-line-integration/` |

### Cross-Cutting Platform

| 順序 | Leaf ID | 模組名稱 | 固定工作區資料夾 |
|---:|---|---|---|
| 27 | X01 | 主站組裝、Middleware、Routes 與 Lifetime | `X01-host-composition-routes-lifetimes/` |
| 28 | X02A | 共用 Cache 基礎 | `X02A-shared-cache-foundation/` |
| 29 | X02B | Observability、Health 與 Logging | `X02B-observability-health-logging/` |
| 30 | X02C | Performance Profiling | `X02C-performance-profiling/` |
| 31 | X02Q | Legacy Trace 隔離 | `X02Q-legacy-trace-quarantine/` |
| 32 | X03 | 共用 Web UI 與靜態資產平台 | `X03-shared-web-ui-assets/` |
| 33 | X04A | Runtime Configuration 與 Secrets | `X04A-runtime-configuration-secrets/` |
| 34 | X04B | Deployment 與 Package Sources | `X04B-deployment-package-sources/` |
| 35 | X05Q | ChurchReport Legacy Boundary 隔離 | `X05Q-churchreport-legacy-boundary-quarantine/` |

## 13. 第一個示範模組

使用者批准後，第一個示範模組固定為模組地圖第 5.1 節第一列：

```text
Leaf ID: F01A
Module: Solution、Build 與 CI 治理
Workspace: docs/project-modular-diagnostics/F01A-solution-build-ci-governance/
Mode: DIAGNOSIS_ONLY
```

第一個示範執行必須：

1. 由 Lead Codex 建立 F01A 工作區。
2. 派出一個且僅一個 Workspace Diagnostic Subagent。
3. 明確禁止該 subagent 派出任何巢狀 agent。
4. 由同一個 subagent 完成 Security、Performance、Extraction 診斷與
   `issue.md` 初稿。
5. 由同一個 subagent 直接呼叫 CCG self-healing runner，進入逐 ISSUE
   零信任審核迴圈；不得先派出 review agent。
6. 完整通過或進入明確的 pending/blocked 狀態。
7. Lead Codex 等待完成後，只核對 agent topology、CCG 摘要、最終狀態與
   Git 寫入範圍。
8. 不修改 solution、project、CI 或其他產品/治理檔案。

## 14. 本流程的 CCG 信任狀態

本工作流在撰寫前已執行 CCG analyzer：

```text
Run ID: 20260710-172007-isolation-zone-diagnostic-workflow-analyzer
Claude: completed
Gemini: quota/billing 403 餘額不足
Result: degradedFallback=true
```

已保留該次分析中與目前單層流程相容的修正：

- agent write scope 白名單與 Git delta 驗證；
- CCG reviewer 必須重新開啟原始碼核對行號；
- 必須解析 `summary.json`，不能只看 exit code；
- quota/billing failure 停止本機修復重試；
- 3 輪 REWRITE 後升級人工決策；
- `issue.md` 欄位與 runtime validation 規則。

原分析提出的巢狀 Level-1/Level-2 agent 拓撲，已依使用者後續決策廢止。
自本版起，每個工作區只有一個 Diagnostic Subagent，獨立審核只由 CCG
backend 執行。

因 Gemini 未完成，本工作流本身沒有取得完整雙模型設計審查；依使用者批准的
政策，本次 Claude 可用輸出加上 Lead Codex 本機核對可接受為降級設計審查。
必須保留 `degradedFallback=true` 紀錄，不得宣稱為完整雙模型 approval。

上述 CCG run 審查的是先前版本。這次單層 agent 拓撲是使用者直接指定的流程
修訂，本回合依要求只更新本文件，沒有重新啟動 F01A 或執行新的 CCG run。
新流程仍須通過第 15 節的使用者批准閘門。

## 15. 使用者批准狀態

使用者已批准全部 35 個工作區依序或小批次執行。每個工作區可以：

- 建立第 12 節登錄的固定資料夾；
- 派出一個且僅一個 Workspace Diagnostic Subagent；
- 建立正式 `issue.md` 與 evidence 文件；
- 啟動該工作區的 CCG reviewer；
- 完成後由 Lead Codex 做輕量流程與寫入範圍驗收。

禁止：

- 派出任何巢狀 agent；
- 以 review agent、investigator agent 或其他代理包裝 CCG 審核；
- 同一個工作區同時由兩個 agent 診斷；
- 由 Lead Codex 代替工作區 agent 進行診斷；
- 未經另外批准進行任何 optimization 或產品程式碼修改。
