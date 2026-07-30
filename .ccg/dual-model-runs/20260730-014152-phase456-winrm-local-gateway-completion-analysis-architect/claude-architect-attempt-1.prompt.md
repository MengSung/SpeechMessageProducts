ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: phase456-winrm-local-gateway-completion-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Phase 4～6、WinRM 與 Local Gateway 完成度分析

## 角色與限制

請以架構師身分唯讀分析目前工作樹，不得修改檔案，不得輸出任何帳號、密碼、Token、Credential、Secret Reference、Session ID、CRM endpoint 完整值或私密網路資訊。

## 使用者終極目標

- 保留並依新規格繼續 Phase 4、Phase 5、Phase 6。
- Central Gateway 是正式環境目標；Local Gateway 是目前 ChurchReport／Visual Studio 驗證路徑；Embedded 保留但延後。
- 可修改程式、啟動 WinRM、設定 DC 與 D365 虛擬機器、啟動 Local Gateway 與 ChurchReport，並自行用瀏覽器驗證。
- 程式不得有跨 request／session／user／tenant 的 Session Leakage，不得有 Memory／Socket／Timer／Task／Handler／Semaphore／Cache／Connection Pool／Cancellation Registration 等資源洩漏。
- 效能目標是最高安全持續吞吐量；不得以取消隔離、放寬界限或無界平行提高速度。
- 所有新增或實質修改的 Production／Test／Tool／Script 程式必須有完整、深入、詳細繁體中文註解，並使用 UTF-8 without BOM、CRLF、final CRLF。
- `DynamicsAccess:Package01FeeReadsEnabled=false` 必須保持，直到真實 Local Gateway、授權、操作與瀏覽器 E2E Gate 全部完成。
- Embedded、Data8 與 `PowerPlatform.Dataverse.Client` 目前不得移除；只有 Phase 6 Gate 全部通過才能移除。

## 已知本地增量

- ChurchReport 主 DI 擁有唯一 Dynamics ProcessHost 與 bounded Gateway WhoAmI preflight。
- Donation Session resource 使用 opaque scope、request lease、drain、failed-cleanup retry。
- Logout／re-login 在 Session.Clear 前 drain，且已有真實 production path 測試。
- 先前本地證據：ChurchReport 366 tests pass；Dynamics non-live 228 pass、1 live SQL skip；Release build 0 warning／0 error。
- 先前 final CCG 只有 Gemini PASS，Claude 因 session quota 未產生輸出，屬 degraded fallback。

## 請檢查的 authoritative source

- `.trellis/tasks/07-23-dynamics-connection-compatibility/{prd.md,design.md,implement.md}`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-*.md`
- `SpeechMessage.Dynamics.*`
- `SpeechMessageProducts.ChurchReport`
- `ChurchReport.MemberInfo.Tests`
- repository 中的 WinRM／DC／D365 VM 探測、部署與 smoke scripts／documentation
- `git diff` 與目前 configuration；不得只依摘要推論。

## 必須輸出的分析

1. 明確判斷 Phase 4～6 是否應保留，以及新規格如何映射每個 Phase。
2. 列出目前完成、未完成、證據不足或互相矛盾的 Gate，依 Critical／Warning／Info 分級。
3. 提出下一個最小但不縮小終極目標的實作順序；每一步列出精確檔案、測試與 rollback。
4. 找出任何 Session／Memory／Resource Leakage、共享 mutable state、錯誤 owner、unbounded retention、use-after-dispose、deadlock 或效能瓶頸。
5. 評估 WinRM／DC／D365 VM 設定所需的安全前置條件、可自動化命令、不可記錄資訊、fail-closed 驗證與 rollback。
6. 定義 Local Gateway＋ChurchReport＋瀏覽器 E2E 的可執行驗收矩陣，包含啟動、健康、授權、WhoAmI／read-only operation、錯誤路徑、停止與資源 baseline。
7. 指出現在是否可以把 `Package01FeeReadsEnabled` 改成 true；若不可以，列出唯一可接受的解鎖證據。

請以繁體中文輸出，結論要能直接轉成 implementation checklist。任何可信的隔離或資源保留風險都是 release blocker。


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