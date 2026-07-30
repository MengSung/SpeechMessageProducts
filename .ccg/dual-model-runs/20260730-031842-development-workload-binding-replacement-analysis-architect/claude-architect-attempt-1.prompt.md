ROLE_FILE: C:\Users\[LOCAL_PROFILE_REDACTED]\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: development-workload-binding-replacement-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Development WorkloadBindings 繼承授權根因與修正方案分析

## 角色與限制

請以高風險 authentication／authorization 架構師身分唯讀分析目前工作樹，不得修改檔案。不得輸出或轉述任何實際 Windows identity、SID、帳號、Client ID、Callback、Credential、Token、Secret Reference、完整 CRM／AD FS endpoint 或私有網路資訊。

## 已確認問題

- `SpeechMessage.Dynamics.Gateway/appsettings.json` 目前含正式 `DynamicsGateway:WorkloadBindings` index 0。
- `appsettings.Development.json` 目前在同一路徑加入 index 1，只授權 Development 的 `runtime.health.whoami`。
- ASP.NET Core／.NET Configuration 依數字 index 合併，因此 Development 最終 configuration 同時保留 base 與 local binding；Development entry 並未取代 base entry。
- 本機不存在 base binding 的 Windows identity，所以目前 smoke 沒有實際越權，但這是部署到存在該 identity 的環境前必須關閉的 authorization Warning。
- 不能只把 index 1 改成 index 0，因為 base binding 內 nested `CapabilityOperationIds:1..N` 等子索引仍可能殘留。

## 核准架構與不可破壞條件

- Central Gateway 是正式目標；Local Gateway 是 Development 路徑；兩者共用程式，但 authorization binding 必須由各部署環境唯一擁有。
- Development 只能授權一個核准的 local workload、`crm82` 與 `runtime.health.whoami`；不得繼承正式資料 operation。
- Production／Testing 既有 binding 行為不能因修正而靜默改變。
- 未知 binding source、空 replacement set、重複 SID／principal、未知 alias／operation 都必須在 listener 接流量前 fail closed。
- request 熱路徑仍必須使用 immutable frozen lookup，不新增 reload subscription、timer、cache、background task 或 disposable owner。
- `Package01FeeReadsEnabled=false` 保持；Embedded、Data8、`PowerPlatform.Dataverse.Client` 保留。
- 所有新增或實質修改的 Production／Test 程式必須有完整深入的繁體中文註解，說明 trust boundary、owner、競爭、fail-closed、cleanup、效能／記憶體取捨；UTF-8 without BOM、CRLF、final CRLF。

## 請比較的方案

1. 將 base workload binding 移到新的 `appsettings.Production.json`，Development 僅定義自己的 index 0。
2. 新增明確且固定 allowlist 的 binding-set／replacement section，由 authorizer 在建構時選擇單一 section；Development 使用獨立 section，Production／Testing 維持既有 section。
3. 只在 Development JSON 覆寫 index 0／nested arrays／null values。
4. 任何更簡單但能證明不受 Configuration array merge 影響的方案。

## 必須輸出的內容

1. 根因與可重現的最小失敗時序。
2. 推薦方案及未選方案的風險，特別是 nested array merge、部署相容與 authorization fail-closed。
3. 精確修改檔案、API／configuration contract 與完整繁體中文註解要求。
4. TDD RED 測試：必須在目前程式下因 base principal 仍可被 Development authorizer 授權而失敗；修正後還要驗證 local WhoAmI 成功、local data operation 拒絕、base principal 拒絕、Production／Testing 不回歸、未知 source fail closed。
5. focused／full test、Release build、實機 Local Gateway 401／403／controlled 400、stop/listener/resource baseline 與 rollback 指令。
6. 檢查是否會引入 Session／Memory／Socket／Timer／Task／Handler／Configuration reload retention 或效能退化。

請以繁體中文輸出可直接實作的 checklist。任何可利用的 inherited authorization、silent fallback、敏感值輸出或新的長壽命 mutable state 都是 Critical。


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
