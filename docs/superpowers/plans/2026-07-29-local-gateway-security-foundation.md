# Local Gateway Security Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or the project CCG parallel worker protocol. Every production change follows RED → GREEN → REFACTOR, and no worker may modify files outside its assigned ownership.

**Goal:** 在不弱化 Central Gateway 正式邊界、不使用 in-memory coordinator 假冒完成的前提下，建立 ChurchReport 可安全連線的 Local Gateway 安全基礎。

**Architecture:** Local Gateway 與 Central Gateway 共用 `ExecutionMode=Gateway` 與同一 HTTP contract。Development 使用 Windows Negotiate 與 HTTPS loopback，Gateway 依經驗證的 Windows principal 建立 bounded workload binding，再於建立 `OperationExecutionRequest` 前同時驗證 alias 與 operation。HTTP request body 與 queue dispatch envelope 都使用真實 UTF-8 byte 上限，Gateway 回應不得暴露 CRM 實體 endpoint。Durable host coordinator 使用明確 provision 的 SQL LocalDB 進行單機 Development 驗證，Gateway startup 仍只驗證 schema、不自行建表。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Windows Negotiate、xUnit、FluentAssertions、SQL Server LocalDB 17、Microsoft.Data.SqlClient。

---

## File ownership map

- Gateway authentication／authorization owner:
  - `SpeechMessage.Dynamics.Gateway/Program.cs`
  - `SpeechMessage.Dynamics.Gateway/SpeechMessage.Dynamics.Gateway.csproj`
  - `SpeechMessage.Dynamics.Gateway/Security/GatewayWorkloadBinding.cs`
  - `SpeechMessage.Dynamics.Gateway/Security/IGatewayOperationAuthorizer.cs`
  - `SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
  - `SpeechMessage.Dynamics.Gateway/appsettings.json`
  - `SpeechMessage.Dynamics.Gateway/appsettings.Development.json`
  - `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`
  - `SpeechMessage.Dynamics.Tests/GatewayKestrelNegotiateTests.cs`
- Dispatch byte-bound owner:
  - `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/PreparedOperationDispatch.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs`
  - `SpeechMessage.Dynamics.Tests/ControlledOperationExecutorTests.cs`
  - `SpeechMessage.Dynamics.Tests/Phase4IsolationSoakTests.cs`
- Product-facing response owner:
  - `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
  - `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`
- Durable LocalDB proof owner:
  - `docs/scripts/Provision-DynamicsControlPlaneLocalDb.ps1`
  - `eng/dynamics-control-plane-schema.sql`
  - `SpeechMessage.Dynamics.Tests/SqlRuntimeHostSlotCoordinatorTests.cs`
  - `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-gateway-security-verification.md`

所有新增 C# public/internal 型別與 security/lifecycle 方法，都必須有完整繁體中文 XML 文件，說明信任來源、唯一 owner、並行規則、fail-closed 行為、不得保留的資料與 cleanup 順序。安全關鍵的實作順序需有鄰近繁體中文註解。所有新增／修改文字檔均為 UTF-8 without BOM、CRLF。

### Task 1: Gateway workload binding and operation authorization

**Files:**

- Create: `SpeechMessage.Dynamics.Gateway/Security/GatewayWorkloadBinding.cs`
- Create: `SpeechMessage.Dynamics.Gateway/Security/IGatewayOperationAuthorizer.cs`
- Create: `SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
- Modify: `SpeechMessage.Dynamics.Gateway/Program.cs`
- Modify: `SpeechMessage.Dynamics.Gateway/appsettings.json`
- Test: `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`

- [ ] **Step 1: Write RED tests for unauthorized alias and operation**

新增下列測試，並使用既有 recording executor 驗證拒絕發生在 runtime/admission/transport 之前：

```csharp
[Fact]
public async Task Mapped_workload_cannot_call_alias_outside_binding()
{
    using var host = await CreateGatewayHostAsync(
        principalName: @"IIS APPPOOL\ChurchReport",
        allowedAlias: "sunnyvalechback-prod",
        allowedOperation: "fee.dedication.retrieve.by.contact.date.range");

    var response = await host.Client.PostAsJsonAsync(
        "/v1/organizations/crm82/operations/fee.dedication.retrieve.by.contact.date.range",
        new { parameters = new Dictionary<string, object?>() });

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    host.Executor.CallCount.Should().Be(0);
}

[Fact]
public async Task Mapped_workload_cannot_call_operation_outside_binding()
{
    using var host = await CreateGatewayHostAsync(
        principalName: @"IIS APPPOOL\ChurchReport",
        allowedAlias: "sunnyvalechback-prod",
        allowedOperation: "fee.dedication.retrieve.by.contact.date.range");

    var response = await host.Client.PostAsJsonAsync(
        "/v1/organizations/sunnyvalechback-prod/operations/runtime.health.whoami",
        new { parameters = new Dictionary<string, object?>() });

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    host.Executor.CallCount.Should().Be(0);
}
```

- [ ] **Step 2: Run RED tests**

Run:

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~GatewayWorkloadBoundaryTests"
```

Expected: 新測試失敗，現況 mapped principal 可直接進入 executor，且 `/v1/operations` 仍可匿名列舉。

- [ ] **Step 3: Add immutable binding model and fail-closed authorizer**

新增的 authorizer contract 固定回傳 server-derived workload，不接受 client body 的 workload：

```csharp
public interface IGatewayOperationAuthorizer
{
    GatewayOperationAuthorization Authorize(
        ClaimsPrincipal principal,
        string profileAlias,
        string capabilityOperationId);
}

public sealed record GatewayOperationAuthorization(
    bool Succeeded,
    string WorkloadSubjectId,
    string ProfileAlias,
    string CapabilityOperationId,
    string FailureCode);
```

`ConfigurationGatewayOperationAuthorizer` 必須：

- 只接受 `principal.Identity.IsAuthenticated == true`；
- 優先以 Windows SID，比對不到才使用 exact authenticated principal name；
- 將 alias／operation 以 `OrdinalIgnoreCase` 正規化，但保留 registry canonical operation ID；
- 拒絕 wildcard、空字串、重複 principal、重複 SID、未知 alias、未知 operation；
- 不保存 `ClaimsPrincipal`、HttpContext、SID、token 或 raw request；成功結果只保存 bounded workload／alias／operation；
- constructor 建立不可變 lookup，任何設定錯誤在 host startup fail closed。

- [ ] **Step 4: Wire the authorizer before request construction**

在 `Program.cs` 的 POST endpoint 中，先取得 authorization；失敗直接 `Results.Forbid()`，成功才建立 request：

```csharp
var authorization = operationAuthorizer.Authorize(
    httpContext.User,
    alias,
    capabilityOperationId);
if (!authorization.Succeeded)
{
    return Results.Forbid();
}

var request = new OperationExecutionRequest
{
    ProfileAlias = authorization.ProfileAlias,
    CapabilityOperationId = authorization.CapabilityOperationId,
    WorkloadSubjectId = authorization.WorkloadSubjectId,
    Parameters = body.Parameters ?? new Dictionary<string, object?>(),
    IdempotencyKey = body.IdempotencyKey
};
```

`/v1/operations` 必須 `.RequireAuthorization()`，並只回傳目前 principal 被授權的 operation；若尚未實作過濾，先移除該 endpoint，不能維持匿名 catalog。

- [ ] **Step 5: Run GREEN tests and existing boundary suite**

Run the same command. Expected: mapped/unauthorized alias、mapped/unauthorized operation、anonymous、unmapped principal 與 body identity override 全部通過，recording executor 在拒絕案例維持 0 次。

### Task 2: Development-only Negotiate and HTTPS loopback boundary

**Files:**

- Modify: `SpeechMessage.Dynamics.Gateway/SpeechMessage.Dynamics.Gateway.csproj`
- Modify: `SpeechMessage.Dynamics.Gateway/Program.cs`
- Create: `SpeechMessage.Dynamics.Gateway/appsettings.Development.json`
- Create: `SpeechMessage.Dynamics.Tests/GatewayKestrelNegotiateTests.cs`

- [ ] **Step 1: Write RED tests**

測試必須證明非 IIS TestServer/Kestrel 啟動時存在真實 Negotiate handler，而不是只有 scheme 名稱：

```csharp
[Fact]
public async Task Development_kestrel_challenges_with_negotiate()
{
    await using var factory = CreateDevelopmentGatewayFactory();
    using var response = await factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
        .PostAsJsonAsync(
            "/v1/organizations/sunnyvalechback-prod/operations/runtime.health.whoami",
            new { parameters = new Dictionary<string, object?>() });

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    response.Headers.WwwAuthenticate.Select(x => x.Scheme)
        .Should().Contain("Negotiate");
}
```

另加測試：Development operation request 的 `RemoteIpAddress` 非 loopback 時回 403；Testing fake scheme 只能由 Testing host 注入，不能從 Development JSON 選取。

- [ ] **Step 2: Run RED tests**

Expected: 現況因沒有 authentication handler 而啟動／challenge 失敗。

- [ ] **Step 3: Register Microsoft Negotiate only for Development Kestrel**

- 在 csproj 加入 net10 相容的 `Microsoft.AspNetCore.Authentication.Negotiate`。
- `Development` 固定 `NegotiateDefaults.AuthenticationScheme` 並呼叫 `.AddNegotiate()`；不可用任意 Header principal。
- `Testing` 仍只由 `WebApplicationFactory` 注入 fake handler。
- 非 Development 維持部署核准的 IIS Windows authentication；未知 scheme startup fail closed。
- 對 operation 與 catalog endpoint 加 loopback filter，允許 `IPAddress.IsLoopback(RemoteIpAddress)`，RemoteIpAddress 缺失也 fail closed；health/ready 可維持監控契約。

- [ ] **Step 4: Run GREEN tests**

Expected: Development challenge 為 401 Negotiate、remote client 為 403、Testing fake scheme 不會外洩到 Development。

### Task 3: Hard HTTP body limit and exact bounded dispatch envelope

**Files:**

- Modify: `SpeechMessage.Dynamics.Gateway/Program.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/PreparedOperationDispatch.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs`
- Modify: `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
- Test: `SpeechMessage.Dynamics.Tests/ControlledOperationExecutorTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/Phase4IsolationSoakTests.cs`

- [ ] **Step 1: Write RED boundary tests**

新增：

- declared `Content-Length = limit + 1` 在 JSON binding 前回 413、executor 0 次；
- chunked body 在讀超過 limit 的第一個 byte 時回 413；
- UTF-8 多位元組字串以 byte 計算，不能以 UTF-16 char 粗估；
- dictionary insertion order 不影響 canonical bytes/hash；
- exact limit-1、limit 接受，limit+1 在 queue 前拒絕；
- nested object、array、未知 type 在 prepare 階段拒絕；
- blocked queue 取消後，prepared buffer 被清除，queued／active permit／workload counter 回到 baseline。

- [ ] **Step 2: Run RED tests**

Expected: 現況非字串固定估 64 bytes，超大 nested JSON 可通過 admission，且 async state 保留原始 request parameters。

- [ ] **Step 3: Implement synchronous prepare boundary**

`OperationDispatchPreparer.Prepare` 必須在第一個 `await` 前：

1. 查 registry 並驗證 parameter count／name／required／declared type；
2. 正規化為只含核准 primitive／bounded array 的 immutable typed values；
3. 依 version、type tag、sorted parameter name、big-endian length prefix 建立穩定 UTF-8 bytes；
4. 若超過 `MaxDispatchEnvelopeBytes` 立即失敗，不進 admission queue；
5. 回傳唯一擁有 byte buffer 的 `PreparedOperationDispatch`。

`PreparedOperationDispatch` 必須實作 `IDisposable`，以 `CryptographicOperations.ZeroMemory` 清除 buffer；不得保存原始 `OperationExecutionRequest`、ClaimsPrincipal、HttpContext、token、credential 或 user/session identity。

- [ ] **Step 4: Split async queue execution from original request**

`ControlledOperationExecutor.ExecuteAsync` 在 prepare 成功後立即不再引用原始 request，後續只把 `PreparedOperationDispatch` 的 bounded envelope 送入 admission；permit 後使用 prepared typed parameters 呼叫 client，最後一定 dispose／zero buffer。

- [ ] **Step 5: Configure server body limit**

Kestrel 與 IIS request body limit 使用同一個部署設定與 hard upper bound；限制必須在 JSON deserialization 前生效。JSON options 同時設定有限 `MaxDepth`，Unknown member 維持 disallow。

- [ ] **Step 6: Run GREEN tests and Phase 4 focused suite**

Run:

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~ControlledOperationExecutorTests|FullyQualifiedName~Phase4IsolationSoakTests|FullyQualifiedName~GatewayWorkloadBoundaryTests"
```

Expected: 所有 byte／queue／cancellation／zeroing 測試通過，拒絕案例沒有 runtime/token/transport I/O。

### Task 4: Remove product-facing CRM endpoint disclosure

**Files:**

- Modify: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
- Test: `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`

- [ ] **Step 1: Write RED tests**

成功結果序列化後不得包含：`approvedWebApiRoot`、CRM hostname、`/api/data/`、secret、token、credential。

- [ ] **Step 2: Run RED tests**

Expected: 現況 `DynamicsWebApiClient.cs` 將 `approvedWebApiRoot` 加入 `OperationExecutionResult.Data`，測試失敗。

- [ ] **Step 3: Remove the field from success payload**

成功 payload 只保留 operation ID、CE contract version 與投影後的 `data`；內部 approved root 只能留在 profile runtime／transport，不可跨 Gateway contract。

- [ ] **Step 4: Run GREEN tests**

Expected: ProductClient 仍能解析 `data`，Gateway body 不再包含實體 CRM endpoint。

### Task 5: Explicit LocalDB provisioning and live durable coordinator proof

**Files:**

- Create: `docs/scripts/Provision-DynamicsControlPlaneLocalDb.ps1`
- Modify: `SpeechMessage.Dynamics.Tests/SqlRuntimeHostSlotCoordinatorTests.cs`
- Create: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-gateway-security-verification.md`

- [ ] **Step 1: Verify installed LocalDB and keep provisioning explicit**

Script parameters必須包含 instance name、database name 與 schema file；只允許 database `SpeechMessageDynamicsControlPlane`，預設 schema file 為 `eng/dynamics-control-plane-schema.sql`。Script 可啟動 user-owned LocalDB、建立 database、套用 schema，但 Gateway startup 不得呼叫此 script 或 `EnsureSchemaAsync`。

- [ ] **Step 2: Provision the local control-plane**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Provision-DynamicsControlPlaneLocalDb.ps1
```

Expected: LocalDB `MSSQLLocalDB` running，database 與三個 schema objects 存在；重跑為 idempotent。

- [ ] **Step 3: Run real SQL contract with explicit environment variable**

```powershell
$env:SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;Encrypt=false;Pooling=true;Max Pool Size=32;Connect Timeout=5;Application Name=SpeechMessage.Dynamics.Tests"
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~SqlRuntimeHostSlotCoordinatorTests"
Remove-Item Env:SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION
```

Expected: live create／renew／fence／release／epoch／quarantine contract 真正執行，不是缺環境變數後直接 return。

- [ ] **Step 4: Record evidence**

驗證文件需記錄 LocalDB 版本、實際執行的 test names/count、schema hash、連線字串只保留非秘密形狀，以及「LocalDB 只證明同一 Windows user 的單機 Development；不代表 Central multi-host production」。

### Task 6: Full local verification and review gate

- [ ] Run scoped formatting and build:

```powershell
dotnet format .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --verify-no-changes
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
```

- [ ] Run all Dynamics tests and focused Local Gateway tests.
- [ ] Run strict UTF-8/no-BOM/CRLF validation on every added/modified text file.
- [ ] Enumerate added C# public/internal types and important methods; verify substantive Traditional Chinese XML documentation and nearby safety-order comments.
- [ ] Run `git diff --check` and added-line secret scan.
- [ ] Run the required CCG Gemini＋Claude reviewer through `docs/scripts/Start-CcgDualModelRun.ps1`; verify every Critical/Warning against actual code, fix, and repeat until no open Critical remains.
- [ ] Do not declare Phase 4 complete: CE 9.1 profile activation proof、ChurchReport host configuration、real WhoAmI、durable audit、multi-host capacity、fault/soak/performance remain separate gates.
