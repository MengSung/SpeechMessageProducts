# P7.4 MemberInfo 小組樹授權來源稽核 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 對 ORG-CALL-00031／00032 交付可驗證的安全設計決策，避免把 Session、共享 ListManager 或保存帳密誤當成 Gateway 授權邊界。

**Architecture:** 此工作是 source-only no-go audit，不產生 runtime capability。它從 matrix、MemberInfo controller 與隔離規格建立 Church／Shepherd 授權資料流，判斷是否在任何 CRM I/O、profile/host composition 或 browser locator 解析前已有 request-local server-derived scope。

**Tech Stack:** .NET/ASP.NET Core source contracts、Trellis task records、CCG bounded architecture review、PowerShell/JSON/byte-level encoding checks。

---

### Task 1: 建立可追溯的 source-audit 與驗收範圍

**Files:**
- Modify: `.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/prd.md`
- Create: `.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/source-audit.md`
- Create: `.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/design.md`
- Test: `.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/implement.jsonl`

- [x] **Step 1: 寫出可失敗的稽核驗收條件**

```markdown
- [ ] source-audit 對應 00031／00032、GetAccess、Church/Shepherd branch、
      EnsureShepherdListsLoaded 與 legacy SDK bridge。
- [ ] design 明確決定 runtime capability 是否可安全實作。
```

- [x] **Step 2: 以來源證據證明現行 flow 不符合條件**

Run:

```powershell
rg -n -C 4 'GetAccess\(|GetShepherdListIds|EnsureShepherdListsLoaded|SetupListManager|FetchSmallGroupDescriptors|FetchGroupMemberships' SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
```

Expected: `GetAccess` 可讀寫 Session、Shepherd scope 使用 `InMemoryContext.ListManager`，且未載入時以保存帳密呼叫 `SetupListManager`。

- [x] **Step 3: 寫入最小 no-go 設計**

```markdown
authenticated principal
  -> server-derived immutable MemberInfo scope
  -> Church or Shepherd capability selected on server
  -> request-local, bounded authorized list ID allowlist
```

禁止 Session、InMemoryContext、ListManager、credential、browser locator 或 raw SDK state 作為 scope authority。

- [x] **Step 4: 驗證 manifest 可解析**

Run:

```powershell
python ./.trellis/scripts/task.py validate .trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit
```

Expected: `implement.jsonl` 與 `check.jsonl` 都顯示 curated entry 數量且驗證通過。

### Task 2: 執行有界架構審查與本機 fail-closed 決策

**Files:**
- Modify: `.ccg/tasks/p74-memberinfo-smallgroup-tree-authorization-audit/review.md`
- Create: `.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/check.md`
- Modify: `.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/implement.md`

- [x] **Step 1: 發起限時架構審查**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Start-CcgDualModelRun.ps1 `
  -Role architect `
  -Title p74-memberinfo-smallgroup-tree-authorization-audit-analysis `
  -PromptFile .\.ccg\dual-model-runs\p74-memberinfo-smallgroup-tree-authorization-audit-analysis-input.md `
  -RepositoryPath <worktree-root> `
  -OutputDirectory .\.ccg\dual-model-runs `
  -AllowSingleModelWhenQuotaBlocked
```

Expected: 最多等待 45 秒；若無 usable output，紀錄「雙模型未完成」並改由本機來源驗證，不重試等待。

- [ ] **Step 2: 將 no-go 與恢復條件寫入 check/review**

```markdown
恢復條件：獨立 child 先建立 request-local、server-derived MemberInfo scope；
Shepherd assignment 不可來自 legacy loader；scope 必須先於 locator/cache/client/CRM I/O。
```

- [ ] **Step 3: 本機驗證 source-only scope**

Run:

```powershell
git diff --check
git diff --name-only -- .trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit .ccg/tasks/p74-memberinfo-smallgroup-tree-authorization-audit
```

Expected: 僅 task／CCG 文件；沒有 runtime、matrix、gate、CE、traffic、P7.5 或 P8 變更。

### Task 3: 回寫 P7/P8 路線並封存 child

**Files:**
- Modify: `.trellis/tasks/08-05-gateway-purpose-and-positioning/{prd.md,design.md,implement.md,roadmap-p5-p7.md,task.json}`
- Modify: `.trellis/tasks/08-12-churchreport-productclient-cutover/task.json`
- Modify: `.ccg/tasks/p74-memberinfo-smallgroup-tree-authorization-audit/task.json`

- [ ] **Step 1: 寫入 parent checkpoint**

```markdown
ORG-CALL-00031／00032 是獨立 source-only local design no-go；不阻擋後續不相依 P7 capability。
```

- [ ] **Step 2: 執行 JSON、編碼與 scope 檢查**

Run:

```powershell
python -m json.tool .trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/task.json > $null
python -m json.tool .ccg/tasks/p74-memberinfo-smallgroup-tree-authorization-audit/task.json > $null
git diff --check
```

Expected: JSON 可解析、所有 child 文件 UTF-8 無 BOM／CRLF／final CRLF，且 diff scope 符合 task/parent checkpoint。

- [ ] **Step 3: scope-only commit 與 archive**

```powershell
git add .trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit .ccg/tasks/p74-memberinfo-smallgroup-tree-authorization-audit <parent checkpoint files>
git commit -m "docs(p74): record smallgroup tree authorization no-go"
python ./.trellis/scripts/task.py archive 08-14-p74-memberinfo-smallgroup-tree-authorization-audit
```

Expected: child archive 不包含其他工作樹變更；P7.4 parent 的 next action 轉向下一個不相依 matrix family。
