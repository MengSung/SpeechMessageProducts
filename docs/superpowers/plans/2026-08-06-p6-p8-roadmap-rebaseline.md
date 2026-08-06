# P6～P8 Roadmap Rebaseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` for inline execution. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將所有權威規劃文件校正為「Lenovo Legion 完成 P6／P7，P8 再部署單一 ChurchReport cloud Central Gateway」，並消除舊的第二產品觸發與 P7 編號漂移。

**Architecture:** 只修改 Trellis task、roadmap 與連線管理計畫文件，不修改產品程式或 runtime 設定。P6 保持 `in_progress`，P7.0／parent 保持 `planning`，P8 不建立、不啟動；文件以 canonical P7.0～P7.5 與 P8.0～P8.4 表達單向依賴。

**Tech Stack:** Trellis Markdown／JSON task artifacts、PowerShell byte-level text validation、Git read-only diff validation。

---

### Task 1: Rebaseline parent requirements and design

**Files:**
- Modify: `.trellis/tasks/08-05-gateway-purpose-and-positioning/prd.md`
- Modify: `.trellis/tasks/08-05-gateway-purpose-and-positioning/design.md`
- Modify: `.trellis/tasks/08-05-gateway-purpose-and-positioning/implement.md`
- Modify: `.trellis/tasks/08-05-gateway-purpose-and-positioning/task.json`

- [x] **Step 1: Replace the obsolete P8 trigger**

Record that P8 deploys the single ChurchReport product to a cloud Central Gateway after P7.5; move second／third-product onboarding to a future independent task.

- [x] **Step 2: Lock canonical numbering**

Use P7.0 inventory, P7.1 reads, P7.2 writes/actions/functions, P7.3 special resources, P7.4 product cutover, P7.5 ToolUtility removal, followed by P8.0～P8.4.

- [x] **Step 3: Add cloud acceptance boundaries**

Require cloud readiness, service/workload identity, TLS, secret ownership, deploy/restart/drain, monitoring, rollback drill and live evidence without moving those responsibilities into P6／P7.

### Task 2: Rebaseline roadmap and global plan

**Files:**
- Modify: `.trellis/tasks/08-05-gateway-purpose-and-positioning/roadmap-p5-p7.md`
- Modify: `docs/dynamics-connection-management-plan.md`

- [x] **Step 1: Publish the single P5～P8 route**

Show P5 archived → P6 local → P7.0～P7.5 local → independently authorized P8.0～P8.4 cloud, with second／third products outside the completion path.

- [x] **Step 2: Preserve useful prior design decisions**

Keep vertical slices, deterministic coverage, separate coverage states, aggregate-capacity coordination, capability-level rollback, lifecycle baselines and fail-closed connector routing.

- [x] **Step 3: Remove stale execution advice**

Replace old P1/A1/A2 next steps and second-product-only Central wording with the actual P6.2 Lenovo profile-input readiness action.

### Task 3: Align P6 and P7 task artifacts

**Files:**
- Modify: `.trellis/tasks/08-05-official-worker-router-ce-integration/prd.md`
- Modify: `.trellis/tasks/08-05-official-worker-router-ce-integration/design.md`
- Modify: `.trellis/tasks/08-05-official-worker-router-ce-integration/implement.md`
- Modify: `.trellis/tasks/08-05-official-worker-router-ce-integration/task.json`
- Modify: `.trellis/tasks/08-05-gateway-capability-inventory/prd.md`
- Modify: `.trellis/tasks/08-05-gateway-capability-inventory/design.md`
- Modify: `.trellis/tasks/08-05-gateway-capability-inventory/implement.md`
- Modify: `.trellis/tasks/08-05-gateway-capability-inventory/task.json`

- [x] **Step 1: Correct P6 status and host**

Record P6 as `in_progress`, P6.1 as passed, and Lenovo Legion as the local P6/P7 execution host. Preserve the readiness fact that both profiles only report `profile-input-required`.

- [x] **Step 2: Preserve P7 planning state**

Keep P7.0 `planning` until P6 is archived. Add the P7.5 → P8.0 immutable handoff without activating P7 or P8.

- [x] **Step 3: Add integrated-goal authorization semantics**

Allow a later user-provided P6／P7 `/goal` to preauthorize sequential child activation after each gate succeeds; do not let that authorization bypass missing secrets, safe CE fixtures, validation or rollback.

### Task 4: Validate documentation-only scope

**Files:**
- Check: all files listed above
- Preserve unchanged: product `.cs`, `.cshtml`, `.csproj`, runtime configuration and feature flags

- [x] **Step 1: Validate JSON structure**

Run:

```powershell
Get-Content -Raw -Encoding UTF8 .\.trellis\tasks\08-05-gateway-purpose-and-positioning\task.json | ConvertFrom-Json | Out-Null
Get-Content -Raw -Encoding UTF8 .\.trellis\tasks\08-05-official-worker-router-ce-integration\task.json | ConvertFrom-Json | Out-Null
Get-Content -Raw -Encoding UTF8 .\.trellis\tasks\08-05-gateway-capability-inventory\task.json | ConvertFrom-Json | Out-Null
```

Expected: all commands exit 0 without output.

- [x] **Step 2: Scan contradictions**

Run a scoped `rg` scan for second-product-only P8 wording, single-product Central rejection, `P7.6`, `P7.7`, and stale P6 planning-only statements. Historical wording is acceptable only when the same paragraph explicitly marks it superseded.

- [x] **Step 3: Normalize and verify text bytes**

Ensure every modified text file is strict UTF-8 without BOM, CRLF-only and final CRLF.

- [x] **Step 4: Verify the diff scope**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; changed files are planning/task artifacts plus the pre-existing P6 readiness probe files. No product code, product configuration, feature flag or ChurchReport traffic file is added by this rebaseline.

Validation result on 2026-08-06: all three task JSON files parsed; 15 scoped files were strict UTF-8 without BOM, CRLF-only and final CRLF; stale P7.6/P7.7/second-product-only phrases were absent; `git diff --check` was empty; all four focused offline deployment/readiness PowerShell test suites passed.

This plan does not authorize CE calls, cloud deployment, `task.py start`, P7/P8 activation, commit, archive, push or PR creation.
