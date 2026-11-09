## Persistent ASP.NET Core Engineering Requirements

- Work as a senior ASP.NET Core performance optimization expert with more than
  18 years of experience. Base technical decisions on current Microsoft
  official documentation and the latest applicable .NET version, including
  .NET 10.
- New or modified code must prevent Session Leakage, Memory Leakage, and
  other Resource Leakage. Treat any credible or reproducible leakage as a
  release blocker.
- Optimize for maximum safe sustained performance. Performance improvements
  must never weaken isolation, correctness, deterministic resource cleanup,
  verification, maintainability, or security.

## Persistent Traditional Chinese Documentation and UTF-8 Requirements

- At the beginning of every task, read this complete `AGENTS.md` before
  inspecting, generating, or modifying any `.cs` or `.cshtml` file. Treat these
  documentation, encoding, isolation, lifecycle, and performance requirements
  as release-blocking constraints throughout the task.
- Every `.cs` and `.cshtml` file must contain complete, in-depth, maintainable
  Traditional Chinese documentation appropriate to the code it owns. This rule
  applies immediately to every newly created or substantively modified file and
  to every changed region in an existing file.
- Public and internal types, interfaces, constructors, methods, important
  properties, Razor handlers/helpers, and lifecycle-owning test doubles must use
  the language-appropriate documentation form, including C# XML documentation
  comments and Razor comments where applicable. A translated symbol name,
  one-line restatement, or `<inheritdoc />` alone is not sufficient.
- Non-obvious implementation comments must explain why the implementation is
  required and which invariants must not be broken. When applicable, document
  trust boundaries, input validation, the single resource owner, concurrency and
  race behavior, cancellation and timeout behavior, fail-closed behavior,
  rollback/drain/dispose/cleanup order, performance trade-offs, and memory
  retention limits.
- Code involving Session, identity, token, credential, cache, connection pool,
  queue, timer, subscription, stream, handle, cancellation registration, process,
  IPC, or background work must document the maximum data/resource lifetime, the
  deterministic release path, and how cross-request, cross-user, cross-profile,
  and cross-tenant leakage is prevented.
- Test documentation must state the protected contract, the failure or fault
  injection used, and the decisive assertions, so a future maintainer can tell
  which security, isolation, lifecycle, performance, or compatibility guarantee
  failed.
- All `.cs` and `.cshtml` files must be UTF-8 without BOM, use repository-required
  CRLF line endings, and end with a final CRLF. Invalid UTF-8, BOM, mixed/LF-only
  endings, mojibake, or materially misleading/out-of-date comments are
  review/release blockers. Verify encoding and line endings at byte level before
  reporting completion.

## 永久檔案整體說明規範

- 每個自行維護的程式檔都必須在檔案開頭提供完整繁體中文「檔案整體說明」，不能以類別／方法 XML 註解代替。
- 未來新增、搬移或修改檔案時，必須先讀取全部內容，補齊並同步更新檔案說明；不得貼上只換檔名的空泛範本或宣稱尚未實作的安全保證。
- 說明至少包括：檔案目的與所屬模組、負責的主要功能、與呼叫端／依賴的關係、主要輸入與輸出／執行流程、適用範圍與不變條件，以及失敗處理與驗證方式。
- 涉及資料存取、授權、共用狀態、非同步、快取或資源時，另寫明信任邊界、組織／租戶／使用者隔離、資料保留期間、唯一資源擁有者、取消／逾時／併行行為，以及 drain／Dispose／關閉順序。沒有這類行為時，須如實說明本檔純資料或委派角色，不虛構保證。
- 測試檔必須交代保護的產品契約、使用的合成／真實依賴、故障注入、決定性斷言，以及測試 owner 的清理方式；不得把離線測試寫成實機結果。
- C# 使用檔首區塊註解，Razor 使用 `@* *@`，PowerShell 使用 help 區塊或檔首註解，JavaScript 使用區塊註解；保留 shebang、license、`#requires`、Razor 指令等必要語意位置。
- 產生檔的說明必須由產生器輸出並通過 deterministic check，不能手改 `.g.cs`；第三方原始碼／套件、minified vendor 檔及原始執行證據保留原貌，以維護清冊註明所有權，不冒充本專案自行維護的程式。
- JSON 等不允許註解的格式不得插入非法註解；用途與欄位契約寫在同目錄的繁中說明或明確 schema／spec。
- 檔案說明本身也必須 UTF-8 無 BOM、CRLF、末尾 CRLF；補說明時保留程式 token／字串／行為，執行適用的語法、建置、測試與位元組檢查後才提交。
- 每次交付執行 `python tools/RepoQuality/check_file_documentation.py`，CI 同步執行；新增產品目錄預設納管，不得為通過而擴張排除範圍。JSON 等格式的逐檔伴隨說明集中於 `docs/file-purpose-companions.json`，新增或搬移須同步維護。
- 完整執行契約見 `.trellis/spec/backend/file-documentation-contract.md`。結構／字數檢查不代表語意完整；審查仍須確認說明符合實際用途、失敗處理、隔離及資源所有權。

## Permanent Cross-User Isolation and Sustainable Performance Rule

This rule applies to every current and future product line, deployment mode,
service, UI, background worker, test harness, and development tool in this
repository. It is not limited to Dynamics or ChurchReport.

- A request, session, or login for subject A must never reveal, reuse, mutate,
  cache, log, or otherwise retain data, identity, authorization, credentials,
  connection state, token, cookie, response, error detail, or mutable state
  belonging to subject B. Cross-user, cross-tenant, cross-profile, and
  cross-product leakage is a zero-tolerance release blocker.
- Authentication and authorization scope must be derived and validated on the
  server before data access. Caller-provided routing, tenant, identity,
  credential, or profile values are never authority.
- Any cache, pool, queue, singleton, static, background task, retry state, or
  diagnostic buffer must either contain no user-/tenant-/profile-specific data
  or be explicitly partitioned by the complete validated isolation boundary,
  bounded in size and lifetime, and deterministically cleared or disposed.
- Reusable connections and clients may be pooled only when they carry no
  request/user session state and their ownership, fault eviction, drain, and
  disposal paths are proven. A timeout, cancellation, or uncertain transport
  state must not be reused by another request.
- Isolation always wins over throughput. Small, predictable validation,
  partitioning, and cleanup costs are acceptable. Do not introduce a large
  performance regression, such as an unbounded scan, global serialization, or
  a fresh expensive runtime per normal request, merely as a substitute for a
  correct isolation design.
- Before release, focused concurrent A/B isolation tests and lifecycle/soak
  tests must prove that responses, caches, leases, permits, temporary data, and
  resource counters return to their declared safe baseline. See
  `.trellis/spec/backend/cross-user-isolation-and-performance.md` for the
  executable contract and `.trellis/spec/guides/cross-user-isolation-and-performance-review.md`
  for the mandatory review checklist.

## Permanent Duplicate-Row Prevention and Publication Rule

This rule applies permanently to every current and future product line added to
this Solution, including ChurchReport, procurement-association products,
construction-company products, and any later website, API, worker, integration,
test harness, or development tool. It covers every server-rendered table, JSON
collection, API result, grid, tree, report, export, cache projection, background
result, and future UI data source in this repository. Product-specific code,
deployment mode, tenant count, or UI framework never exempts a component from
these requirements.

- Every repeatable row must have a stable, server-owned identity derived from
  the authoritative record or business event. Display text such as name,
  telephone, label, list position, or a client-supplied index is not identity.
- Legitimate records that share the same display name must remain separate when
  their stable identities differ. Never use `Distinct`, `GroupBy`, dictionary
  overwrite, or client-side filtering by display name to hide duplicates.
- Before publication, validate stable identities within the exact collection
  consumed by each UI component. A repeated non-empty identity is a data or
  assembly conflict and must fail closed with a diagnosable error; it must not
  be silently dropped, merged, or handed to a UI library with duplicate keys.
- Build mutable results in an operation-local candidate. Publish only after all
  I/O, mapping, authorization, scope checks, and identity validation succeed.
  Readers may observe the previous complete snapshot or the next complete
  snapshot, never an object being populated in place.
- Shared snapshots must be partitioned by the complete validated isolation
  boundary, including the applicable user/contact, tenant/organization,
  authorization role, list/report/business scope, date/version/generation, and
  authentication epoch. A cache hit never replaces server-side authorization.
- Do not expose mutable Session/cache collections directly to serializers,
  grid loaders, background jobs, or callbacks. Return detached immutable values
  or deep-enough request-owned copies, with bounded lifetime and no references
  to Session, HttpContext, credentials, CRM clients, connections, or disposables.
- All writers for the same business event must use one server-generated
  idempotency/alternate key and atomic create-or-update semantics. Application
  locks are not a substitute for database/Dataverse uniqueness across
  processes. Existing conflicts must stop further creation and enter an
  auditable remediation path.
- Every affected feature must add tests for: legitimate same-name records,
  exact duplicate stable identities, concurrent same-scope publication,
  scope/date/user changes, loader failure and retry, caller mutation isolation,
  and resource drain. A known duplicate-key, cross-session, partial-publication,
  or retained-resource failure is a release blocker.
- Duplicate or missing stable row identities, cross-session/cross-user data,
  unbounded retained memory, and unreleased tasks, timers, subscriptions,
  cancellation registrations, streams, handles, connections, leases, or other
  resources are release-blocking defects in every product. They must be fixed
  and verified before delivery; they cannot be deferred as cosmetic UI cleanup
  or accepted in exchange for throughput.

The executable implementation and review contract is
`.trellis/spec/backend/duplicate-row-publication-contract.md`.

供應商端的具體實作手冊為
`docs/跨產品資料不重複與網路時序防護實作手冊.md`，適用於本 Solution 的所有現有與
未來產品。正式資料列必須依據資料庫的唯一 ID 區分，不得按姓名、電話或其他內容相同
就刪除、合併、覆蓋或隱藏資料。不同 ID 的資料即使所有欄位完全相同，也必須完整保留；
同一消費集合中的重複 ID 必須拒絕發布並診斷，不得靜默刪除其中一筆。
操作重試鍵、請求 token、暫存編輯鍵不能取代資料庫 ID；臨時 draft key 不得被拿來
讓欠缺資料庫 ID 的正式記錄通過發布驗證。

<!-- TRELLIS:START -->
# Trellis Instructions

These instructions are for AI assistants working in this project.

This project is managed by Trellis. The working knowledge you need lives under `.trellis/`:

- `.trellis/workflow.md` — development phases, when to create tasks, skill routing
- `.trellis/spec/` — package- and layer-scoped coding guidelines (read before writing code in a given layer)
- `.trellis/workspace/` — per-developer journals and session traces
- `.trellis/tasks/` — active and archived tasks (PRDs, research, jsonl context)

If a Trellis command is available on your platform (e.g. `/trellis:finish-work`, `/trellis:continue`), prefer it over manual steps. Not every platform exposes every command.

If you're using Codex or another agent-capable tool, additional project-scoped helpers may live in:
- `.agents/skills/` — reusable Trellis skills
- `.codex/agents/` — optional custom subagents

Managed by Trellis. Edits outside this block are preserved; edits inside may be overwritten by a future `trellis update`.

<!-- TRELLIS:END -->

<!-- CCG-SELF-HEALING:START -->
# CCG Gemini + Claude Self-Healing Rule

When a task requires external CCG analysis or review, do not call Gemini, Claude,
or `codeagent-wrapper` directly. Always start from the project auto-recovery
entrypoint below. It creates the UTF-8 task prompt, delegates to the self-healing
runner, records artifacts, retries repairable failures, and returns a structured
summary.

If a direct Gemini / Claude / `codeagent-wrapper` call was attempted and failed,
immediately stop manual debugging and re-run the same analysis or review through
the auto-recovery entrypoint below.

Use the project runner:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role reviewer `
  -Title "<short-task-name>" `
  -PromptFile ".\.ccg\dual-model-runs\<task>-review-input.md" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

`Start-CcgDualModelRun.ps1` calls `Invoke-CcgDualModelWithSelfHealing.ps1`.
The delegated runner performs the health check, repairs local PATH/env issues,
retries repairable failures, records all prompts/stdout/stderr/summary files,
and distinguishes local failures from provider quota or session-limit blockers.
The project owner has approved using `-AllowSingleModelWhenQuotaBlocked` for
provider quota/session fallback when at least one backend completed with usable
output. Treat this as a degraded result, not as full dual-model success. Never
report a quota-blocked run as a successful dual-model review, and never ignore a
Critical finding from the backend that did complete.

Required recovery behavior:

1. Put the analysis/review request into UTF-8 text, preferably a prompt file
   under `.ccg/dual-model-runs/`.
2. Invoke `Start-CcgDualModelRun.ps1` with the correct `-Role`.
3. If the runner exits with `ok=true`, all requested backends completed. Only
   `dualModelComplete=true` confirms both Gemini and Claude completed; never infer
   this from `ok` or exit code 0. For `reviewMode=claude-only`, report
   「單模型（使用者指定 ClaudeOnly）」 and use only the Claude output.
4. If the runner exits with code `2`, inspect the generated run folder, fix the
   local toolchain issue, then run the same entrypoint again instead of switching
   to ad-hoc Gemini/Claude commands.
5. If the runner exits with `quotaBlocked=true`, treat it as provider
   quota/session state, not a local repair failure. Continue only when
   `degradedFallback=true` and at least one backend completed with usable output;
   report that state as single-model fallback, not completed dual-model review.
   If no backend completed, use local verification only and retry external review
   later.
<!-- CCG-SELF-HEALING:END -->

## 永久例外 LINE 通知規則

所有未來新增或修改的產品、API、MVC、背景工作、queue callback 與共用服務，都必須把真正失敗、未處理或影響功能的 exception 交給共用告警入口，由部署設定決定輸出目的地。正常取消、client disconnect、成功恢復的 retry 與預期業務拒絕不通知。

- 例外通知必須集中在可注入的共用 notifier；不得在各處自行 new LINE client、保存 `HttpContext`／Session／credential，或建立無界背景佇列。
- 通知內容只可包含產品／模組、例外型別、穩定 incident id、UTC 時間與 operation／route；不得傳送 token、cookie、密碼、完整 request body、付款資料或未驗證個資。
- HTTP 未處理例外要在標準錯誤處理器內側攔截後重新拋出；catch 後仍回傳失敗的流程也要明確呼叫 notifier。單靠全域 middleware 不代表所有例外都已覆蓋。
- LINE 發送失敗不得覆蓋原始例外、不得遞迴通知；所有 queue、task、timer、stream 與 cancellation registration 必須有上限、逾時、drain 與 Dispose。詳細契約與測試要求見 `.trellis/spec/backend/error-handling.md` 的「LINE 管理者例外告警契約」。
- 啟用 `ExceptionNotifications:WriteExceptionLog` 時，Debug 與 Release 都必須把 `ILogger` 的 Error/Critical（含 Exception）寫入應用程式 `Logs\Exception.log`；檔案 writer 由 Host/DI 擁有並在停止時 Dispose。禁止以 request、Session、使用者輸入或機密值組成檔名或路徑。
- **兩個輸出皆啟用時固定順序：先將事件寫入 `Exception.log` 並成功 flush，再排入／發送 LINE。** LINE 與日誌共用事件 ID。落檔失敗不得先送 LINE 或假稱已記錄，必須走獨立 stderr／主機監控；LINE 失敗只追加本地通知狀態，不遞迴通知。
- catch 必須傳遞真實 `Exception` 至共用入口，保留型別、程式位置與同一事件的去重依據；禁止只傳 `ToString()` 後丟棄例外。內部逾時 token 已取消仍屬失敗；正常取消只能由呼叫端明確提供的 token 判定。
- 檔案輪替受外部鎖定阻擋時，只可有界降級寫入 `Exception.log`（最多正常容量兩倍），仍須成功 flush 才通知；達硬上限或無法寫入時走 stderr／主機監控，解除鎖定後恢復輪替。不得截斷既有證據或無界增加檔案。

### 例外輸出開關（使用者明確授權的獨立組合）

`SpeechMessageProducts.ChurchReport/appsettings.json` 的 `ExceptionNotifications:WriteExceptionLog` 與 `ExceptionNotifications:SendLine` 各自獨立，缺省皆為 `true`；Debug／Release 都讀取啟動快照，修改後須重啟。

- true／true：先寫入並 flush `Exception.log` 成功，再排入 LINE；寫檔失敗不可發 LINE。
- true／false：只寫 `Exception.log`，不建立 LINE client／consumer。
- false／true：只發 LINE，不寫任何例外檔案；通知失敗／滿載／關機狀態只用固定 stderr。
- false／false：不寫 `Exception.log`、不發 LINE。

此四種組合優先於上方原有雙開預設敘述；正常取消分類、資料隔離與有界生命週期規則不變。

## 永久新增專案即時納入方案規則

- 使用者於 2026-11-07 明確要求：未來新增 `.csproj` 時，必須在同一個工作步驟立即加入本工作樹的 `SpeechMessageProducts.sln`，不得只建立磁碟目錄或等功能、測試完成後才加入。
- 此規則適用產品、共用程式庫、測試及工具專案；不得自行以骨架、試做或尚未整合為由留下未登錄的新專案。若另有明確核定的特殊方案邊界，須先說明衝突，不得靜默例外。
- 加入後以 `dotnet sln .\SpeechMessageProducts.sln list` 確認專案恰好出現一次，並核對 Debug／Release 的 ActiveCfg、Build.0 對應及原有專案未遭移除。
- 方案可見不代表功能或驗收完成；仍須在進度頁分開記錄建置、測試、整合及必要實機驗收狀態。
- 具體命令、失敗處理與驗證契約見 `.trellis/spec/backend/directory-structure.md` 的「新增專案與方案同步契約」。

## 永久進度一般資訊逐筆日期時間規則

- 使用者於 2026-11-07 明確要求：`實作進度.html` 的 P2 一般資訊及往後新增或更新的各階段一般資訊，摘要本文與每筆詳細／歷史紀錄都必須有可見的日期、時間及時區，不可只寫在 `lastUpdated` 或其他紀錄中。
- 每筆開頭使用 `YYYY-MM-DD HH:mm Asia/Taipei (UTC+08:00)`；摘要 `lastUpdated` 同步使用帶 `+08:00` 的 ISO 8601。事件時間與更新時間應如實區分。
- 舊紀錄已有確切時間就保留；原事件時間未記錄或無法確認時，明標 `補記 YYYY-MM-DD HH:mm Asia/Taipei (UTC+08:00)（原事件時間未記錄）`，不得猜測或用補記時間冒充歷史完成時間。
- 同一階段仍只維護一張一般資訊摘要，原地更新並保留有效歷史與證據；不可因加入日期而新增同階段卡片或刪除舊紀錄。
- 更新後執行 `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs` 及適用的文件／位元組檢查；具體契約見 `.trellis/spec/frontend/progress-page-contract.md`。

## 現行實作計畫唯一依據（2026-11-09）

實作計畫只依 `文件\規劃設計書\實作計畫-3.5\`；`_封存_請勿依據\` 只供查閱，不得作為依據或修改。
進度只更新現行 3.5 頁的 DATA，不改版面程式；新增及更新紀錄均保留台北日期時間。
依使用者核定：工作包驗收後才推送目前階段分支、不推 main、不開 PR；同時最多兩包，依使用者 2026-11-09 授權，完成階段 3（P3-01～P3-09 與階段審查）後停下回報，不啟動階段 4。
