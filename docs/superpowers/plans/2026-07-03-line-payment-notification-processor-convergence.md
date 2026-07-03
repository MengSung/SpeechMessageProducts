# 切片 0：付款通知 LINE 路徑收斂 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `PaymentNotificationService` 的 LINE 推播收斂成單一 processor 路徑，並補齊 LineMessagingProcessor 生產建構子的測試缺口（C1）與中性化註解（W1）。

**Architecture:** 依 `docs/superpowers/specs/2026-07-03-line-shared-extraction-design.md` 切片 0。PaymentNotificationService 目前有兩條 push 路徑（無 retryKey 走 `LineMessagingClient`+`PushUtility` 吞錯路徑；有 retryKey 走 processor）；收斂為一律走 `LineMessagingProcessorClass.SendReliableMessageAsync(lineId, message, retryKey)`（SDK 對 null retryKey 不送 X-Line-Retry-Key header，等同非重試推播）。同時給 processor 加 `（token, HttpClient）`生產建構子，讓測試能攔截 HTTP 並驗證 token 路徑。

**Tech Stack:** .NET（net10.0）、xUnit + FluentAssertions、Line.Messaging SDK（本 repo 內專案）。

## Global Constraints

- 共用層（`Line.Messaging/`、`LineMessagingProcessor/`）禁止出現 ChurchReport、CRM（`IOrganizationService`/`Entity`）、DbContext 相依。
- 共用層失敗一律拋例外（不吞錯）；吞錯是產品層決策。
- 不動 `LinePayCSharp/`、LIFF/`.cshtml`/JS、矩陣 P2 官方 API。
- `LineUtilityClass` 本切片完全不碰。
- 檔案 UTF-8 無 BOM + CRLF；`bin/`、`obj/`、`artifacts/` 不進 commit。
- 工作目錄：worktree `.worktrees/Jesus_5.1.6.WorktreeRefactorLine`（分支 `Jesus_5.1.6.WorktreeRefactorLine`）。
- 測試指令若因未 restore 失敗，先跑一次 `dotnet restore ChurchReport.sln` 再重試。

---

### Task 1: processor 生產建構子（token + HttpClient）與 C1 測試

**Files:**
- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`（在第 46 行 DI 建構子之後加新建構子；第 1-10 行 using 區加 `using System.Net.Http;`）
- Create: `LineMessagingProcessor.Tests/LineMessagingProcessorProductionConstructorTests.cs`

**Interfaces:**
- Consumes: 既有 `LineMessagingClient(HttpClient httpClient, string channelAccessToken, string uri = DEFAULT_URI)`（`Line.Messaging/LineMessagingClient.cs:108`，非 obsolete）、既有私有 `NormalizeBearerToken` / `StripBearerPrefix` / `GetRequiredChannelAccessToken`。
- Produces: `public LineMessagingProcessorClass(string channelAccessToken, HttpClient httpClient)` — `_requiresChannelAccessToken = true` 的可測生產路徑。Task 5 的審查會引用本 task 的測試證據。

- [ ] **Step 1: 寫失敗測試**

建立 `LineMessagingProcessor.Tests/LineMessagingProcessorProductionConstructorTests.cs`：

```csharp
using System.Net;
using System.Text;
using FluentAssertions;
using Xunit;

namespace LineMessagingProcessor.Tests;

public sealed class LineMessagingProcessorProductionConstructorTests
{
    [Fact]
    public async Task SendMessage_with_blank_token_throws_before_any_http_call()
    {
        var handler = new CapturingHttpMessageHandler();
        var processor = new LineMessagingProcessorClass(string.Empty, new HttpClient(handler));

        Func<Task> action = () => processor.SendMessage("U1234567890abcdef", "hello");

        await action.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessage_with_valid_token_sends_bearer_authorization_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var processor = new LineMessagingProcessorClass("test-token", new HttpClient(handler));

        await processor.SendMessage("U1234567890abcdef", "hello");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Authorization.Should().NotBeNull();
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("test-token");
    }

    [Fact]
    public async Task SendMessage_with_bearer_prefixed_token_does_not_double_the_prefix()
    {
        var handler = new CapturingHttpMessageHandler();
        var processor = new LineMessagingProcessorClass("Bearer test-token", new HttpClient(handler));

        await processor.SendMessage("U1234567890abcdef", "hello");

        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("test-token");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~LineMessagingProcessorProductionConstructorTests"`
Expected: 編譯錯誤 CS1729（`LineMessagingProcessorClass` 沒有吃 2 個引數的建構子）— 編譯失敗即為本步的「失敗」證據。

- [ ] **Step 3: 最小實作**

`LineMessagingProcessor/LineMessagingProcessorClass.cs`：

在檔頭 using 區（`using System.Linq;` 之後）加：

```csharp
using System.Net.Http;
```

在 DI 建構子（`public LineMessagingProcessorClass(LineMessagingClient lineMessagingClient)`，約第 41-46 行）之後加：

```csharp
/// <summary>
/// 以呼叫端管理的 HttpClient 建立生產用 processor。
/// 與純 token 建構子同樣強制 token 檢查（_requiresChannelAccessToken = true），
/// 但改用非過時的 SDK 建構子，避免 HttpClient socket 耗盡，也讓測試能攔截請求。
/// </summary>
public LineMessagingProcessorClass(string channelAccessToken, HttpClient httpClient)
{
    if (httpClient == null)
    {
        throw new ArgumentNullException(nameof(httpClient));
    }

    _channelAccessToken = NormalizeBearerToken(channelAccessToken);
    _requiresChannelAccessToken = true;
    _lineMessagingClient = new LineMessagingClient(httpClient, StripBearerPrefix(_channelAccessToken));
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~LineMessagingProcessorProductionConstructorTests"`
Expected: PASS（3 tests）

- [ ] **Step 5: 跑整個 processor 測試專案確認沒破壞**

Run: `dotnet test LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj --no-restore -v minimal`
Expected: 全數 PASS（既有 13 + 新 3 = 16 tests）

- [ ] **Step 6: Commit**

```bash
git add LineMessagingProcessor/LineMessagingProcessorClass.cs LineMessagingProcessor.Tests/LineMessagingProcessorProductionConstructorTests.cs
git commit -m "test: cover LINE processor production token constructor"
```

---

### Task 2: 非 2xx 失敗行為特徵測試

**Files:**
- Create: `LineMessagingProcessor.Tests/LineMessagingProcessorFailureBehaviorTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `LineMessagingProcessorClass(string, HttpClient)`；SDK 的 `LineResponseException`（`Line.Messaging/HttpResponseMessageExtensions.cs:32` 於非 2xx 時擲出，含 `StatusCode` 屬性）。
- Produces: 明文固定「共用層失敗拋例外」的行為契約，Task 4 的服務層改動依賴此語意。

- [ ] **Step 1: 寫特徵測試（characterization test，預期直接通過）**

建立 `LineMessagingProcessor.Tests/LineMessagingProcessorFailureBehaviorTests.cs`：

```csharp
using System.Net;
using System.Text;
using FluentAssertions;
using Line.Messaging;
using Xunit;

namespace LineMessagingProcessor.Tests;

public sealed class LineMessagingProcessorFailureBehaviorTests
{
    [Fact]
    public async Task SendMessage_throws_LineResponseException_on_non_success_status()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "{\"message\":\"Internal error\"}");
        var processor = new LineMessagingProcessorClass("test-token", new HttpClient(handler));

        Func<Task> action = () => processor.SendMessage("U1234567890abcdef", "hello");

        var exception = await action.Should().ThrowAsync<LineResponseException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task SendReliableMessageAsync_throws_LineResponseException_on_non_success_status()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest, "{\"message\":\"Invalid user id\"}");
        var processor = new LineMessagingProcessorClass("test-token", new HttpClient(handler));

        Func<Task> action = () => processor.SendReliableMessageAsync("U1234567890abcdef", "hello", "retry-key-1");

        var exception = await action.Should().ThrowAsync<LineResponseException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHttpMessageHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
```

- [ ] **Step 2: 跑測試**

Run: `dotnet test LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~LineMessagingProcessorFailureBehaviorTests"`
Expected: PASS（2 tests）— 這是特徵測試，鎖定既有 SDK 行為；若 FAIL，停下來回報，不要改 SDK 遷就測試。

- [ ] **Step 3: Commit**

```bash
git add LineMessagingProcessor.Tests/LineMessagingProcessorFailureBehaviorTests.cs
git commit -m "test: pin LINE processor non-2xx failure behavior"
```

---

### Task 3: W1 — 共用層註解中性化

**Files:**
- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs:254-256`（`SendMessage` 內的註解）

**Interfaces:**
- Consumes: 無（純註解）。
- Produces: 共用層文字不再點名特定消費端專案。

- [ ] **Step 1: 改寫註解**

把（Task 1 完成後行號會往後平移，請以文字定位）：

```csharp
// 舊版 ChurchReport 流程曾用這個特殊字串要求系統回傳 LINE 使用者 ID。
// 這不是 LINE 官方 Messaging API 的協定；此處只保留既有文字轉換，
// 實際 HTTP endpoint、Authorization header 與 JSON 序列化全部交給 Line.Messaging SDK。
```

改為：

```csharp
// 既有呼叫端流程曾用這個特殊字串要求系統回傳 LINE 使用者 ID（非 LINE 官方協定，
// 屬既有相容行為，所有消費端一體適用）；此處只保留既有文字轉換，
// 實際 HTTP endpoint、Authorization header 與 JSON 序列化全部交給 Line.Messaging SDK。
```

- [ ] **Step 2: 驗證共用層不再點名產品**

Run: `grep -rn "ChurchReport" LineMessagingProcessor/*.cs`
Expected: 無任何輸出。
（註：`ProcessMessage` 內的「好牧人」歡迎文案是既有產品字串，屬路線圖切片 5 的瘦身範圍，本刀不動。）

- [ ] **Step 3: 跑 processor 測試確認沒破壞**

Run: `dotnet test LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj --no-restore -v minimal`
Expected: 全數 PASS

- [ ] **Step 4: Commit**

```bash
git add LineMessagingProcessor/LineMessagingProcessorClass.cs
git commit -m "docs: neutralize consumer naming in LINE processor comment"
```

---

### Task 4: PaymentNotificationService 收斂為單一 processor 路徑

**Files:**
- Modify: `ChurchReport/Services/PaymentNotificationService.cs`（第 5 行 using、第 82-111 行 `SendLineMessage`）

**Interfaces:**
- Consumes: `LineMessagingProcessorClass(string channelAccessToken)`（既有生產建構子）、`SendReliableMessageAsync(string UserId, string Message, string? retryKey)`（retryKey 為 null/空白時 SDK 不送 X-Line-Retry-Key header，已有測試：`LineMessagingProcessorReliableNotificationTests` 第 44 行）。
- Produces: `SendLineMessage(string lineId, string message, string? retryKey)` 簽章不變，內部單一路徑。

**行為變更（刻意為之，要寫進 commit message）：** 舊的無 retryKey 路徑經 `PushUtility.SendMessage` 內部吞錯（`ChurchReport/Tools/PushUtility.cs:48-53`，失敗也會 log「已發送」）；改走 processor 後失敗會拋 `LineResponseException` → 被本方法 catch → log「發送失敗」→ rethrow → 由 `ChurchReportPaymentPostPaymentHandlers.NotifyAsync` 的 catch 記錄（`ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:108-114`），付款流程不中斷。淨效果：失敗不再被誤記為成功。

- [ ] **Step 1: 改寫 SendLineMessage 三參數版**

把第 82-111 行：

```csharp
        /// <summary>
        /// 透過 LINE Messaging API 推播付款通知。
        /// retryKey 有值時走 LineMessagingProcessor 的可重試入口；沒有 retryKey 時保留舊的 PushUtility 路徑，
        /// 讓既有非付款或無穩定識別碼的通知不被本次重構影響。
        /// </summary>
        public void SendLineMessage(string lineId, string message, string? retryKey)
        {
            try
            {
                var channelAccessToken = GetLineChannelAccessToken();
                if (string.IsNullOrWhiteSpace(retryKey))
                {
                    var lineMessagingClient = new LineMessagingClient(channelAccessToken);
                    var pushUtility = new PushUtility(lineMessagingClient);
                    pushUtility.SendMessage(lineId, message).Wait();
                }
                else
                {
                    var processor = new LineMessagingProcessorClass(channelAccessToken);
                    processor.SendReliableMessageAsync(lineId, message, retryKey).Wait();
                }

                _logger.LogInformation($"SendLineMessage: 已發送 - LineId: {lineId}, RetryKey: {retryKey ?? "<none>"}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendLineMessage: 發送失敗 - LineId: {lineId}, RetryKey: {retryKey ?? "<none>"}");
                throw;
            }
        }
```

改為：

```csharp
        /// <summary>
        /// 透過 LINE Messaging API 推播付款通知。
        /// 一律走共用 LineMessagingProcessor：retryKey 有值時 SDK 會帶 X-Line-Retry-Key header，
        /// 空白時 SDK 不送該 header（等同一般推播）。失敗一律拋例外（不再沿用舊 PushUtility 吞錯行為），
        /// 由呼叫端（payment post-payment handler）決定記錄與補償策略。
        /// </summary>
        public void SendLineMessage(string lineId, string message, string? retryKey)
        {
            try
            {
                var channelAccessToken = GetLineChannelAccessToken();
                using var processor = new LineMessagingProcessorClass(channelAccessToken);
                processor.SendReliableMessageAsync(lineId, message, retryKey).Wait();

                _logger.LogInformation($"SendLineMessage: 已發送 - LineId: {lineId}, RetryKey: {retryKey ?? "<none>"}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendLineMessage: 發送失敗 - LineId: {lineId}, RetryKey: {retryKey ?? "<none>"}");
                throw;
            }
        }
```

- [ ] **Step 2: 移除不再使用的 using**

先確認 `Line.Messaging` 型別在檔內已無其他使用者：

Run: `grep -n "LineMessagingClient\|TextMessage\|ISendMessage\|PushUtility" ChurchReport/Services/PaymentNotificationService.cs`
Expected: 無任何輸出（Step 1 已移除唯一使用點）。

然後刪除第 5 行 `using Line.Messaging;`。`using ChurchReport.Tools;`（第 4 行）保留 — `PaymentMessageBuilder` 等型別仍可能依賴它，不要動。

- [ ] **Step 3: 建置整個 solution**

Run: `dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false`
Expected: Build succeeded，0 Error。若因刪 using 造成 CS0246，把 `using Line.Messaging;` 加回並回報。

- [ ] **Step 4: 跑全部測試專案**

Run:
```bash
dotnet test LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj --no-restore -v minimal
dotnet test Line.Messaging.Tests/Line.Messaging.Tests.csproj --no-restore -v minimal
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal
```
Expected: 全數 PASS。

- [ ] **Step 5: Commit**

```bash
git add ChurchReport/Services/PaymentNotificationService.cs
git commit -m "feat: route payment LINE notifications through single processor path

Non-retry sends previously went through PushUtility, which swallows
failures and logs success even when LINE rejects the push. Both paths
now use LineMessagingProcessorClass.SendReliableMessageAsync; failures
throw, are logged as failures here, and are contained by the payment
post-payment handler's catch. Intentional behavior change."
```

---

### Task 5: 收尾驗證、雙模型審查與 CCG 記錄

**Files:**
- Create: `.ccg/tasks/line-payment-notification-processor-convergence/task.json`
- Create: `.ccg/tasks/line-payment-notification-processor-convergence/review.md`

**Interfaces:**
- Consumes: Task 1-4 的全部 commit。
- Produces: 切片 0 完成證據；後續切片 1（基礎發送族）以此為起點。

- [ ] **Step 1: 邊界掃描**

Run: `grep -rn "ChurchReport\|IOrganizationService\|Microsoft.Xrm\|DbContext" LineMessagingProcessor/*.cs Line.Messaging/*.cs`
Expected: 無任何輸出。

- [ ] **Step 2: 編碼與行尾檢查**

Run: `file LineMessagingProcessor/LineMessagingProcessorClass.cs ChurchReport/Services/PaymentNotificationService.cs LineMessagingProcessor.Tests/LineMessagingProcessorProductionConstructorTests.cs LineMessagingProcessor.Tests/LineMessagingProcessorFailureBehaviorTests.cs`
Expected: 每個檔案為 UTF-8 text（無 BOM），CRLF line terminators。

- [ ] **Step 3: 建立 CCG 任務記錄**

建立 `.ccg/tasks/line-payment-notification-processor-convergence/task.json`：

```json
{
    "id": "line-payment-notification-processor-convergence",
    "title": "Route payment LINE notifications through single processor path",
    "status": "in_progress",
    "complexity": "S",
    "risk": "low",
    "domain": "backend",
    "currentPhase": "review",
    "nextAction": "Record dual-model review verdicts",
    "createdAt": "2026-07-03T00:00:00+08:00",
    "branch": "Jesus_5.1.6.WorktreeRefactorLine"
}
```

- [ ] **Step 4: 雙模型審查（真實 diff，不要蓋章請求）**

PowerShell（管線前先設編碼；審查素材用本切片全部 commit 的 diff）：

```powershell
$OutputEncoding = [System.Text.Encoding]::UTF8
git diff 9fbba2bf..HEAD -- LineMessagingProcessor LineMessagingProcessor.Tests ChurchReport/Services/PaymentNotificationService.cs > review-diff.txt
# 組審查 prompt（含背景與 diff）後：
Get-Content review-task.txt -Raw -Encoding UTF8 | & "$HOME\.claude\bin\codeagent-wrapper.exe" --progress --lite --backend gemini - "$env:TEMP"
Get-Content review-task.txt -Raw -Encoding UTF8 | & "$HOME\.claude\bin\codeagent-wrapper.exe" --progress --lite --backend claude - "$env:TEMP"
```

Expected: 兩個 backend exit 0、輸出審查報告。若 gemini exit 55 → `$env:GEMINI_CLI_TRUST_WORKSPACE='true'`；其他故障按 `docs/ccg-dual-model-troubleshooting.md` 分診表處理。Critical 必須修完並重審；Warning 記錄裁定。審查完把 `review-diff.txt`、`review-task.txt` 刪除（不進 commit）。

- [ ] **Step 5: 寫審查記錄**

把兩個模型的結論與裁定寫進 `.ccg/tasks/line-payment-notification-processor-convergence/review.md`（格式沿用 `.ccg/tasks/archive/2026-07/line-processor-sdk-backed-send-message/review.md`：Scope、External Review Results、Lead Synthesis（Critical/Warning/Info）、Verification Evidence），並把 `task.json` 的 `status` 改為 `done`、`currentPhase` 改為 `archived`。

- [ ] **Step 6: 勾掉本計畫 checkbox 並提交記錄**

```bash
git add .ccg/tasks/line-payment-notification-processor-convergence/ docs/superpowers/plans/2026-07-03-line-payment-notification-processor-convergence.md
git commit -m "chore: record LINE payment notification convergence review"
```

---

## Self-Review 記錄

- Spec 覆蓋：本 plan 只對應設計文件 §5 切片 0（含 C1/W1 審查債）；切片 1-6 各有後續 plan，不在此。
- 佔位符掃描：無 TBD/TODO；所有程式碼步驟含完整程式碼；所有指令含預期結果。
- 型別一致性：`LineMessagingProcessorClass(string, HttpClient)` 於 Task 1 定義、Task 2 使用；`SendReliableMessageAsync(string, string, string?)` 與現有簽章一致（`LineMessagingProcessorClass.cs:280`）；`LineResponseException.StatusCode` 與 `HttpResponseMessageExtensions.cs:32` 一致。
