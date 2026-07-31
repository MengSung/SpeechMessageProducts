# Gateway Product Response Boundary Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent upstream OData routing annotations from reaching product-facing Gateway responses and make operation responses uncacheable by shared or private HTTP caches.

**Architecture:** Keep the current product envelope (`operationId`, `ceVersion`, `data`) unchanged. The Web API connector recursively projects the bounded parsed response into a new `JsonElement`, omitting only the two explicitly unsafe OData annotations before it becomes `OperationExecutionResult.Data`; a path-limited Gateway middleware sets the response header before authentication so operation and catalog successes plus controlled 401/403/415 errors cannot be cached.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, `System.Text.Json`, xUnit, FluentAssertions.

---

## File structure

- Modify `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`: add the bounded recursive product projection immediately after response parsing and before constructing the product envelope.
- Modify `SpeechMessage.Dynamics.Gateway/Program.cs`: add a local `SetPrivateNoStoreResponseHeaders(HttpResponse)` helper and route-limited pre-authentication middleware for the operation and catalog paths.
- Modify `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`: add a regression proving product data retains ordinary business fields while removing both upstream OData routing annotations, including nested occurrences.
- Modify `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`: add a regression proving successful authorized operation and catalog responses both have `Cache-Control: no-store, private`.

### Task 1: Lock the product OData boundary with a failing test

**Files:**

- Modify: `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`

- [ ] **Step 1: Add a regression test after `Successful_result_does_not_disclose_internal_web_api_root`**

```csharp
[Fact]
public async Task Successful_result_removes_upstream_odata_routing_annotations()
{
    var client = CreateClient(_ => JsonResponse("""
        {
          "@odata.context":"https://crm.example.local/org/api/data/v8.2/$metadata#WhoAmIResponse",
          "value":[{
            "name":"保留的商業資料",
            "@odata.nextLink":"https://crm.example.local/org/api/data/v8.2/accounts?$skiptoken=secret"
          }],
          "nested":{"@odata.context":"https://crm.example.local/internal","businessId":"abc-123"}
        }
        """));

    var result = await client.WhoAmIAsync();

    result.Succeeded.Should().BeTrue();
    using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
    var data = document.RootElement.GetProperty("data");
    data.TryGetProperty("@odata.context", out _).Should().BeFalse();
    data.GetProperty("value")[0].TryGetProperty("@odata.nextLink", out _).Should().BeFalse();
    data.GetProperty("nested").TryGetProperty("@odata.context", out _).Should().BeFalse();
    data.GetProperty("value")[0].GetProperty("name").GetString().Should().Be("保留的商業資料");
    data.GetProperty("nested").GetProperty("businessId").GetString().Should().Be("abc-123");
    JsonSerializer.Serialize(result.Data).Should().NotContain("crm.example.local");
}
```

- [ ] **Step 2: Run the focused regression and confirm red**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter FullyQualifiedName~Successful_result_removes_upstream_odata_routing_annotations --no-restore
```

Expected: FAIL because the current `data` element still serializes the upstream `@odata.context` and nested `@odata.nextLink` URL.

### Task 2: Project the bounded response before it enters the product envelope

**Files:**

- Modify: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs:355-379`

- [ ] **Step 1: Replace the parsed-data assignment with the explicit product projection**

```csharp
data = ProjectProductResponseData(read.Data);
```

Place this after the existing oversized-response check. Preserve all existing parser, cancellation, buffer-zeroing, response-disposal, retry, and URI-allowlist code.

- [ ] **Step 2: Add the private projection methods before `ReadBoundedJsonAsync`**

```csharp
private static JsonElement? ProjectProductResponseData(JsonElement? data)
{
    if (data is not JsonElement source)
    {
        return null;
    }

    var buffer = new ArrayBufferWriter<byte>();
    try
    {
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            WriteProductSafeJson(writer, source);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
    finally
    {
        buffer.Clear();
    }
}

private static void WriteProductSafeJson(Utf8JsonWriter writer, JsonElement element)
{
    switch (element.ValueKind)
    {
        case JsonValueKind.Object:
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("@odata.context") || property.NameEquals("@odata.nextLink"))
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);
                WriteProductSafeJson(writer, property.Value);
            }

            writer.WriteEndObject();
            break;
        case JsonValueKind.Array:
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
            {
                WriteProductSafeJson(writer, item);
            }

            writer.WriteEndArray();
            break;
        default:
            element.WriteTo(writer);
            break;
    }
}
```

Add `using System.Buffers;` and `using System.Text.Encodings.Web;`. Add complete Traditional Chinese XML documentation that describes the trust boundary, the bounded temporary buffer, its deterministic `Clear` path, the cloned element, and why this projection must precede `OperationExecutionResult.Data`. The relaxed encoder deliberately avoids HTML escaping expansion; the already bounded upstream JSON is therefore the finite upper bound for the temporary projection payload.

- [ ] **Step 3: Run the focused projection test and confirm green**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter FullyQualifiedName~Successful_result_removes_upstream_odata_routing_annotations --no-restore
```

Expected: PASS; the two annotations and their absolute CRM URLs are absent, while ordinary business fields remain.

### Task 3: Lock private/no-store headers with failing endpoint tests

**Files:**

- Modify: `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`

- [ ] **Step 1: Add a regression after the existing authorized operation and catalog tests**

```csharp
[Fact]
public async Task Authorized_operation_and_catalog_responses_are_private_no_store()
{
    var executor = new RecordingExecutor();
    await using var factory = CreateFactory(executor, DefaultPrincipalName, mapped: true);
    using var client = factory.CreateClient();

    using var operationResponse = await client.PostAsync(
        $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
        Json("{\"parameters\":{}}"));
    using var catalogResponse = await client.GetAsync("/v1/operations");

    operationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    catalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    operationResponse.Headers.CacheControl.Should().NotBeNull();
    operationResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
    operationResponse.Headers.CacheControl.Private.Should().BeTrue();
    catalogResponse.Headers.CacheControl.Should().NotBeNull();
    catalogResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
    catalogResponse.Headers.CacheControl.Private.Should().BeTrue();
}
```

- [ ] **Step 2: Add a controlled-error regression that covers the pre-authentication boundary**

```csharp
[Fact]
public async Task Operation_and_catalog_controlled_error_responses_are_private_no_store()
{
    var authorizedExecutor = new RecordingExecutor();
    await using var authorizedFactory = CreateFactory(
        authorizedExecutor,
        DefaultPrincipalName,
        mapped: true);
    using var authorizedClient = authorizedFactory.CreateClient();
    using var unsupportedMediaType = await authorizedClient.PostAsync(
        $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
        new StringContent("{}", Encoding.UTF8, "text/plain"));

    var unmappedExecutor = new RecordingExecutor();
    await using var unmappedFactory = CreateFactory(
        unmappedExecutor,
        @"SPEECHMESSAGE\UnmappedService$",
        mapped: false);
    using var unmappedClient = unmappedFactory.CreateClient();
    using var forbiddenCatalog = await unmappedClient.GetAsync("/v1/operations");

    using var anonymousFactory = CreateFactory(new RecordingExecutor(), principalName: null, mapped: true);
    using var anonymousClient = anonymousFactory.CreateClient();
    using var unauthorizedCatalog = await anonymousClient.GetAsync("/v1/operations");

    unsupportedMediaType.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    forbiddenCatalog.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    unauthorizedCatalog.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    foreach (var response in new[] { unsupportedMediaType, forbiddenCatalog, unauthorizedCatalog })
    {
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.CacheControl.Private.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Run the focused endpoint regressions and confirm red**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~Authorized_operation_and_catalog_responses_are_private_no_store|FullyQualifiedName~Operation_and_catalog_controlled_error_responses_are_private_no_store" --no-restore
```

Expected: FAIL because neither endpoint currently sets a `Cache-Control` header. In particular, the anonymous catalog test proves that an endpoint-delegate-only implementation would be insufficient because authorization returns 401 before the delegate executes.

### Task 4: Apply one path-limited private/no-store response policy before authorization

**Files:**

- Modify: `SpeechMessage.Dynamics.Gateway/Program.cs:161-164`

- [ ] **Step 1: Insert the path-limited middleware immediately before `app.UseAuthentication()`**

```csharp
app.Use(async (context, next) =>
{
    if (IsProductOperationOrCatalogRequest(context.Request))
    {
        SetPrivateNoStoreResponseHeaders(context.Response);
    }

    await next().ConfigureAwait(false);
});
```

The middleware must precede authentication and authorization so it covers successful results plus `401`, `403`, `415`, `413`, `400`, and executor-controlled error responses without changing their bodies or status codes.

- [ ] **Step 2: Add the local path matcher and header helper before `IsDevelopmentHttpsLoopbackRequest`**

```csharp
/// <summary>
/// 判定 request 是否為產品可見的受控操作或操作目錄。比對只依 HTTP method 與固定 REST path 前綴，
/// 不讀取 body、principal、token 或路由參數，也不建立任何快取、計時器或可釋放資源；因此可在驗證前安全設定
/// 回應的快取邊界，並讓未驗證的拒絕回應同樣受保護。
/// </summary>
static bool IsProductOperationOrCatalogRequest(HttpRequest request)
{
    var path = request.Path.Value;
    return (HttpMethods.IsGet(request.Method) &&
            string.Equals(path, "/v1/operations", StringComparison.Ordinal)) ||
        (HttpMethods.IsPost(request.Method) &&
            path is not null &&
            path.StartsWith("/v1/organizations/", StringComparison.Ordinal));
}

/// <summary>
/// 將產品可見的 Gateway 操作回應標示為僅限單次私有傳遞，避免 CRM 業務資料、錯誤狀態或 workload 可見的操作目錄
/// 被瀏覽器、反向 Proxy 或共享快取重播。此 helper 不保存 HttpContext、principal、body、token 或任何可釋放資源；
/// response header 的唯一 owner 仍是 ASP.NET Core request scope，request 結束時由框架完成清理。
/// </summary>
static void SetPrivateNoStoreResponseHeaders(HttpResponse response)
{
    response.Headers.CacheControl = "no-store, private";
}
```

The matcher intentionally only covers the exact `GET /v1/operations` catalog and `POST /v1/organizations/...` operation route family. Compare the nullable `Path.Value` strings with explicit ordinal semantics; this avoids the non-matching `PathString` overload/segment behavior observed during the regression test. Parenthesize each condition to make the `&&`/`||` precedence explicit.

- [ ] **Step 3: Run the focused endpoint regressions and confirm green**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~Authorized_operation_and_catalog_responses_are_private_no_store|FullyQualifiedName~Operation_and_catalog_controlled_error_responses_are_private_no_store" --no-restore
```

Expected: PASS; every success and controlled error `HttpResponseMessage.Headers.CacheControl` value has `NoStore == true` and `Private == true`.

### Task 5: Verify the complete local lane and preserve evidence

**Files:**

- Modify: `docs/superpowers/plans/2026-07-31-gateway-product-response-boundary-remediation.md` (mark only completed steps after evidence exists)

- [ ] **Step 1: Run the Dynamics test project**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore
```

Expected: PASS with no regression in the existing response-boundary, authentication, request-body, pool, or lifecycle tests.

- [ ] **Step 2: Build the changed projects in Release configuration**

Run:

```powershell
dotnet build SpeechMessage.Dynamics.Gateway\SpeechMessage.Dynamics.Gateway.csproj --configuration Release --no-restore
```

Expected: `Build succeeded` with zero errors.

- [ ] **Step 3: Check only this change set and the modified-file encoding**

Run:

```powershell
git diff --check -- SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs SpeechMessage.Dynamics.Gateway/Program.cs SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs docs/superpowers/plans/2026-07-31-gateway-product-response-boundary-remediation.md
```

Expected: no output. Then verify each modified file is UTF-8 without BOM, CRLF-only, and ends with a CRLF.

- [ ] **Step 4: Run `trellis-check` without an external CCG review**

Use the project quality skill for local static, build, test, and scope verification. Do not invoke Gemini, Claude, or CCG review tooling because the user explicitly declined it for this work session.

- [ ] **Step 5: Commit only the local-lane remediation files after all verification is green**

```powershell
git add -- SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs SpeechMessage.Dynamics.Gateway/Program.cs SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs docs/superpowers/plans/2026-07-31-gateway-product-response-boundary-remediation.md
git commit -m "fix: protect gateway product response boundary"
```

Expected: one focused commit. Do not stage the existing CCG progress-report artifacts or claim that this completes the real CE 8.2/9.1 administrative verification gate.

## Plan self-review

- Spec coverage: Task 2 implements the product OData projection required by the Gateway hosting contract; Tasks 3–4 implement the private/no-store response contract; Task 5 verifies both while retaining the separate real-environment gate.
- Placeholder scan: no `TODO`, `TBD`, or generic testing steps remain; every code and validation step names the exact files, behavior, and command.
- Type consistency: `ProjectProductResponseData` preserves the existing nullable `JsonElement` data contract; `SetPrivateNoStoreResponseHeaders` accepts the existing ASP.NET Core `HttpResponse` from both endpoint delegates.
