# 會友資訊可攜式部署套件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立一個可拖入其他教會專案、讓 AI 先盤點差異再安全適配會友資訊樹狀檢視、頭像、搜尋、明細與手機操作的 UTF-8 文件套件及 ZIP。

**Architecture:** 套件分成「整合導覽文件」與「權威來源／受控參考實作」兩層。第一層提供規格、依賴、Prompt 與遷移流程；第二層保留 9 Specs、9 Plans、權威 requirements/context、功能專屬 source snapshot 與宿主檔案的路徑限定 patch。最後由 manifest、PowerShell verifier 與 ZIP 重現性檢查證明內容完整。

**Tech Stack:** Markdown、JSON、PowerShell 7／Windows PowerShell、Git、SHA-256、ZIP、C#／ASP.NET Core MVC、DevExtreme、Dataverse CRM、Razor／JavaScript。

**Execution constraint:** 使用者要求不 Commit；所有通常的 commit checkpoint 改為 `git status`、`git diff --check`、雜湊與驗證腳本 checkpoint。所有寫入都限於 `.worktrees/Sunny_5.1.2.WorktreeTuneMemberView`。

---

## File ownership map

### Layer 1：可平行

- Source curator owns only:
  - `docs/portable/member-info-portable-kit/original-specs/**`
  - `docs/portable/member-info-portable-kit/original-plans/**`
  - `docs/portable/member-info-portable-kit/authoritative-context/**`
- Integrated-spec writer owns only:
  - `docs/portable/member-info-portable-kit/01-INTEGRATED-SPEC.md`
  - `docs/portable/member-info-portable-kit/02-DEPENDENCY-MATRIX.md`
- Prompt writer owns only:
  - `docs/portable/member-info-portable-kit/03-PROMPT-HISTORY-VERBATIM.md`
  - `docs/portable/member-info-portable-kit/04-PROMPT-PLAYBOOK.md`
- Lead agent works locally on:
  - `docs/portable/member-info-portable-kit/05-MIGRATION-RUNBOOK.md`
  - `docs/portable/member-info-portable-kit/06-ACCEPTANCE-CHECKLIST.md`

### Layer 2：Layer 1 完成後

- Reference curator owns only `docs/portable/member-info-portable-kit/reference-implementation/**`.
- Lead agent owns only:
  - `docs/portable/member-info-portable-kit/00-START-HERE.md`
  - `docs/portable/member-info-portable-kit/07-PRIVACY-REDACTIONS.md`
  - `docs/portable/member-info-portable-kit/verify-package.ps1`
  - `docs/portable/member-info-portable-kit/manifest.json`
  - `docs/portable/member-info-portable-kit.zip`
  - `.ccg/tasks/build-member-info-portable-kit/**`

No two workers may write the same file. Workers must not modify the original MemberInfo application source.

---

### Task 1: Lock the authoritative source inventory

**Files:**
- Create: `docs/portable/member-info-portable-kit/original-specs/*.md`
- Create: `docs/portable/member-info-portable-kit/original-plans/*.md`
- Create: `docs/portable/member-info-portable-kit/authoritative-context/requirements.md`
- Create: `docs/portable/member-info-portable-kit/authoritative-context/context.jsonl`
- Verify source: `docs/superpowers/specs/*.md`
- Verify source: `docs/superpowers/plans/*.md`

- [ ] **Step 1: Assert the exact 9＋9 source list exists**

Run from the worktree root:

```powershell
$specs = @(
  '2026-07-15-member-info-district-group-tree-design.md',
  '2026-07-15-member-info-loading-animation-design.md',
  '2026-07-16-member-detail-gender-birthdate-design.md',
  '2026-07-16-member-info-layout-search-design.md',
  '2026-07-16-member-info-mobile-responsive-typography-design.md',
  '2026-07-16-member-info-session-comments-utf8-design.md',
  '2026-07-16-sort-unassigned-district-last-design.md',
  '2026-07-17-member-info-fixed-identity-columns-design.md',
  '2026-07-17-member-info-resizable-sortable-columns-design.md'
)
$plans = @(
  '2026-07-15-member-info-district-group-tree.md',
  '2026-07-15-member-info-loading-animation.md',
  '2026-07-16-member-detail-gender-birthdate.md',
  '2026-07-16-member-info-layout-search.md',
  '2026-07-16-member-info-mobile-responsive-typography.md',
  '2026-07-16-member-info-session-comments-utf8.md',
  '2026-07-16-sort-unassigned-district-last.md',
  '2026-07-17-member-info-fixed-identity-columns.md',
  '2026-07-17-member-info-resizable-sortable-columns.md'
)
$missing = @($specs | Where-Object { -not (Test-Path "docs/superpowers/specs/$_") }) +
           @($plans | Where-Object { -not (Test-Path "docs/superpowers/plans/$_") })
if ($missing.Count) { throw "Missing authoritative files: $($missing -join ', ')" }
```

Expected: command exits without output. A missing source stops the task.

- [x] **Step 2: Copy sources without transforming bytes**

Use `Copy-Item -LiteralPath` for the 18 files and two context files. Preserve each basename and place Specs／Plans in separate directories. Do not normalize line endings or rewrite headings before the documented privacy-redaction stage.

- [x] **Step 3: Prove copied bytes equal source bytes**

For each copy, compare `(Get-FileHash -Algorithm SHA256)` with its source before privacy redaction. Expected: 20 comparisons are equal: 9 Specs, 9 Plans, `requirements.md`, and `context.jsonl`.

- [x] **Step 4: Verify source directory scope**

Run:

```powershell
(Get-ChildItem 'docs/portable/member-info-portable-kit/original-specs' -File).Count
(Get-ChildItem 'docs/portable/member-info-portable-kit/original-plans' -File).Count
```

Expected: `9` and `9`. No other docs are silently included.

The byte-equality checkpoint proves source lineage before packaging. Task 5A intentionally creates sanitized derivatives; final manifest hashes therefore apply to delivered files, not raw source bytes.

---

### Task 2: Write the integrated behavior and dependency contract

**Files:**
- Create: `docs/portable/member-info-portable-kit/01-INTEGRATED-SPEC.md`
- Create: `docs/portable/member-info-portable-kit/02-DEPENDENCY-MATRIX.md`
- Read: all files under `docs/portable/member-info-portable-kit/original-specs/`
- Read: `docs/portable/member-info-portable-kit/authoritative-context/requirements.md`

- [x] **Step 1: Write the integrated spec with traceable requirement IDs**

Use stable IDs and these exact sections:

```markdown
# 會友資訊整合規格
## 使用方式與規格優先序
## MI-AUTH：角色、範圍與批次授權
## MI-DATA：CRM 欄位、PascalCase DTO 與日期正規化
## MI-TREE：區長→小組→會友樹與排序
## MI-AVATAR：主要照片、LINE、剪影、批次載入、上傳與快取
## MI-DETAIL：明細、關係目標、性別與生日
## MI-SEARCH：搜尋、取消、結果取代、零筆與返回
## MI-LOAD：Loading、錯誤、空狀態與 reduced-motion
## MI-MOBILE：單一捲軸、指頭滑動、字級與觸控
## MI-OBS：診斷、效能與錯誤證據
## MI-ENC：註解、UTF-8 與測試
## 非目標與禁止捷徑
## 原始規格對照表
```

Each behavior must cite at least one relative link under `original-specs/` or `authoritative-context/`. The photo section must explicitly label pre-2026-07-15 avatar behavior as a prerequisite, not pretend it came from the seven Specs.

- [x] **Step 2: Define the dependency matrix schema**

Create a Markdown table with exactly these columns:

```markdown
| ID | 能力 | Sunny 來源 | 目標版本必查 | 適配方式 | 缺少時風險 | 驗證證據 |
|---|---|---|---|---|---|---|
```

Include rows for .NET target, DevExtreme client/server versions, Newtonsoft PascalCase resolver, Dataverse SDK, CRM contact/list/listmember schema, custom district/group/LINE fields, Church／Shepherd claims, `ListManager`, `IMemoryCache`, ImageSharp, LINE channel token lookup, Razor/jQuery/DataGrid, horizontal scrolling, reduced-motion, and xUnit contract tests.

- [x] **Step 3: Add hard-stop rules**

The matrix must require the target AI to stop before coding when CRM logical names, authorization source, photo storage, or LINE credentials contract cannot be established. It must forbid inventing fields, hardcoding secrets, loosening permissions, or replacing batch authorization with per-row service calls.

- [x] **Step 4: Validate traceability**

Run `rg -n 'MI-(AUTH|DATA|TREE|AVATAR|DETAIL|SEARCH|LOAD|MOBILE|OBS|ENC)'` on both files. Expected: every integrated-spec section has IDs and the dependency matrix points back to the relevant IDs.

---

### Task 3: Preserve prompt history and create reusable prompts

**Files:**
- Create: `docs/portable/member-info-portable-kit/03-PROMPT-HISTORY-VERBATIM.md`
- Create: `docs/portable/member-info-portable-kit/04-PROMPT-PLAYBOOK.md`
- Read: the current conversation transcript available to the lead agent

- [x] **Step 1: Transcribe the user-visible MemberInfo prompt sequence**

Record each available user message in chronological order with an ordinal, category, and fenced `text` block. Preserve markers such as `[Image #1]`; do not invent image contents. Include the early failure reports, relation-goal correction, Loading request, detail gender/birthdate, layout/search decisions, narrow-screen behavior, unassigned district sorting, responsive typography, iOS focus zoom, comments/UTF-8, and portable-kit request/approvals.

- [x] **Step 2: Separate environment-only operations**

Move worktree creation/merge, port restart, DLL lock, branch queries, Commit preferences, and hook-status prompts to `## 環境操作歷程（不可直接複製）`. Explain why absolute paths, PIDs, ports, branch names, and process commands must be rediscovered in each target repo.

- [x] **Step 3: Disclose transcript limits**

State that entries are verbatim only for user messages available in this session context. If an image is unavailable, retain its marker and accompanying text without claiming visual details. Do not call a reconstructed summary a verbatim quote.

- [x] **Step 4: Write nine copy-ready prompt stages**

Create these sections in `04-PROMPT-PLAYBOOK.md`:

```markdown
## Prompt 0：只讀盤點與差異報告
## Prompt 1：遷移設計與使用者核准
## Prompt 2：頭像基礎能力
## Prompt 3：權限、DTO 與樹狀 API
## Prompt 4：樹狀 UI 與照片批次載入
## Prompt 5：搜尋與 Loading 狀態機
## Prompt 6：會友明細、性別、生日與關係目標
## Prompt 7：手機響應式、單一捲軸與手勢
## Prompt 8：完整測試、瀏覽器驗收與交付報告
```

Every Prompt must contain: `目的`, `先讀`, `只允許`, `禁止`, `必須驗證`, `停止條件`, and `輸出`. Prompt 0 must prohibit edits; Prompt 1 must obtain design approval; Prompts 2-7 must prohibit Commit; Prompt 8 must report evidence and leave Commit to the user.

- [x] **Step 5: Check standalone readability**

Read `04-PROMPT-PLAYBOOK.md` without other conversation context. Confirm Prompt 0 identifies the ZIP entry file, target repo, required diff report, and safety stops; no prompt relies on undefined words such as “照之前” or “同上”.

---

### Task 4: Write the migration runbook and acceptance checklist

**Files:**
- Create: `docs/portable/member-info-portable-kit/05-MIGRATION-RUNBOOK.md`
- Create: `docs/portable/member-info-portable-kit/06-ACCEPTANCE-CHECKLIST.md`
- Read: `docs/superpowers/specs/2026-07-16-member-info-portable-deployment-kit-design.md`

- [x] **Step 1: Write the operator runbook**

Use the exact lifecycle:

```text
建立目標分支或 worktree
→ 拖入並解壓 ZIP
→ 讓 AI 讀 00-START-HERE.md
→ 執行 Prompt 0 只讀盤點
→ 人工核准 Prompt 1 的遷移設計
→ 依 Prompt 2～7 分階段實作與測試
→ 執行 Prompt 8 完整驗收
→ 人工檢視 git diff
→ 使用者自行 Commit／合併
```

Include copyable starter wording, where to place the kit, how to tell source kit files from target app files, how to pause on missing CRM fields, and how to remove the kit after migration without touching application changes.

- [x] **Step 2: Add rollback guidance**

Use branch/worktree isolation and ordinary revert/discard of the isolated migration branch. Do not include `git reset --hard`, recursive deletion, fixed absolute paths, or process-kill instructions.

- [x] **Step 3: Write evidence-oriented acceptance sections**

Use checkbox tables for: environment/dependencies, authorization, CRM/data contract, district/group/member tree, avatar and LINE, detail fields, relation-goal, Loading/error/empty states, search/cancel/return, desktop layout, mobile gesture/typography, accessibility, performance/batching, tests, UTF-8/security, and final diff review.

Each row must have `驗收項目`, `操作／命令`, `預期結果`, and `證據` columns. Do not pre-check boxes.

- [x] **Step 4: Add high-risk negative tests**

Explicitly test unauthorized list/contact access, malformed GUIDs, missing CRM optional fields, LINE 403/404/timeouts, image upload type/size rejection, search cancellation, multiple/zero results, repeated group expand, repeated detail open, narrow iPhone viewport, reduced-motion, and duplicate horizontal scrollbar prevention.

---

### Task 5: Build the controlled reference implementation

**Files:**
- Create: `docs/portable/member-info-portable-kit/reference-implementation/README.md`
- Create: `docs/portable/member-info-portable-kit/reference-implementation/feature-files/**`
- Create: `docs/portable/member-info-portable-kit/reference-implementation/tests/**`
- Create: `docs/portable/member-info-portable-kit/reference-implementation/host-integration/01-photo-prerequisite.patch`
- Create: `docs/portable/member-info-portable-kit/reference-implementation/host-integration/02-member-info-2026-07-15-plus.patch`
- Create: `docs/portable/member-info-portable-kit/reference-implementation/host-integration/03-member-info-fixed-identity-columns.patch`
- Create: `docs/portable/member-info-portable-kit/reference-implementation/host-integration/SOURCE-MAP.md`
- Read only: current application source and Git history

- [x] **Step 1: Copy feature-owned source files byte-for-byte**

Copy these current files while preserving their relative paths below `feature-files/`:

```text
ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs
ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs
ChurchReport/Services/MemberInfo/MemberInfoAccess.cs
ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs
ChurchReport/Services/MemberInfo/MemberInfoCurrentContactCounter.cs
ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs
ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs
ChurchReport/Services/MemberInfo/RelationGoalFormatter.cs
ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs
ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs
ChurchReport/ViewModels/MemberInfoDetailViewModel.cs
ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs
```

Verify every copy against the current source SHA-256.

- [x] **Step 2: Copy the test contracts byte-for-byte**

Copy `ChurchReport.MemberInfo.Tests/*.cs` and `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` below `reference-implementation/tests/`, preserving the project directory name. Verify SHA-256 against source.

- [x] **Step 3: Generate the photo prerequisite patch**

Resolve `2471ea4e^` and create a path-limited UTF-8 unified diff through `17043805` for only:

```text
ChurchReport/Controllers/MemberInfoController.cs
ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml
ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs
ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs
ChurchReport/ViewModels/MemberInfoDetailViewModel.cs
ChurchReport/ChurchReport.csproj
```

Record both endpoint commit IDs in `SOURCE-MAP.md`. This patch is historical reference only and must carry a warning not to run `git apply` blindly.

- [x] **Step 4: Generate the July 15-plus patch**

Create a path-limited UTF-8 unified diff from `8ebb47a0` through the current package source commit `320ab43851c8` for the same host files plus `ChurchReport/Startup.cs` and all feature/test files. Record the exact endpoints and generation command in `SOURCE-MAP.md`.

- [x] **Step 5: Write host method and dependency map**

`SOURCE-MAP.md` must index the controller actions `Index`, `LoadDistrictTree`, `SearchDistrictTree`, `LoadGroupMembers`, `LoadUngroupedMembers`, `Detail`, `GetContactImage`, `GetContactImagesBatch`, `ResyncLineCandidateIds`, `ResyncLineProfiles`, `UploadContactImage`, and `UpdateContactInfo`; it must also identify serializer, ImageSharp, ContactAvatar, CRM, LINE, cache, Razor, and test dependencies.

- [x] **Step 6: Scan reference files for secrets and personal data**

Search case-insensitively for `password`, `secret`, `apikey`, `connectionstring`, `ChannelAccessToken`, `clientsecret`, phone/email patterns, and known local absolute-path prefixes. Configuration key names may remain only when README explains they are lookups, but literal values must fail the task. Patches must not contain `appsettings`, publish profiles, `bin`, `obj`, CRM exports, real photos, or runtime logs.

- [x] **Step 7: Add the approved 2026-07-17 fixed-identity increment**

Copy the sanitized 2026-07-17 Spec／Plan, refresh the `MemberInfoTreeViewContractTests.cs` reference snapshot, and generate `03-member-info-fixed-identity-columns.patch` from `320ab43851c8` to `b3c50550deefb9cb7031ea938fce592366459448` for only `MemberInfoGrid.cshtml` and `MemberInfoTreeViewContractTests.cs`. Document that this incremental patch is for reading and adaptation only, not direct `git apply` on another church version.

- [ ] **Step 8: Add the approved 2026-07-17 resizable／sortable-column increment**

Copy the sanitized resizable／sortable Spec and Plan, refresh the `MemberInfoTreeViewContractTests.cs` reference snapshot, and generate `04-member-info-resizable-sortable-columns.patch` from the fixed-column package source `526b533d4` through `b238d96871fdd490a2a0493e27869753e86baae8` for only `MemberInfoGrid.cshtml` and `MemberInfoTreeViewContractTests.cs`. Document 96／80px, avatar opt-out, widget resizing, single sorting, remote `RelationGoals` protection, exact endpoints, and the warning not to apply the patch blindly.

---

### Task 5A: Sanitize portable derivatives consistently

**Files:**
- Create: `docs/portable/member-info-portable-kit/07-PRIVACY-REDACTIONS.md`
- Modify: `docs/portable/member-info-portable-kit/00-START-HERE.md`
- Modify: `docs/portable/member-info-portable-kit/03-PROMPT-HISTORY-VERBATIM.md`
- Modify: `docs/portable/member-info-portable-kit/original-specs/**`
- Modify: `docs/portable/member-info-portable-kit/original-plans/**`
- Modify: `docs/portable/member-info-portable-kit/reference-implementation/**`

- [x] **Step 1: Run the failing whole-package privacy scan**

Scan all portable text for the known session fixture identities, fixed local repository／desktop roots, localhost port, process PID, email and mobile patterns. Expected before the fix: non-zero findings across prompt history, original docs, tests and patches; this proves the root cause is cross-layer raw-source propagation rather than one document.

- [x] **Step 2: Apply one consistent non-reversible redaction policy**

Replace possible real people／organization examples with `會友甲／乙`, `區長甲／乙`, `範例小組`, `範例牧區`; replace machine values with `<來源儲存庫根目錄>`, `<本機參考圖片路徑>`, `<本機連接埠>`, `<程序 PID>`. Do not store an original-value map. Preserve code structure, assertions and requirement semantics.

- [x] **Step 3: Disclose sanitized lineage**

Explain in `07-PRIVACY-REDACTIONS.md`, prompt history, reference README and SOURCE-MAP that final copies are sanitized derivatives. `original-*` identifies source category, not byte equality; Git commands document raw lineage but cannot reconstruct a byte-identical delivered patch.

- [ ] **Step 4: Prove privacy and behavior after redaction**

Require zero privacy-scan findings, strict UTF-8/U+FFFD=0, intact 7＋7 inventory, MemberInfo tests passing from application source, and package verifier success. Recreate manifest and ZIP after every redaction-related edit.

---

### Task 6: Write the package entry point

**Files:**
- Create: `docs/portable/member-info-portable-kit/00-START-HERE.md`
- Read: `01-INTEGRATED-SPEC.md` through `06-ACCEPTANCE-CHECKLIST.md`
- Read: `reference-implementation/README.md`

- [x] **Step 1: Write the one-file onboarding path**

Include these exact headings:

```markdown
# 會友資訊可攜式部署套件：從這裡開始
## 這個套件能做什麼
## 重要安全警告
## 拖入其他教會專案後的第一個 Prompt
## 閱讀順序
## 三種使用方式
## 何時必須停止並詢問
## 如何驗證套件本身
## 如何完成遷移
## 來源與版本
```

The first Prompt must tell the target AI to read this file, inspect but not modify the target repo, then execute Prompt 0 from `04-PROMPT-PLAYBOOK.md`. It must say the reference implementation is evidence, not an overwrite source.

- [x] **Step 2: Add three usage modes**

Document: full staged migration, single-feature adaptation, and audit-only comparison. Full migration is recommended; single-feature mode must still run dependency inventory; audit-only mode never edits.

- [x] **Step 3: Link every top-level artifact**

Use only relative links that remain valid after ZIP extraction. Link all six numbered docs, both original directories, authoritative context, reference implementation, `manifest.json`, and `verify-package.ps1`.

---

### Task 7: Generate manifest and verifier

**Files:**
- Create: `docs/portable/member-info-portable-kit/verify-package.ps1`
- Create: `docs/portable/member-info-portable-kit/manifest.json`
- Read: every finalized file under `docs/portable/member-info-portable-kit/`

- [ ] **Step 1: Implement strict text-file detection and decoding**

The verifier must treat `.md`, `.json`, `.jsonl`, `.cs`, `.cshtml`, `.csproj`, `.patch`, and `.ps1` as text. Use `New-Object System.Text.UTF8Encoding($false, $true)` and fail on decoding exceptions or U+FFFD.

- [ ] **Step 2: Implement manifest verification**

For every manifest entry, resolve the relative POSIX path under the kit root, reject path traversal, assert the file exists, compare byte length and SHA-256, and assert the `utf8` flag for text files. Then enumerate kit files excluding `manifest.json` and require exact set equality with manifest paths.

- [ ] **Step 3: Implement Markdown relative-link checks**

Parse inline Markdown links with relative targets, ignore `http:`, `https:`, `mailto:`, anchors, and fenced-code examples, URL-decode the path, resolve it relative to the containing file, and fail if the target is absent. Reject `file://`, drive-root absolute paths, and parent traversal outside the kit root.

- [ ] **Step 4: Implement manifest generation**

Generate deterministic JSON with:

```json
{
  "formatVersion": 1,
  "kitId": "member-info-portable-kit",
  "source": {
    "branch": "Sunny_5.1.2.WorktreeTuneMemberView",
    "commit": "b238d96871fdd490a2a0493e27869753e86baae8",
    "documentRangeStart": "2026-07-15"
  },
  "files": []
}
```

Populate `files` sorted by ordinal relative path with `path`, `role`, `sourcePath` when copied, `bytes`, `sha256`, and `utf8`. Exclude only `manifest.json` itself.

- [ ] **Step 5: Run verifier before creating ZIP**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/portable/member-info-portable-kit/verify-package.ps1
```

Expected: exit code 0 and a summary containing the verified file count, strict UTF-8 count, resolved Markdown link count, and SHA-256 success.

---

### Task 8: Create and independently verify the ZIP

**Files:**
- Create: `docs/portable/member-info-portable-kit.zip`
- Verify: `docs/portable/member-info-portable-kit/manifest.json`

- [ ] **Step 1: Remove only a previous ZIP at the exact target path**

Resolve `docs/portable/member-info-portable-kit.zip`, verify its parent is the worktree `docs/portable` directory, and if it exists remove only that file with `Remove-Item -LiteralPath`. Do not recursively delete directories.

- [ ] **Step 2: Create the ZIP with one top-level kit directory**

Use `System.IO.Compression.ZipFile` or `Compress-Archive` so extraction yields `member-info-portable-kit/00-START-HERE.md`, not loose files and not an extra nested duplicate directory.

- [ ] **Step 3: Extract ZIP to a disposable verified location**

Resolve a temporary directory below the worktree, verify its absolute prefix, extract there, run the packaged verifier against the extracted kit, and compare every manifest entry with the source kit.

- [ ] **Step 4: Clean only the verified temporary directory**

Before recursive cleanup, resolve the absolute temp directory and assert it is a direct child of the intended worktree temp root and not equal to the root. Use native PowerShell `Remove-Item -LiteralPath -Recurse` only after this assertion.

- [ ] **Step 5: Record ZIP evidence**

Write ZIP byte length and SHA-256 into `.ccg/tasks/build-member-info-portable-kit/review.md`, not inside the self-contained manifest.

---

### Task 9: Run source and package quality gates

**Files:**
- Create: `.ccg/tasks/build-member-info-portable-kit/review.md`
- Modify: `.ccg/tasks/build-member-info-portable-kit/task.json`
- Verify only: application/test source

- [ ] **Step 1: Run the MemberInfo contract test project**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --configuration Debug
```

Expected: all MemberInfo tests pass. If a Visual Studio process locks the default output, rerun with a verified alternate `BaseOutputPath` under the worktree; do not kill the user’s IDE or app without explicit permission.

- [ ] **Step 2: Run repository checks**

Run `git diff --check`, strict UTF-8 validation for every new text file, U+FFFD scan, and `git status --short`. Expected: only package/task/spec/plan files plus the user’s pre-existing `.ccg/tasks/fix-member-info-tree-loading/task.json` change; no application source changes.

- [ ] **Step 3: Retry dual-model review in parallel**

Invoke Gemini frontend reviewer and Claude reviewer concurrently using the AGENTS.md wrapper template. Ask both to review package completeness, safety, prompt usability, migration ordering, reference snapshot boundaries, and verification evidence. Record exact success or failure output; never convert Gemini 403 or Claude wrapper failure into a passing review.

- [ ] **Step 4: Consolidate review findings**

Write `review.md` with `Critical`, `Warning`, and `Info`. Fix all Critical issues, rerun the verifier/ZIP checks, then repeat both model reviews. Warnings must be fixed or explicitly accepted with rationale.

- [ ] **Step 5: Run requirement-by-requirement completion audit**

Map each requirement in `.ccg/tasks/build-member-info-portable-kit/requirements.md` to authoritative evidence: file path, verifier output, test output, model review state, ZIP hash, or git diff. Missing or indirect evidence means the task remains in progress.

- [ ] **Step 6: Mark task complete without committing**

Only after every requirement has direct evidence, set `status` and `currentPhase` to `completed`; set `nextAction` to state that the kit is ready for user inspection and no Commit was created. Do not archive because the user explicitly owns the Commit and archive workflow.

---

## Self-review coverage map

| Design requirement | Implemented by |
|---|---|
| 9 Specs＋9 Plans and authoritative context | Task 1／Task 5 Steps 7–8 |
| Integrated final behavior and photo prerequisite | Task 2 |
| Verbatim available prompts and environment appendix | Task 3 |
| Copy-ready staged prompts | Task 3 |
| Migration teaching and rollback | Task 4 |
| Evidence-oriented acceptance | Task 4 |
| Controlled source snapshot and host patches | Task 5 |
| Non-reversible privacy sanitization across all derivatives | Task 5A |
| One-file drag-and-read onboarding | Task 6 |
| Manifest, UTF-8, hash and link validation | Task 7 |
| Portable ZIP and independent extraction test | Task 8 |
| Tests, dual review, diff scope and completion audit | Task 9 |

The plan contains no step that modifies existing MemberInfo behavior, copies secrets, commits changes, or blindly applies Sunny host files to a target church version.
