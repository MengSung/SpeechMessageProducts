# 會友資訊 Portable Kit Metadata 排序增量更新 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將已驗收並推送的 metadata rank 排序完整納入 portable kit，重建可驗證的 Manifest 與 ZIP。

**Architecture:** 以 `589f0baa → 2406b126` 的 path-limited patch 保存宿主整合證據；功能專屬 source/test 採快照，整合文件描述跨教會適配規則。Manifest 由既有 verifier 機械生成，ZIP 解壓後再次用套件內 verifier 驗證。

**Tech Stack:** Markdown、PowerShell 5.1、Git、SHA-256、System.IO.Compression、ASP.NET Core／DevExtreme／Dataverse 參考程式、xUnit。

---

### Task 1: 固定來源與任務狀態

**Files:**
- Modify: `.ccg/tasks/tune-member-info-layout-search/task.json`
- Modify: `.ccg/tasks/sort-member-info-by-commitment-type/task.json`
- Modify: `.ccg/tasks/refresh-member-info-portable-kit-metadata-order/task.json`

- [x] 驗證 `HEAD` 與 upstream 都是 `2406b126e989cc980e8cada9da0e07a2ede1e08d`，且使用者已回報 VS 實測通過。
- [x] 將前兩個已驗收 task 標示 `completed`；新 task 推進至 `implementation`。
- [x] 不 Commit；以 `git diff --check` 作為 checkpoint。

### Task 2: 同步原始決策與整合文件

**Files:**
- Create: `docs/portable/member-info-portable-kit/original-specs/2026-07-18-member-info-commitment-type-sorting-design.md`
- Create: `docs/portable/member-info-portable-kit/original-plans/2026-07-18-member-info-commitment-type-sorting.md`
- Modify: `00-START-HERE.md`, `01-INTEGRATED-SPEC.md`, `02-DEPENDENCY-MATRIX.md`
- Modify: `03-PROMPT-HISTORY-VERBATIM.md`, `04-PROMPT-PLAYBOOK.md`
- Modify: `05-MIGRATION-RUNBOOK.md`, `06-ACCEPTANCE-CHECKLIST.md`, `07-PRIVACY-REDACTIONS.md`
- Modify: `authoritative-context/requirements.md`, `authoritative-context/context.jsonl`

- [x] 納入新 Spec／Plan 並將 10／10 更新為 11／11；Spec 保持 byte-identical，Plan 命中本機路徑／連接埠／PID 後依隱私政策成為有雜湊 lineage 的 sanitized derivative。
- [x] 新增 metadata rank、Unknown／Empty、正反向與遠端跨 segment 分頁契約。
- [x] Prompt Playbook 新增獨立 Prompt 8，原最終驗收改為 Prompt 9；同步所有 0→9／十段文字。
- [x] Prompt History 只加入真實訊息；隱私掃描不合格時才做不可逆遮罩。
- [x] 執行 Markdown 相對連結與 UTF-8 checkpoint。

### Task 3: 同步 reference implementation

**Files:**
- Create three service snapshots and three test snapshots matching the source paths.
- Update `DistrictTreeViewModels.cs`, `MemberInfoTreeSearchBuilder.cs` and three existing contract test snapshots.
- Create: `reference-implementation/host-integration/06-member-info-commitment-type-metadata-order.patch`
- Modify: `reference-implementation/README.md`
- Modify: `reference-implementation/host-integration/SOURCE-MAP.md`

- [x] 逐檔掃描 11 個 source/test 檔；10 份保持 byte-identical，命中姓名 fixture 的 `MemberInfoTreeSearchBuilderTests.cs` 依政策泛化並記錄來源／交付 SHA-256。
- [x] 以 `git diff 589f0baa 2406b126 -- <13 application/test paths>` 產生 patch 06，命中姓名 fixture 後只做一致的隱私泛化並記錄 raw／delivery SHA-256。
- [x] 驗證 patch 恰好 13 paths，不含 `.ccg`、portable、秘密、絕對路徑或 runtime 值。
- [x] 更新 README／SOURCE-MAP 的 counts、base/end、產生命令、sanitized lineage 與 evidence-only 警告。

### Task 4: 更新 verifier 來源與重建 Manifest

**Files:**
- Modify: `docs/portable/member-info-portable-kit/verify-package.ps1`
- Regenerate: `docs/portable/member-info-portable-kit/manifest.json`

- [x] 將 generator 的 `source.commit` 更新為完整 `2406b126...`，document range start 維持不變。
- [x] 先跑 `.ccg/tasks/build-member-info-portable-kit/test-verify-package.ps1`，確保 verifier 行為未回歸。
- [x] 執行 `verify-package.ps1 -GenerateManifest`，再以唯讀模式驗證 hashes、bytes、UTF-8 與 links。

### Task 5: 重建並驗證 ZIP

**Files:**
- Replace: `docs/portable/member-info-portable-kit.zip`

- [x] 只移除既有 ZIP 檔，不刪除目錄；使用 `System.IO.Compression.ZipFile` 建立單一頂層 `member-info-portable-kit/`。
- [x] 解壓至新的 `%TEMP%` GUID 目錄，拒絕 traversal／reparse point，執行 ZIP 內 verifier。
- [x] 逐檔比對解壓內容與 Manifest，確認新增 service/test/spec/plan/patch 06 均存在。
- [x] 記錄 ZIP bytes、SHA-256、entries 與 files。

### Task 6: 完整驗證與審查

**Files:**
- Create: `.ccg/tasks/refresh-member-info-portable-kit-metadata-order/review.md`
- Modify: `.ccg/tasks/refresh-member-info-portable-kit-metadata-order/task.json`

- [x] 跑完整 `ChurchReport.MemberInfo.Tests`、兩個 affected builds、strict UTF-8、U+FFFD、privacy scan、`git diff --check`。
- [x] Gemini 與 Claude reviewer 並行各呼叫一次；失敗如實記錄且不重試已知配額／wrapper 問題。
- [x] 寫需求對證據表。自動驗證通過後將新 task 設為 `completed`；使用者後續明確要求 Push，因此依 CCG 規則歸檔並 Commit／Push。
