# Member Info Deployment Manual Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 強化既有會友資訊跨教會版本遷移操作手冊，使第一次操作的人可從快速開始、第一個 Prompt、Prompt 0～8 關卡表一路完成遷移驗收與正式部署決策。

**Architecture:** `00-START-HERE.md` 維持拖入套件後的入口，`05-MIGRATION-RUNBOOK.md` 成為人員操作的單一權威手冊；`04-PROMPT-PLAYBOOK.md` 與 `06-ACCEPTANCE-CHECKLIST.md` 繼續分別承擔完整 Prompt 與證據記錄。修改完成後由既有 verifier 產生 deterministic manifest，再建立單一頂層 ZIP 並從解壓內容重新驗證。

**Tech Stack:** Markdown、PowerShell 5.1、Git、`verify-package.ps1`、JSON Manifest、ZIP。

**限制：** 不修改 application source／tests／reference patches；依使用者要求不 Commit、merge、push 或歸檔。

---

### Task 1: 建立操作手冊結構的 RED 基線

**Files:**
- Test only: `docs/portable/member-info-portable-kit/05-MIGRATION-RUNBOOK.md`

- [x] **Step 1: 執行尚未滿足的新結構檢查**

Run:

```powershell
$manual = Get-Content -Raw -Encoding utf8 `
  docs/portable/member-info-portable-kit/05-MIGRATION-RUNBOOK.md

$required = @(
  '快速開始：第一次部署照這一頁操作',
  '可以直接複製的第一個 Prompt',
  'Prompt 0～8 操作關卡表',
  '功能遷移完成不等於正式部署',
  '操作人員完成檢查表'
)

$missing = @($required | Where-Object { -not $manual.Contains($_) })
if ($missing.Count -gt 0) {
    throw "Missing manual sections: $($missing -join ', ')"
}
```

Expected: FAIL，並列出目前尚未存在的快速操作章節。

### Task 2: 強化既有 `05-MIGRATION-RUNBOOK.md`

**Files:**
- Modify: `docs/portable/member-info-portable-kit/05-MIGRATION-RUNBOOK.md`

- [x] **Step 1: 在標題後加入快速開始定位**

加入以下章節，不刪除既有 1～11 章：

```markdown
## 快速開始：第一次部署照這一頁操作

本套件協助的是「把會友資訊功能遷移到另一個教會版本」，不是直接把網站發佈到正式環境。先完成 Prompt 0～8、測試與人工驗收，才由操作人員依目標教會原有 release procedure 決定 Commit、merge 與正式部署。
```

- [x] **Step 2: 加入八步快速流程表**

表格欄位固定為「步驟／操作／完成判定／不得繼續的情況」，內容依序為：

1. 建立隔離 branch/worktree，記錄 HEAD 與 dirty files。
2. 把 `member-info-portable-kit/` 放在目標 repository 文件區。
3. 執行 `verify-package.ps1`，必須 exit 0。
4. 貼上第一個 Prompt，只執行 Prompt 0 只讀盤點。
5. 執行 Prompt 1，使用者明確批准遷移設計。
6. Prompt 2～7 逐階段實作、測試與人工 gate。
7. Prompt 8 與 `06-ACCEPTANCE-CHECKLIST.md` 收集直接證據。
8. 人工檢查 application diff，再由使用者自行 Commit／merge／publish。

- [x] **Step 3: 加入完整可複製的第一個 Prompt**

使用以下文字，安全語意必須與 `00-START-HERE.md` 一致：

```text
KIT_ROOT = docs/portable/member-info-portable-kit

請先把 KIT_ROOT 視為本套件在目前 repository 中的實際根目錄，並完整閱讀 KIT_ROOT/00-START-HERE.md。若套件不在預設位置，只修改 KIT_ROOT，不要猜本機絕對路徑。

這是其他教會版本的會友資訊遷移工作。此階段只允許讀取與分析：不可修改檔案、不可套 patch、不可安裝套件、不可 Commit、不可假設 Sunny 的 CRM 欄位、權限、DevExtreme、LINE 或照片儲存方式與本專案相同。

KIT_ROOT/reference-implementation/ 只提供行為與整合證據，不是覆蓋來源；不可直接複製宿主檔案或套用其中 patch。

閱讀完成後，請執行 KIT_ROOT/04-PROMPT-PLAYBOOK.md 的「Prompt 0：只讀盤點與差異報告」，先回報 repository root、branch/worktree、HEAD、dirty files、技術與套件版本、MemberInfo 相關檔案、CRM schema、角色與授權來源、照片／LINE／快取契約、現有測試與阻擋項。本階段不要實作。
```

- [x] **Step 4: 加入 Prompt 0～8 操作關卡表**

表格欄位使用「階段／目的／AI 可執行事項／操作人員批准點／必要證據」，逐列列出：

- Prompt 0：只讀盤點；不得修改。
- Prompt 1：遷移設計；使用者批准後才可寫入。
- Prompt 2：頭像基礎能力；照片來源、fallback、批次與快取證據。
- Prompt 3：權限、DTO、樹狀 API；正向／負向授權與資料契約。
- Prompt 4：樹狀 UI、照片批次載入；多區、多組、Ungrouped 與重複展開。
- Prompt 5：搜尋與 Loading；多筆、零筆、取消、返回、錯誤與競態。
- Prompt 6：明細、性別、生日、關係目標；正確 contact 與重複開啟。
- Prompt 7：手機、欄位、摘要、fixed／resize／sort／touch；三種 grid 和真機矩陣。
- Prompt 8：完整測試與交付；`06-ACCEPTANCE-CHECKLIST.md` 直接證據。

- [x] **Step 5: 加入功能遷移與正式部署分界**

章節標題必須為：

```markdown
## 功能遷移完成不等於正式部署
```

內容分成兩個清單：

- 功能遷移完成：Prompt 8、build/tests、`git diff --check`、桌機／手機、權限、效能、隱私與 publish 排除全部有證據。
- 正式部署：人工檢查 diff、自行 Commit／merge、使用目標版本原有 CI/CD／Visual Studio publish／release procedure、由正式環境安全管道供應 secrets、上線後 smoke test 與既有 rollback。

不得提供假設所有教會都適用的 IIS、container、server path 或 publish profile 指令。

- [x] **Step 6: 加入操作人員完成檢查表**

以 Markdown checkbox 列出 repository/worktree、verifier、Prompt 0、Prompt 1 批准、Prompt 2～7 分階段證據、Prompt 8、diff／隱私／權限／N+1、目標 release procedure、smoke test／rollback。連結至 `06-ACCEPTANCE-CHECKLIST.md`，不重複展開全部驗收矩陣。

- [x] **Step 7: 重跑結構檢查**

重跑 Task 1 的 PowerShell。

Expected: PASS，`$missing.Count` 為 0。

### Task 3: 同步入口文件定位與安全語意

**Files:**
- Modify: `docs/portable/member-info-portable-kit/00-START-HERE.md`

- [x] **Step 1: 強化手冊描述**

把套件內容中的 Runbook 說明調整為：

```markdown
- [遷移操作手冊](05-MIGRATION-RUNBOOK.md)：包含第一次操作快速開始、可直接複製的第一個 Prompt、Prompt 0～8 關卡、功能遷移／正式部署分界、回復方式；完成後再使用[驗收清單](06-ACCEPTANCE-CHECKLIST.md)記錄直接證據。
```

- [x] **Step 2: 在閱讀順序中明示操作人員入口**

在 `00-START-HERE.md` 的閱讀順序中，確保第一次接手的 AI 仍先讀 `00` 與 Prompt 0；人工操作人員則可先讀 `05` 的快速開始，再依其連結回到 `00／04／06`。

- [x] **Step 3: 驗證安全語意一致**

Run:

```powershell
$start = Get-Content -Raw -Encoding utf8 docs/portable/member-info-portable-kit/00-START-HERE.md
$manual = Get-Content -Raw -Encoding utf8 docs/portable/member-info-portable-kit/05-MIGRATION-RUNBOOK.md

$required = @('只允許讀取與分析', '不可套 patch', '不可 Commit', '不可假設 Sunny')
foreach ($token in $required) {
    if (-not $start.Contains($token) -or -not $manual.Contains($token)) {
        throw "Prompt safety mismatch: $token"
    }
}
```

Expected: PASS。

### Task 4: 重建 Manifest 與 ZIP

**Files:**
- Modify: `docs/portable/member-info-portable-kit/manifest.json`
- Modify: `docs/portable/member-info-portable-kit.zip`

- [x] **Step 1: 產生 deterministic manifest**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File docs/portable/member-info-portable-kit/verify-package.ps1 `
  -GenerateManifest
```

Expected: manifest generation 成功，沒有 `.work` 或其他暫存檔殘留。

- [x] **Step 2: 驗證來源套件**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File docs/portable/member-info-portable-kit/verify-package.ps1
```

Expected: PASS，所有 manifest files、strict UTF-8、SHA-256 與 Markdown links 通過。

- [x] **Step 3: 以單一頂層目錄重建 ZIP**

Run from repository root:

```powershell
Compress-Archive `
  -LiteralPath docs/portable/member-info-portable-kit `
  -DestinationPath docs/portable/member-info-portable-kit.zip `
  -Force
```

- [x] **Step 4: 解壓到安全暫存目錄並執行 ZIP 內 verifier**

使用 `$env:TEMP/member-info-kit-verify-<GUID>`，先確認解析後路徑位於系統 temp 之下，再解壓 ZIP。驗證：

- 正規化 `\` 為 `/` 後只有 `member-info-portable-kit/` 一個頂層。
- ZIP files 數量等於 manifest files + `manifest.json`。
- ZIP 內 `verify-package.ps1` exit 0。
- 記錄 ZIP bytes 與 SHA-256。

### Task 5: 最終範圍、隱私與交付驗證

**Files:**
- Modify: `.ccg/tasks/enhance-member-info-deployment-manual/task.json`
- Create: `.ccg/tasks/enhance-member-info-deployment-manual/review.md`

- [x] **Step 1: 驗證沒有 application 變更**

以本任務開始時的 application paths 為基準，確認本任務 diff 只增加／修改：`00`、`05`、manifest、ZIP、核准 spec／plan 與本 task records。不得修改 `ChurchReport/`、`ChurchReport.MemberInfo.Tests/` 或 reference patches。

- [x] **Step 2: 執行文字與隱私檢查**

對套件及本任務 spec／plan／task files 執行 strict UTF-8 decode、U+FFFD=0、真實姓名／hostname／絕對路徑／固定 port／PID／secret value 掃描。

- [x] **Step 3: 執行品質檢查**

Run:

```powershell
git diff --check
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .ccg/tasks/build-member-info-portable-kit/test-verify-package.ps1
```

Expected: `git diff --check` exit 0；verifier hostile-fixture suite exit 0，平台不支援項只能以既有明示 SKIP 出現。

- [x] **Step 4: 平行執行 Gemini 與 Claude 最終審查**

兩個模型均審查本任務 scoped diff，輸出 Critical／Warning／Info。外部服務不可用時記錄 exit code 與原因，不得寫成審查通過。

- [x] **Step 5: 寫入 review 與完成 task**

`review.md` 必須記錄結構檢查、Prompt 安全一致性、verifier、ZIP、UTF-8、links、privacy、scope 與外部審查狀態。全部必要證據成立後，把 `task.json` 設為 `completed`；依使用者要求不 Commit、不歸檔。
