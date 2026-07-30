# CCG reviewer Task: gateway-http-canonical-final-review-retry

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Gateway HTTP 與 Canonical Queue 最終程式審查

## 審查範圍

請在 repository 內讀取 `git status --short`、tracked diff 與下列 untracked source/test 檔案，執行完整 code review；不要修改程式：

- `SpeechMessage.Dynamics.Gateway/Program.cs`
- `SpeechMessage.Dynamics.Gateway/appsettings.json`
- `SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayRequestBodyLimitOptions.cs`
- `SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs`
- `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`
- `SpeechMessage.Dynamics.Tests/OperationDispatchPreparerTests.cs`
- `SpeechMessage.Dynamics.Tests/OperationDispatchQueueLifecycleTests.cs`
- `SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/PreparedOperationDispatch.cs`
- `Line.Messaging/LineMessagingClient.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`

## 必查契約

1. Authentication 與 principal→workload→alias→operation authorization 必須先於 Content-Type、Content-Length、body read、JSON parsing 與 executor invocation；未授權 request 不得得到 body-contract oracle。
2. Operation endpoint 是 fail-closed JSON-only：只接受大小寫不敏感的 `application/json`，可無參數或只有一個 UTF-8 charset。缺少、無法解析、`application/*+json`、未知／重複參數、非 UTF-8 charset 應在 body I/O 與 buffer rent 前回 415。
3. Kestrel、IIS 與 application reader 必須共用一個 deployment-owned hard request-body ceiling；Content-Length 與 chunked limit+1 都必須受控。
4. Reader 必須嚴格計算 UTF-8 wire bytes，限制 JSON depth，拒絕 duplicate／unknown members，且所有成功、失敗、取消、exception 路徑都完整清零 rented array 後 Return；不得 Dispose ASP.NET Core-owned request stream。
5. Public `ControlledOperationExecutor.ExecuteAsync` 必須在第一個 async suspension 前完成 registry/type validation 與 canonical preparation，queue 不得保留原始 request、caller mutable dictionary、JsonElement、JsonDocument、HttpContext、principal、session、token 或 credential graph。
6. Canonical bytes 必須 deterministic、typed、versioned、Ordinal 排序、UInt32 big-endian UTF-8 length-prefix；admission 使用 exact canonical bytes，不得回到 UTF-16 heuristic。
7. `PreparedOperationDispatch` 必須有單一 owner、並行 idempotent Dispose、zero-before-return；lease cleanup 必須先於 prepared buffer cleanup。取消、admission reject、client throw、timeout 都必須走相同 cleanup。
8. Session／Memory／Resource Leakage 為 zero-tolerance release blocker。檢查 static/singleton/shared mutable state、ArrayPool、CTS、timer、registration、stream、handler、Task、queue retention、exception path 與 GC reachability。
9. 所有新增或實質修改的 Production/Test/Tool/Script 程式必須有完整、深入、詳細的繁體中文 XML 與必要實作註解，說明信任邊界、唯一 owner、並行、失敗、取消、cleanup/disposal、記憶體與效能取捨；`<inheritdoc />` 不足。
10. 變更檔案必須是 UTF-8 without BOM、CRLF、final CRLF；不得包含秘密值。`Package01FeeReadsEnabled=false` 必須維持。

## 已有驗證證據

- Gateway request-body boundary focused：24/24 passed。
- `SpeechMessage.Dynamics.Tests` Release：227 passed、1 live-SQL skipped by contract、0 failed。
- Full solution Release build：0 warnings、0 errors。
- Full solution tests excluding one unrelated pre-existing RichMenus root-detection test：all passed；該既有測試硬編碼尋找不存在的 `ChurchReport.sln`，完整 solution run 唯一因此失敗。
- Strict encoding check：15 scoped files are UTF-8 without BOM, CRLF-only, final CRLF。
- Scoped `dotnet format --verify-no-changes` passed。
- `git diff --check` passed。
- Scoped secret scan passed；no `<inheritdoc />`-only members；`Package01FeeReadsEnabled=false`。

## 輸出格式

請分成 Critical / Warning / Info。每個 finding 必須提供：檔案、精確行或成員、可重現失敗情境、影響、建議修正與應新增的 assertion。若沒有 Critical，請明確寫出；不要把 style preference 當成 correctness finding。特別標示任何 Session／Memory／Resource Leakage 風險。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.