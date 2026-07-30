ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: gateway-http-canonical-queue-bounds-analysis-retry

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Gateway HTTP／Queue Bounds 精準架構分析

只閱讀下列範圍，不要展開完整 PRD／design：

- `.ccg/tasks/dynamics-connection-compatibility/research/http-request-body-canonical-queue-retention-2026-07-29.md`
- `SpeechMessage.Dynamics.Gateway/Program.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
- `SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs`
- `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
- `SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionRequest.cs`
- `SpeechMessage.Dynamics.Abstractions/Operations/OperationDefinition.cs`
- `SpeechMessage.Dynamics.Tests/ControlledOperationExecutorTests.cs`
- `SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs`

## 已確認缺陷

1. Gateway 無專案 hard request-body limit；declared/chunked 沒有 focused proof。
2. Executor queue wait 仍保留原始 request／dictionary／JsonElement graph。
3. Envelope 使用 UTF-16 heuristic；大型複合 JsonElement 只估 64 bytes。
4. required/type/value validation 在 admission 之後。

## 請做架構決策

- 推薦「Kestrel＋IIS 共用 hard limit」是否還要搭配 manual bounded JSON reader/custom binder，才能保證限制在反序列化前生效。
- 設計同步 prepare-before-first-await：registry lookup、count/name/required/type/value/idempotency validation、固定排序、version/type tag、UInt32 big-endian UTF-8 length prefix。
- 選擇 canonical buffer owner（ArrayPool／MemoryPool／其他），要求唯一 owner、idempotent dispose、zero-before-return，且避免無界 allocation／double buffering。
- Queue wait 只能保留 prepared bounded DTO/envelope；不可保留 raw request、JsonElement/JsonDocument、HttpContext/principal/session/token/credential/runtime/client。
- 保留 authorization-before-executor、runtime-selection-after-admission、replace-and-drain、lease fencing 與 reverse-order cleanup。
- 定義不易 flaky 的 WeakReference retention test、cancellation/drain counter baseline、Content-Length/chunked/multibyte UTF-8 real Kestrel tests。

## 輸出限制

直接輸出：

1. 推薦設計與反對方案。
2. 精確逐檔修改清單。
3. RED→GREEN 順序。
4. validation/error matrix。
5. leak/lifecycle/performance assertions。
6. Critical／Warning／Info。

所有建議須符合深入繁體中文 XML／實作註解、UTF-8 without BOM＋CRLF。不要執行修改。


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