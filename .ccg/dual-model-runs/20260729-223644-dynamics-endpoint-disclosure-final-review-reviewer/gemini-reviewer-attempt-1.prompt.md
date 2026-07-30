ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-endpoint-disclosure-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics Gateway 成功回應端點洩露：程式碼審查

## 審查範圍

請審查目前工作樹中下列限定差異：

- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
- `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`

請以 `git diff -- <files>` 取得實際差異。不要修改任何檔案，不要讀取或回顯設定檔、密碼、Token、Credential 或實際 Dynamics 位址。

## 必須驗證的契約

1. 成功 `OperationExecutionResult.Data` 不再主動加入 `approvedWebApiRoot`、CRM hostname 或 `/api/data/` 內部路由。
2. Outbound URI 的 HTTPS／origin／port／base-path allowlist 不可被弱化。
3. 取消、逾時、重試、HttpRequestMessage／HttpResponseMessage／Stream／ArrayPool buffer 的 owner 與釋放順序不可被改變。
4. 測試必須是有效的 RED→GREEN regression，而不是只測 mock 行為；同時保留 `operationId`、`ceVersion` 與 `data` 正向契約。
5. 新增或修改的程式與測試註解必須是完整、深入的繁體中文，且內容需準確涵蓋信任邊界、資源 owner、取消／釋放及效能／記憶體取捨。
6. 檔案必須為 UTF-8 without BOM、CRLF、final CRLF。

## 已執行證據

- 新測試在 Production 修正前因 `approvedWebApiRoot` 存在而 RED。
- 最小修正後單一測試 GREEN。
- `DynamicsWebApiClientTests` 全組 17 passed、0 failed、0 skipped。

## 請輸出

- Critical／Warning／Info 分級審查報告。
- 每一項發現需指出實際程式證據與是否必須在本切片修正。
- 若沒有 Critical／Warning，明確輸出 PASS。


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