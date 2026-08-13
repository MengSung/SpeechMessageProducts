# P7.4 認獻單讀取 Disabled Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改變現有付款流程的前提下，交付預設關閉的 ChurchReport 認獻單 typed-read 邊界。

**Architecture:** Bootstrap 將 deployment-owned base/sub-gate 短路於任何 options/host 建立之前；真正的 async service 只消費 ProductClient DTO 並發布 immutable result。獨立 adapter 只有在完整成功後才更新 request-local model，因此 fault/cancellation 不會污染舊模型。

**Tech Stack:** .NET、ASP.NET Core、xUnit、FluentAssertions、Microsoft.Extensions.Configuration/Options、SpeechMessage Dynamics ProductClient。

---

### Task 1: Gate 與 Factory

**Files:**

- Modify: `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- Modify: `SpeechMessageProducts.ChurchReport/appsettings.json`
- Modify: `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
- Modify: `SpeechMessageProducts.ChurchReport/Properties/launchSettings.json`

- [ ] **Step 1: 寫入 fail-first gate/factory tests**

```csharp
DonationDynamicsAccessBootstrap.IsPackage01DedicationBookingReadEnabled(subGateOnly)
    .Should().BeFalse();
DonationDynamicsAccessBootstrap.TryCreatePackage01DedicationBookingReadClient(disabled)
    .Should().BeNull();
```

- [ ] **Step 2: 執行測試並確認 API 缺失導致編譯失敗**

Run: `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~DonationDynamicsAccessBootstrapLifecycleTests`

Expected: 失敗訊息指出新的 gate/factory 尚不存在。

- [ ] **Step 3: 最小實作 gate/factory**

```csharp
if (!IsPackage01DedicationBookingReadEnabled(configuration))
{
    return null;
}

var productOptions = BindOptions(configuration);
EnsureNonEmptyProductProfile(productOptions, "Package01 dedication booking read");
```

- [ ] **Step 4: 將所有 repository settings 保持 false 並重跑測試**

Run: 同 Step 2。

Expected: PASS。

### Task 2: DTO-only Service

**Files:**

- Create: `ChurchReport.MemberInfo.Tests/Services/DonationBookingReadServiceTests.cs`
- Create: `SpeechMessageProducts.ChurchReport/Services/DonationBookingReadService.cs`

- [ ] **Step 1: 寫入 fail-first service tests**

```csharp
await service.RetrieveAsync(Guid.Empty, CancellationToken.None)
    .Invoking(task => task)
    .Should().ThrowAsync<ArgumentException>();
```

以及 fake client 斷言 `profileAlias == "crm91"`、固定 workload、同一 cancellation token；
null row 或空 ID 必須使整次 call 失敗。

- [ ] **Step 2: 執行測試並確認 type/API 缺失**

Run: `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~DonationBookingReadServiceTests`

Expected: 失敗訊息指出 `DonationBookingReadService` 尚不存在。

- [ ] **Step 3: 實作最小 async service 與 immutable result**

```csharp
var rows = await _client.RetrieveDedicationBookingsByContactAsync(
    RequireProfileAlias(), WorkloadSubjectId, contactId, cancellationToken: cancellationToken)
    .ConfigureAwait(false);
```

先完整驗證，再以本地 list 建立 `DonationBookingReadResult`；禁止 Entity、ToolUtility、
shared collection、retry 或 fallback。

- [ ] **Step 4: 重跑 focused test 至 PASS**

Run: 同 Step 2。

Expected: PASS。

### Task 3: 原子 Adapter

**Files:**

- Modify: `ChurchReport.MemberInfo.Tests/Services/DonationBookingReadServiceTests.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Services/DonationBookingReadService.cs`

- [ ] **Step 1: 寫入 fail-first atomic-update tests**

```csharp
var original = model.DedicationBookingList;
await adapter.PopulateAsync(model, contactId, cancellationToken);
model.DedicationBookingList.Should().NotBeSameAs(original);
```

對 cancellation/fault case，斷言 `model.DedicationBookingList.Should().BeSameAs(original)`。

- [ ] **Step 2: 執行 focused test 確認 adapter 尚不存在**

Run: 同 Task 2 Step 2。

Expected: 失敗原因為 API 未實作。

- [ ] **Step 3: 實作只在完整成功後才 replace 的 adapter**

```csharp
var result = await _readService.RetrieveAsync(contactId, cancellationToken).ConfigureAwait(false);
var replacement = result.Rows.Select(Map).ToList();
model.DedicationBookingList = replacement;
```

`Map` 僅使用 scalar DTO values；不得回填 CRM Entity 或修改 `FillBookingList`。

- [ ] **Step 4: 重跑 focused test 至 PASS**

Run: 同 Task 2 Step 2。

Expected: PASS。

### Task 4: 整合檢查與提交

**Files:**

- Modify: child task artifacts、`.ccg/tasks/p74-dedication-booking-read-boundary/*`

- [ ] **Step 1: 執行 test project 與 Release build**

Run: commands in Trellis implement plan。

Expected: PASS。

- [ ] **Step 2: 執行 encoding/scope/diff review**

Run: UTF-8/CRLF verifier、`git diff --check`、`git diff --stat`。

Expected: modified paths 僅屬 child scope。

- [ ] **Step 3: 執行 45 秒上限 CCG review 並記錄結果**

Run: `Start-CcgDualModelRun.ps1` with `-AllowSingleModelWhenQuotaBlocked`。

Expected: 兩份可用 review 或明確的「雙模型未完成」降級紀錄。

- [ ] **Step 4: 完成 Trellis check、scope-only commit、archive**

提交不得包含其他既有工作區變更；P7.5/P8 仍保持未開始。
