ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: dynamics-endpoint-disclosure

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics Gateway 成功回應端點洩露：實作前分析

## 角色與範圍

請以架構／安全分析者身分，唯讀分析下列限定範圍：

- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
- `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`

不要修改任何檔案，不要讀取或回顯設定檔、密碼、Token、Credential 或實際 Dynamics 位址。

## 已確認缺口

`DynamicsWebApiClient.SendJsonGetAsync` 的成功結果目前包含 `approvedWebApiRoot`。這個值是 Gateway／WebApi 內部路由與信任邊界資料，產品呼叫端不需要知道；透過 Gateway HTTP 序列化後會洩露 CRM hostname 與 `/api/data/v8.2|v9.1/` 路徑。

## 預期契約

1. `OperationExecutionResult.Data` 成功 payload 只保留產品契約所需欄位，例如 `operationId`、`ceVersion` 與上游 `data`；不得包含 `approvedWebApiRoot`、CRM hostname 或 `/api/data/`。
2. `ApprovedWebApiRoot` 仍只能在 Client／Transport 內部用於安全路由與 nextLink 驗證，不得因修正而弱化 outbound URI allowlist。
3. 先新增直接 Client RED test，再以最小 Production 變更轉綠；如 Gateway HTTP 測試已有足夠可重用 fixture，再補 HTTP serialization regression。
4. 所有新增或實質修改的程式與測試必須有完整、深入、詳細的繁體中文註解，涵蓋信任邊界、失敗行為、資源 owner、取消／釋放，以及效能／記憶體取捨。
5. 所有修改檔案使用 UTF-8 without BOM、CRLF、final CRLF。

## 請輸出

- 是否同意最小修正方向。
- 建議的精確 RED assertions。
- 可能的相容性或序列化風險。
- Critical／Warning／Info 分級發現。


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