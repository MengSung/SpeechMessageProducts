ROLE_FILE: C:\Users\[LOCAL_PROFILE_REDACTED]\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: development-workload-binding-set-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Development Workload Binding Set 最終安全審查

## 審查目標

請審查本次用具名 binding set 關閉 Development 繼承 Central 授權的修正。這是 authentication／authorization configuration boundary，任何跨環境權限洩漏、startup fallback、共享 mutable state、資源 retention 或不確定 cleanup 都是 release blocker。

## 審查範圍

- `SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
- `SpeechMessage.Dynamics.Gateway/appsettings.json`
- `SpeechMessage.Dynamics.Gateway/appsettings.Development.json`
- `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayKestrelNegotiateTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayReadinessTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`

工作樹含大量既有未提交變更。只能評估上述檔案中與 `ActiveWorkloadBindingSet`、`WorkloadBindingSets`、測試 fixture、SPEC 與證據同步直接相關的變更；不得要求還原其他人的既有修改。

## 必查契約

1. Base 與 Development JSON 會同時載入，但 Local authorizer 必須只 materialize `WorkloadBindingSets:Local`，不得讀取或聯集 Central set。
2. Selector 必須是 deployment-owned exact token。空白、wildcard、未知、scalar-only、childless 或歧義 set 必須在 listener、secret resolution、admission、executor 與 outbound transport 前讓 Host startup fail closed；不得 fallback 到 Central、第一組、base provider 或所有 set 聯集。
3. Selector 解析不得容許 configuration-path injection。Authorizer 應列舉 direct child 後 exact match，而不是把 selector 直接串進 path。
4. Request 熱路徑必須維持 frozen dictionary 唯讀查找，不新增 lock、reload subscription、principal cache、timer、background Task、socket、connection 或 disposal owner。
5. Testing factories 必須明確選擇隔離、非空的 `Testing` set，不得默默繼承 Central binding。
6. Regression 必須實際載入 base＋Development JSON，證明 Central principal 在 Local 得到 `unmapped-principal`。無效／空 set startup 測試也必須覆蓋。
7. 新增或實質修改的 Production／Test 程式必須有完整、深入、詳細的繁體中文註解，說明 trust boundary、owner、競爭、fail-closed、cleanup／dispose、效能與記憶體取捨。所有範圍檔案必須維持 UTF-8 without BOM、CRLF、final CRLF。
8. `Package01FeeReadsEnabled=false`、Embedded 延後、Data8 與 `PowerPlatform.Dataverse.Client` 在 Phase 6 Gate 前保留。不得把本次修正誤判為 Phase 4、5 或 6 已完成。

## 已執行證據

```text
RED: 原 authorizer 載入 base + Development 後，Central principal 得到 Succeeded=true。
GREEN targeted:
  GatewayWorkloadBoundaryTests      23 passed
  GatewayRequestBodyBoundaryTests   24 passed
  GatewayKestrelNegotiateTests       7 passed
  GatewayReadinessTests              4 passed
Full regression:
  SpeechMessage.Dynamics.Tests      235 passed / 0 failed / 1 ordinary skip
  ChurchReport.MemberInfo.Tests     367 passed / 0 failed
  SpeechMessageProducts.sln Release   0 warnings / 0 errors
Real Development Local Gateway:
  health 200 / ready 200 / anonymous 401 / catalog 200 /
  wrong alias 403 / unauthorized operation 403 / allowed fail-closed target controlled 400
  listener 7244 and temporary process logs returned to zero after stop
Encoding:
  10 scoped files strict UTF-8 / no BOM / CRLF / final CRLF
  mojibake pattern matches = 0
```

## 保密要求

不得在 stdout、stderr、prompt echo 或審查報告中輸出或保存實際 Windows principal、SID、使用者名稱、主機名稱、credential、token、password、Session marker、secret reference、完整私密 endpoint 或 callback。若需要描述，統一使用 `[WINDOWS_IDENTITY_REDACTED]`、`[SID_REDACTED]`、`[PRIVATE_ENDPOINT_REDACTED]` 等 placeholder。

## 輸出格式

1. 先給 `PASS` 或 `FAIL`。
2. 依 `Critical`、`Warning`、`Info` 分級；每項要有檔案、具體根因、可重現時序與建議修正。
3. 明確回答是否仍存在 Development→Central authorization inheritance、selector fallback/path injection、Testing→Central 繼承、lifecycle/resource leak、註解或 UTF-8 契約缺口。
4. 若沒有 Critical／Warning，明確寫出沒有發現 release blocker；不要為湊數捏造問題。


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
