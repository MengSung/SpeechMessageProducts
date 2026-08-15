# P7 MemberInfo small-group snapshot data-plane 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 以既有 immutable MemberInfo target scope 產生 ORG-CALL-00031／00032 的 CE 9.1 local-only bounded snapshot read。

**Architecture:** 新 response union 由 fixed Data8 composed operation 建立 descriptor/membership snapshot；ProductClient 與 ChurchReport source 僅映射與 defensive-copy。無 Controller、Session、legacy fallback 或 CE runtime action。

**Tech Stack:** .NET/C#、Dynamics 9.1 Data8 connector、ProductClient、xUnit/FluentAssertions、Trellis/CCG。

---

### Task 1：Abstractions registry 與 response contract

**Files:**
- Modify: `SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs`
- Modify: `SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs`
- Modify: `SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs`
- Test: `SpeechMessage.Dynamics.Tests/MemberInfoSmallGroupSnapshotRegistryTests.cs`

- [ ] 先寫 RED tests，assert exact operation ID/template/CE 9.1 branch、三個 scope parameters、fixed bounds 和 one-and-only snapshot response branch。
- [ ] 新增 immutable wire descriptor/membership/snapshot types、factory 和 validation；factory 只能 publish copied records，拒絕 invalid mode/IDs、duplicate descriptor/member identity、non-subset membership、text/row/byte overflow。
- [ ] 新增 registry `memberinfo.smallgroup.snapshot.retrieve.authorized`，固定 operation metadata、queryexpression template、response kind、security audit、read-only idempotency、512 descriptor/4096 membership budgets。
- [ ] 跑 registry tests，確認 RED→GREEN；無 caller query/profile/credential/owner parameter。

### Task 2：Data8 composed operation 與 executor routing

**Files:**
- Modify: `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs`
- Modify: `SpeechMessage.Dynamics.Connectors.Data8/OnPremiseData8ConnectorClientFactory.cs`
- Create: `SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoSmallGroupSnapshotOperations.cs`
- Test: `SpeechMessage.Dynamics.Tests/MemberInfoSmallGroupSnapshotData8Tests.cs`

- [ ] 寫 RED tests：invalid scope/version/registry 在 router/lease 前零 I/O；Church-wide/assigned-list exact query；metadata closed-status ambiguity、MoreRecords/cookie、overflow、malformed row、duplicate ID、non-subset membership 和 cancellation 均 fail closed。
- [ ] 實作 CE 9.1-only routing，驗證所有 parameters 是 subject/mode/copied scope IDs 的 exact schema；從 fixed `RetrieveAttributeRequest` 取得唯一 closed status。
- [ ] 實作 descriptor query，再以其 returned IDs 建立 membership join query；兩個 query 均固定 projection/order/overflow sentinel 並嚴格計算 byte/row budget。不可接收 list/contact/browser filter 或使用 `RetrieveAllEntities`。
- [ ] 跑 Data8 focused suite，確認 fault/cancellation 不發布 partial response 且 executor retains resource ownership。

### Task 3：ProductClient immutable snapshot client

**Files:**
- Create: `SpeechMessage.Dynamics.ProductClient/MemberInfo/IMemberInfoSmallGroupSnapshotReadClient.cs`
- Create: `SpeechMessage.Dynamics.ProductClient/MemberInfo/MemberInfoSmallGroupSnapshotReadClient.cs`
- Modify: `SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs`
- Test: `SpeechMessage.Dynamics.Tests/MemberInfoSmallGroupSnapshotClientTests.cs`

- [ ] 寫 RED tests：invalid routing/scope zero dispatch、exact operation/version/branch/subject/mode、token passthrough、defensive copy、wrong wire payload fail closed、A/B profile/token/result interleaving。
- [ ] 實作 stateless client，僅將 deployment routing + copied request scope 送入 executor，並把 valid wire snapshot defensive-copy 成 read-only DTO；所有 failure 都不得回傳 partial list。
- [ ] 將 interface/client 加入 local ProductClient DI registration；這不是 Controller registration、feature enablement 或 CE evidence。
- [ ] 跑 ProductClient focused suite。

### Task 4：ChurchReport local source 與隔離契約

**Files:**
- Create: `SpeechMessageProducts.ChurchReport/Security/MemberInfoSmallGroupSnapshotSource.cs`
- Test: `ChurchReport.MemberInfo.Tests/Security/MemberInfoSmallGroupSnapshotSourceTests.cs`
- Modify: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json`

- [ ] 寫 RED tests：null/invalid scope zero typed-client I/O、source fault/deformed payload fail closed、A/B interleaving、scope/DTO defensive copies、reflection guard 無 Session/HttpContext/ClaimsPrincipal/ListManager/CRM SDK/cache/static mutable state。
- [ ] 實作 internal source：只接受 `MemberInfoTargetAuthorizationScope`，將它轉為 typed request；非取消 exception 映射成去識別化 unavailable result，取消原樣傳遞，沒有 fallback。
- [ ] 以 derived local-only mapping 記錄 00031／00032 的新 operation 及仍 pending 的 CE/consumer evidence；不修改 70-row normalized source inventory 或 00033。
- [ ] 跑 ChurchReport focused suite。

### Task 5：整合品質、審查與封存

- [ ] 每層完成後跑 targeted test；整體跑 `dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore`、`dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore`、Release solution tests/build。
- [ ] byte-level 檢查所有修改 `.cs` UTF-8 無 BOM、CRLF、final CRLF，並執行 `git diff --check`、scope review 和 task validation。
- [ ] 以 CCG self-healing runner 各做一次 45 秒 architecture/review；逾時只記為「雙模型未完成」，不重跑等待。
- [ ] 更新 task records、必要 code-spec，scope-only commit/archive；不 stage 既有工作區變更。

