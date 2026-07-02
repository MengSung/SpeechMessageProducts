# LINE Messaging SDK P0 Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking. This repository is in inline mode, so do not dispatch implement/check subagents.

**Goal:** Fix only the `P0` LINE Messaging API defects documented in `Line.Messaging/?辣/LINE_Messaging_API_摰撠?拚.md`.

**Architecture:** Keep the LINE SDK boundary small: `Line.Messaging` owns official API URL/method/payload behavior, and `LineMessagingProcessor` owns product-facing convenience behavior without embedded credentials. Introduce one focused URL base helper inside `LineMessagingClient` instead of a broad rewrite, and protect every fixed endpoint with a request-capturing unit test.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, Newtonsoft.Json, existing `Line.Messaging` and `LineMessagingProcessor` projects.

---

## Scope And Order

This plan intentionally handles `P0` only:

1. Hardcoded LINE channel access tokens in `LineMessagingProcessorClass`.
2. Data-host endpoints that must use `https://api-data.line.me/v2`.
3. `MarkAsReadAsync` official endpoint and payload.
4. Rich menu batch progress and validate endpoints.
5. Duplicate `/v2/v2` paths in insights, coupon, and membership endpoints.

Do not implement `P1` / `P2` items here:

- Do not implement Audience `NotImplementedException` methods.
- Do not redesign message object models.
- Do not modernize OAuth/token flows beyond avoiding regressions.
- Do not refactor ChurchReport LINE workflows unless compile-time compatibility requires it.

## Files And Responsibilities

- Create `Line.Messaging.Tests/Line.Messaging.Tests.csproj`
  - Dedicated SDK tests for request URL, HTTP method, body, and host selection.
- Create `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs`
  - Tests `LineMessagingClient` P0 endpoint behavior through a fake `HttpMessageHandler`.
- Create `Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs`
  - Tests that `LineMessagingProcessorClass` no longer contains or sends built-in bearer tokens.
- Modify `Line.Messaging/LineMessagingClient.cs`
  - Add the smallest URL helper needed to select JSON API vs data API base URI and avoid duplicate `/v2`.
  - Fix P0 endpoint strings and request payloads.
- Modify `Line.Messaging/ILineMessagingClient.cs`
  - Add official token-based mark-as-read overload while keeping the old overload obsolete for compatibility.
- Modify `LineMessagingProcessor/LineMessagingProcessorClass.cs`
  - Remove all literal channel tokens.
  - Add token-injection constructor.
  - Make parameterless constructor read from `LINE_CHANNEL_ACCESS_TOKEN` and fail clearly before sending if no token exists.
- Modify `ChurchReport.sln`
  - Add `Line.Messaging.Tests` project.
- Modify `.ccg/tasks/line-messaging-sdk-p0-fixes/*`
  - Keep planning, review, and verification records.

## Design Constraints

- One endpoint family per task. A task should be easy to review by reading one test file and one implementation diff.
- Do not add a general-purpose routing framework. The URL helper can be private to `LineMessagingClient` unless a second class needs it.
- Public API compatibility matters. Existing `MarkAsReadAsync(string chatId)` callers should compile; the method should be marked `[Obsolete]` and delegate to the official token-based behavior only if the caller passes a token.
- Tests must assert absolute URLs. Relative path tests are not enough because the current failures are host and `/v2/v2` mistakes.
- Do not print or commit real token values in tests, docs, or diagnostics.
- Text files touched by this plan must remain UTF-8 without BOM and CRLF.

---

### Task 1: Create LINE SDK Test Project And HTTP Capture Helper

**Files:**
- Create: `Line.Messaging.Tests/Line.Messaging.Tests.csproj`
- Create: `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs`
- Modify: `ChurchReport.sln`

- [x] **Step 1: Create the test project file**

Create `Line.Messaging.Tests/Line.Messaging.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: Add the project to the solution**

Run:

```powershell
dotnet sln ChurchReport.sln add Line.Messaging.Tests\Line.Messaging.Tests.csproj
```

Expected: the solution adds one new project.

- [x] **Step 3: Write the shared request-capture helper**

Create `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs` with the helper skeleton:

```csharp
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessagingClientP0EndpointTests
{
    private static LineMessagingClient CreateClient(CapturingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new LineMessagingClient(httpClient, "test-token");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly string _mediaType;

        public CapturingHttpMessageHandler(
            string responseBody = "{}",
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string mediaType = "application/json")
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _mediaType = mediaType;
        }

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, _mediaType)
            };
        }
    }
}
```

- [x] **Step 4: Run the empty test project**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal
```

Expected now: may fail with missing assets if restore has not run.

- [x] **Step 5: Restore and rerun the empty test project**

Run:

```powershell
dotnet restore Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal
```

Expected: test project builds successfully with 0 tests or no failing tests.

- [x] **Step 6: Commit the test project skeleton**

Run:

```powershell
git add ChurchReport.sln Line.Messaging.Tests\Line.Messaging.Tests.csproj Line.Messaging.Tests\LineMessagingClientP0EndpointTests.cs
git commit -m "test: add LINE Messaging SDK endpoint test project"
```

---

### Task 2: Add Minimal URL Base Helper To LineMessagingClient

**Files:**
- Modify: `Line.Messaging/LineMessagingClient.cs`
- Test: `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs`

- [x] **Step 1: Write failing tests for base URL normalization**

Append these tests inside `LineMessagingClientP0EndpointTests`:

```csharp
[Fact]
public async Task Get_message_delivery_uses_single_v2_segment()
{
    var handler = new CapturingHttpMessageHandler("""{"status":"ready","success":0}""");
    var client = CreateClient(handler);

    await client.GetMessageDeliveryAsync(new DateTime(2026, 7, 2));

    handler.Requests.Should().ContainSingle();
    handler.Requests[0].RequestUri!.ToString()
        .Should().Be("https://api.line.me/v2/bot/insight/message/delivery?date=20260702");
}

[Fact]
public async Task Get_content_stream_uses_api_data_host()
{
    var handler = new CapturingHttpMessageHandler("binary-content", mediaType: "application/octet-stream");
    var client = CreateClient(handler);

    using var result = await client.GetContentStreamAsync("message-123");

    handler.Requests.Should().ContainSingle();
    handler.Requests[0].RequestUri!.ToString()
        .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content");
}
```

- [x] **Step 2: Run the two tests and verify they fail**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~Get_message_delivery_uses_single_v2_segment|FullyQualifiedName~Get_content_stream_uses_api_data_host" -v minimal
```

Expected before implementation:

- `Get_message_delivery_uses_single_v2_segment` fails because actual URL contains `/v2/v2/`.
- `Get_content_stream_uses_api_data_host` fails because actual host is `api.line.me`.

- [x] **Step 3: Add private URL helper methods**

Modify the field area in `Line.Messaging/LineMessagingClient.cs`:

```csharp
private const string DEFAULT_URI = "https://api.line.me/v2";
private const string DEFAULT_DATA_URI = "https://api-data.line.me/v2";
private string _uri;
private string _dataUri;
```

In both constructors after `_uri = uri;`, add:

```csharp
_dataUri = DeriveDataUri(uri);
```

Add private helpers near the constructors:

```csharp
private static string DeriveDataUri(string apiUri)
{
    if (string.IsNullOrWhiteSpace(apiUri))
    {
        return DEFAULT_DATA_URI;
    }

    return apiUri.Replace("https://api.line.me", "https://api-data.line.me");
}

private string ApiUrl(string path)
{
    return CombineBaseAndPath(_uri, path);
}

private string DataUrl(string path)
{
    return CombineBaseAndPath(_dataUri, path);
}

private static string CombineBaseAndPath(string baseUri, string path)
{
    if (string.IsNullOrWhiteSpace(baseUri))
    {
        throw new ArgumentException("Base URI is required.", nameof(baseUri));
    }

    if (string.IsNullOrWhiteSpace(path))
    {
        throw new ArgumentException("Path is required.", nameof(path));
    }

    var normalizedBase = baseUri.TrimEnd('/');
    var normalizedPath = path.TrimStart('/');

    if (normalizedBase.EndsWith("/v2", StringComparison.OrdinalIgnoreCase)
        && normalizedPath.StartsWith("v2/", StringComparison.OrdinalIgnoreCase))
    {
        normalizedPath = normalizedPath.Substring("v2/".Length);
    }

    return normalizedBase + "/" + normalizedPath;
}
```

- [x] **Step 4: Change only the two URLs needed for the failing tests**

Change:

```csharp
GetStringAsync($"{_uri}/v2/bot/insight/message/delivery?date={date:yyyyMMdd}")
```

to:

```csharp
GetStringAsync(ApiUrl($"/v2/bot/insight/message/delivery?date={date:yyyyMMdd}"))
```

Change:

```csharp
_client.GetAsync($"{_uri}/bot/message/{messageId}/content")
```

inside `GetContentStreamAsync` to:

```csharp
_client.GetAsync(DataUrl($"/bot/message/{messageId}/content"))
```

- [x] **Step 5: Run the two tests and verify they pass**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~Get_message_delivery_uses_single_v2_segment|FullyQualifiedName~Get_content_stream_uses_api_data_host" -v minimal
```

Expected: both tests pass.

- [x] **Step 6: Commit the URL helper**

Run:

```powershell
git add Line.Messaging\LineMessagingClient.cs Line.Messaging.Tests\LineMessagingClientP0EndpointTests.cs
git commit -m "fix: add LINE API URL base helper"
```

---

### Task 3: Fix Data API Host Endpoints

**Files:**
- Modify: `Line.Messaging/LineMessagingClient.cs`
- Test: `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs`

- [x] **Step 1: Add failing tests for all P0 data-host endpoints**

Append:

```csharp
[Fact]
public async Task Get_content_bytes_uses_api_data_host()
{
    var handler = new CapturingHttpMessageHandler("binary-content", mediaType: "application/octet-stream");
    var client = CreateClient(handler);

    await client.GetContentBytesAsync("message-123");

    handler.Requests[0].RequestUri!.ToString()
        .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content");
}

[Fact]
public async Task Verify_content_preparation_uses_transcoding_endpoint_on_api_data_host()
{
    var handler = new CapturingHttpMessageHandler("""{"status":"succeeded"}""");
    var client = CreateClient(handler);

    await client.VerifyContentPreparationAsync("message-123");

    handler.Requests[0].RequestUri!.ToString()
        .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content/transcoding");
}

[Fact]
public async Task Get_content_preview_uses_api_data_host()
{
    var handler = new CapturingHttpMessageHandler("preview-content", mediaType: "application/octet-stream");
    var client = CreateClient(handler);

    using var result = await client.GetContentPreviewAsync("message-123");

    handler.Requests[0].RequestUri!.ToString()
        .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content/preview");
}

[Fact]
public async Task Rich_menu_image_download_and_upload_use_api_data_host()
{
    var handler = new CapturingHttpMessageHandler("image-content", mediaType: "application/octet-stream");
    var client = CreateClient(handler);

    using var download = await client.DownloadRichMenuImageAsync("rich-1");
    await client.UploadRichMenuJpegImageAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "rich-1");
    await client.UploadRichMenuPngImageAsync(new MemoryStream(new byte[] { 4, 5, 6 }), "rich-1");

    handler.Requests.Select(request => request.RequestUri!.ToString()).Should().Equal(
        "https://api-data.line.me/v2/bot/richmenu/rich-1/content",
        "https://api-data.line.me/v2/bot/richmenu/rich-1/content",
        "https://api-data.line.me/v2/bot/richmenu/rich-1/content");
    handler.Requests.Select(request => request.Method).Should().Equal(HttpMethod.Get, HttpMethod.Post, HttpMethod.Post);
}
```

- [x] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~uses_api_data_host|FullyQualifiedName~uses_transcoding_endpoint" -v minimal
```

Expected before implementation: failures on old host and old `/content/verify` endpoint.

- [x] **Step 3: Update data endpoint implementations**

Change these calls in `LineMessagingClient.cs`:

```csharp
_client.GetAsync($"{_uri}/bot/message/{messageId}/content")
_client.GetAsync($"{_uri}/bot/message/{messageId}/content/verify")
_client.GetAsync($"{_uri}/bot/message/{messageId}/content/preview")
_client.GetAsync($"{_uri}/bot/richmenu/{richMenuId}/content")
_client.PostAsync($"{_uri}/bot/richmenu/{richMenuId}/content", content)
```

to:

```csharp
_client.GetAsync(DataUrl($"/bot/message/{messageId}/content"))
_client.GetAsync(DataUrl($"/bot/message/{messageId}/content/transcoding"))
_client.GetAsync(DataUrl($"/bot/message/{messageId}/content/preview"))
_client.GetAsync(DataUrl($"/bot/richmenu/{richMenuId}/content"))
_client.PostAsync(DataUrl($"/bot/richmenu/{richMenuId}/content"), content)
```

- [x] **Step 4: Run the data-host tests**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~uses_api_data_host|FullyQualifiedName~uses_transcoding_endpoint" -v minimal
```

Expected: all data-host tests pass.

- [x] **Step 5: Commit data endpoint fixes**

Run:

```powershell
git add Line.Messaging\LineMessagingClient.cs Line.Messaging.Tests\LineMessagingClientP0EndpointTests.cs
git commit -m "fix: route LINE data endpoints to api-data host"
```

---

### Task 4: Fix Mark-As-Read Official Endpoint

**Files:**
- Modify: `Line.Messaging/ILineMessagingClient.cs`
- Modify: `Line.Messaging/LineMessagingClient.cs`
- Test: `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs`

- [x] **Step 1: Add failing test for official mark-as-read endpoint**

Append:

```csharp
[Fact]
public async Task Mark_as_read_uses_official_chat_endpoint_and_token_payload()
{
    var handler = new CapturingHttpMessageHandler();
    var client = CreateClient(handler);

    await client.MarkAsReadByTokenAsync("mark-token-123");

    handler.Requests.Should().ContainSingle();
    handler.Requests[0].Method.Should().Be(HttpMethod.Post);
    handler.Requests[0].RequestUri!.ToString()
        .Should().Be("https://api.line.me/v2/bot/chat/markAsRead");

    var body = JObject.Parse(handler.Bodies[0]);
    body["markAsReadToken"]!.Value<string>().Should().Be("mark-token-123");
    body["chatId"].Should().BeNull();
}
```

This test references a method that does not exist yet, so compilation should fail before implementation.

- [x] **Step 2: Run the test and verify compile failure**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~Mark_as_read_uses_official_chat_endpoint_and_token_payload" -v minimal
```

Expected: compile fails because `MarkAsReadByTokenAsync` is not defined.

- [x] **Step 3: Add interface method and obsolete compatibility method**

Modify `ILineMessagingClient.cs` near the existing mark-as-read declaration:

```csharp
Task MarkAsReadByTokenAsync(string markAsReadToken);

[Obsolete("Use MarkAsReadByTokenAsync(markAsReadToken). LINE official API uses markAsReadToken, not chatId.")]
Task MarkAsReadAsync(string chatId);
```

- [x] **Step 4: Implement token-based method**

Modify `LineMessagingClient.cs`:

```csharp
public virtual async Task MarkAsReadByTokenAsync(string markAsReadToken)
{
    var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/chat/markAsRead"));
    request.Content = new StringContent(
        JsonConvert.SerializeObject(new { markAsReadToken }, _jsonSerializerSettings),
        Encoding.UTF8,
        "application/json");
    var response = await _client.SendAsync(request).ConfigureAwait(false);
    await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
}

[Obsolete("Use MarkAsReadByTokenAsync(markAsReadToken). LINE official API uses markAsReadToken, not chatId.")]
public virtual Task MarkAsReadAsync(string chatId)
{
    return MarkAsReadByTokenAsync(chatId);
}
```

- [x] **Step 5: Run the mark-as-read test**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~Mark_as_read_uses_official_chat_endpoint_and_token_payload" -v minimal
```

Expected: pass.

- [x] **Step 6: Commit mark-as-read fix**

Run:

```powershell
git add Line.Messaging\ILineMessagingClient.cs Line.Messaging\LineMessagingClient.cs Line.Messaging.Tests\LineMessagingClientP0EndpointTests.cs
git commit -m "fix: use official LINE mark-as-read token endpoint"
```

---

### Task 5: Fix Rich Menu Batch P0 Endpoints

**Files:**
- Modify: `Line.Messaging/LineMessagingClient.cs`
- Test: `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs`

- [x] **Step 1: Add failing tests for batch progress and validation endpoints**

Append:

```csharp
[Fact]
public async Task Get_rich_menu_batch_progress_uses_progress_query_endpoint()
{
    var handler = new CapturingHttpMessageHandler("""{"phase":"succeeded"}""");
    var client = CreateClient(handler);

    await client.GetRichMenuBatchProgressAsync("request-123");

    handler.Requests[0].Method.Should().Be(HttpMethod.Get);
    handler.Requests[0].RequestUri!.ToString()
        .Should().Be("https://api.line.me/v2/bot/richmenu/progress/batch?requestId=request-123");
}

[Fact]
public async Task Validate_rich_menu_batch_uses_official_validate_batch_endpoint()
{
    var handler = new CapturingHttpMessageHandler();
    var client = CreateClient(handler);

    await client.ValidateRichMenuBatchRequestAsync(new List<RichMenuBatchOperation>());

    handler.Requests[0].Method.Should().Be(HttpMethod.Post);
    handler.Requests[0].RequestUri!.ToString()
        .Should().Be("https://api.line.me/v2/bot/richmenu/validate/batch");
}
```

- [x] **Step 2: Run the tests and verify failure**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~rich_menu_batch" -v minimal
```

Expected before implementation: failures due to `/bot/richmenu/batch/{requestId}` and `/bot/richmenu/batch/validate`.

- [x] **Step 3: Update rich menu batch URLs**

Change:

```csharp
GetStringAsync($"{_uri}/bot/richmenu/batch/{requestId}")
new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/batch/validate")
```

to:

```csharp
GetStringAsync(ApiUrl($"/bot/richmenu/progress/batch?requestId={Uri.EscapeDataString(requestId)}"))
new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/richmenu/validate/batch"))
```

- [x] **Step 4: Run the rich menu batch tests**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~rich_menu_batch" -v minimal
```

Expected: pass.

- [x] **Step 5: Commit rich menu batch fixes**

Run:

```powershell
git add Line.Messaging\LineMessagingClient.cs Line.Messaging.Tests\LineMessagingClientP0EndpointTests.cs
git commit -m "fix: correct LINE rich menu batch endpoints"
```

---

### Task 6: Fix Duplicate `/v2/v2` Insight, Coupon, And Membership Endpoints

**Files:**
- Modify: `Line.Messaging/LineMessagingClient.cs`
- Test: `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs`

- [x] **Step 1: Add failing tests for duplicate-v2 endpoint families**

Append:

```csharp
[Fact]
public async Task Insight_endpoints_do_not_duplicate_v2_segment()
{
    var handler = new CapturingHttpMessageHandler("""{"status":"ready"}""");
    var client = CreateClient(handler);

    await client.GetMessageDeliveryAsync(new DateTime(2026, 7, 2));
    await client.GetFollowerStatisticsAsync(new DateTime(2026, 7, 2));
    await client.GetFriendDemographicsAsync();
    await client.GetUserInteractionStatisticsAsync("request-123");
    await client.GetStatisticsPerUnitAsync("unit one", "20260701", "20260702");
    await client.GetAggregationInfoAsync();
    await client.GetAggregationUnitNameListAsync(10, "next-token");

    handler.Requests.Select(request => request.RequestUri!.ToString()).Should().AllSatisfy(url =>
        url.Should().NotContain("/v2/v2/"));
}

[Fact]
public async Task Coupon_and_membership_endpoints_do_not_duplicate_v2_segment()
{
    var handler = new CapturingHttpMessageHandler("""{}""");
    var client = CreateClient(handler);

    await client.CreateCouponAsync(new CreateCouponRequest());
    await client.CloseCouponAsync("coupon-1");
    await client.GetCouponListAsync();
    await client.GetCouponAsync("coupon-1");
    await client.GetMembershipSubscriptionAsync("user-1");
    await client.GetMembershipUserIdsAsync("membership-1", 100, "next-token");
    await client.GetMembershipPlansAsync();

    handler.Requests.Select(request => request.RequestUri!.ToString()).Should().AllSatisfy(url =>
        url.Should().NotContain("/v2/v2/"));
}
```

- [x] **Step 2: Run the tests and verify failure**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~duplicate_v2_segment" -v minimal
```

Expected before implementation: failures on endpoints currently using `$"{_uri}/v2/bot/..."`.

- [x] **Step 3: Replace duplicate-v2 URL construction**

For these methods, wrap the existing path in `ApiUrl(...)`:

```csharp
GetMessageDeliveryAsync
GetFollowerStatisticsAsync
GetFriendDemographicsAsync
GetUserInteractionStatisticsAsync
GetStatisticsPerUnitAsync
GetAggregationInfoAsync
GetAggregationUnitNameListAsync
CreateCouponAsync
CloseCouponAsync
GetCouponListAsync
GetCouponAsync
GetMembershipSubscriptionAsync
GetMembershipUserIdsAsync
GetMembershipPlansAsync
```

Use this style:

```csharp
ApiUrl($"/v2/bot/insight/followers?date={date:yyyyMMdd}")
```

The helper removes the duplicated `v2` when `_uri` already ends with `/v2`.

- [x] **Step 4: Run duplicate-v2 tests**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~duplicate_v2_segment" -v minimal
```

Expected: pass.

- [x] **Step 5: Commit duplicate-v2 fixes**

Run:

```powershell
git add Line.Messaging\LineMessagingClient.cs Line.Messaging.Tests\LineMessagingClientP0EndpointTests.cs
git commit -m "fix: remove duplicate LINE API version segments"
```

---

### Task 7: Remove Hardcoded LINE Tokens From LineMessagingProcessor

**Files:**
- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`
- Test: `Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs`

- [x] **Step 1: Write failing credential tests**

Create `Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs`:

```csharp
using FluentAssertions;
using LineMessagingProcessor;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessagingProcessorCredentialTests
{
    [Fact]
    public void Processor_source_does_not_contain_literal_bearer_tokens()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "LineMessagingProcessor",
            "LineMessagingProcessorClass.cs"));

        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("Bearer RvnT/");
        source.Should().NotContain("Bearer zBJV+");
        source.Should().NotContain("Bearer PhC1");
        source.Should().NotContain("dB04t89/1O/w1cDnyilFU=");
    }

    [Fact]
    public void Processor_accepts_channel_access_token_through_constructor()
    {
        using var processor = new LineMessagingProcessorClass("test-token");

        processor.Should().NotBeNull();
    }

    [Fact]
    public void Processor_without_token_fails_before_sending_line_request()
    {
        using var processor = new LineMessagingProcessorClass(channelAccessToken: "");

        Func<Task> action = () => processor.SendMessage("user-1", "hello");

        action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LINE channel access token*");
    }
}
```

- [x] **Step 2: Run credential tests and verify failure**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~LineMessagingProcessorCredentialTests" -v minimal
```

Expected before implementation: constructor overload missing and source contains token literals.

- [x] **Step 3: Remove token literals and add token injection**

In `LineMessagingProcessorClass.cs`, replace the whole token region with:

```csharp
private readonly string _channelAccessToken;
```

Replace constructor with:

```csharp
public LineMessagingProcessorClass()
    : this(Environment.GetEnvironmentVariable("LINE_CHANNEL_ACCESS_TOKEN") ?? string.Empty)
{
}

public LineMessagingProcessorClass(string channelAccessToken)
{
    _channelAccessToken = NormalizeBearerToken(channelAccessToken);
    var options = new RestClientOptions("https://api.line.me/v2/bot");
    _restClient = new RestClient(options);
}

private static string NormalizeBearerToken(string channelAccessToken)
{
    if (string.IsNullOrWhiteSpace(channelAccessToken))
    {
        return string.Empty;
    }

    return channelAccessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? channelAccessToken
        : "Bearer " + channelAccessToken;
}

private string GetRequiredChannelAccessToken()
{
    if (string.IsNullOrWhiteSpace(_channelAccessToken))
    {
        throw new InvalidOperationException(
            "LINE channel access token is required. Pass it to LineMessagingProcessorClass or set LINE_CHANNEL_ACCESS_TOKEN.");
    }

    return _channelAccessToken;
}
```

Change both request headers:

```csharp
request.AddHeader("Authorization", GetRequiredChannelAccessToken());
```

- [x] **Step 4: Run credential tests**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore --filter "FullyQualifiedName~LineMessagingProcessorCredentialTests" -v minimal
```

Expected: pass.

- [x] **Step 5: Search for remaining LINE token literals in source**

Run:

```powershell
Select-String -Path 'LineMessagingProcessor\LineMessagingProcessorClass.cs' -Pattern 'Bearer |dB04t89|ChannelAccessToken = "' 
```

Expected: no real token literals. The string `Bearer ` may appear only in normalization logic.

- [x] **Step 6: Commit credential cleanup**

Run:

```powershell
git add LineMessagingProcessor\LineMessagingProcessorClass.cs Line.Messaging.Tests\LineMessagingProcessorCredentialTests.cs
git commit -m "fix: remove hardcoded LINE processor tokens"
```

---

### Task 8: Full Verification And External Review

**Files:**
- Modify: `.ccg/tasks/line-messaging-sdk-p0-fixes/review.md`

- [x] **Step 1: Run focused LINE SDK tests**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal
```

Expected: all `Line.Messaging.Tests` tests pass.

- [x] **Step 2: Run solution build**

Run:

```powershell
dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds with 0 errors. Existing unrelated warnings must be recorded.

- [x] **Step 3: Run source boundary and token searches**

Run:

```powershell
Select-String -Path 'LineMessagingProcessor\LineMessagingProcessorClass.cs' -Pattern 'dB04t89|Bearer [A-Za-z0-9+/=]{20,}'
Select-String -Path 'Line.Messaging\LineMessagingClient.cs' -Pattern '/v2/v2|content/verify|message/markAsRead|richmenu/batch/validate|richmenu/batch/{requestId}'
```

Expected:

- no real token literal matches.
- no old endpoint strings remain.

- [x] **Step 4: Run text format checks for modified text files**

Run a UTF-8 without BOM and LF-only check on:

```text
Line.Messaging/LineMessagingClient.cs
Line.Messaging/ILineMessagingClient.cs
LineMessagingProcessor/LineMessagingProcessorClass.cs
Line.Messaging.Tests/Line.Messaging.Tests.csproj
Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs
docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md
.ccg/tasks/line-messaging-sdk-p0-fixes/task.json
.ccg/tasks/line-messaging-sdk-p0-fixes/requirements.md
```

Expected: no BOM and no LF-only lines.

- [x] **Step 5: Run Gemini and Claude external CCG review**

Run both reviewer backends through `codeagent-wrapper --lite`, using the final diff as context. CCG requires two usable reviewer reports: Gemini and Claude. A single backend is not an acceptable substitute.

```powershell
# Gemini reviewer
$repo = (Get-Location).Path
$env:GEMINI_CLI_TRUST_WORKSPACE = 'true'
$env:Path = 'C:\Users\Administrator\AppData\Roaming\npm;C:\Program Files\nodejs;C:\Users\Administrator\AppData\Local\Programs\Python\Python314\Scripts\;C:\Users\Administrator\AppData\Local\Programs\Python\Python314\;C:\Users\Administrator\AppData\Local\Programs\Python\Launcher\;' + [Environment]::GetEnvironmentVariable('Path','Machine')
$task = @'
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
Review the LINE Messaging SDK P0 fixes. Check official endpoint correctness, backwards compatibility, token handling, tests, and maintainability.
OUTPUT: Critical/Warning/Info/Suggested fixes.
</TASK>
'@
$task | & 'C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe' --lite --backend gemini - $repo

# Claude reviewer
$task = @'
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
Review the LINE Messaging SDK P0 fixes. Check official endpoint correctness, backwards compatibility, token handling, tests, and maintainability.
OUTPUT: Critical/Warning/Info/Suggested fixes.
</TASK>
'@
$task | & 'C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe' --lite --backend claude - $repo
```

Expected: both produce review reports. Do not claim external review is complete unless both backends produce usable findings.

- [x] **Step 6: Fix Critical/Warning findings**

For every external finding:

1. Verify against code and official docs.
2. Fix if valid.
3. Re-run focused tests and build.
4. Re-run external review if a Critical finding was fixed.

- [x] **Step 7: Record review results**

Create `.ccg/tasks/line-messaging-sdk-p0-fixes/review.md` with:

```markdown
# CCG Review - LINE Messaging SDK P0 Fixes

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine

## Gemini

- Session-ID:
- Critical:
- Warning:
- Info:

## Claude

- Session-ID:
- Critical:
- Warning:
- Info:

## Disposition

- Accepted findings:
- Rejected findings with reason:
- Follow-up work not in P0 scope:

## Verification

- dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal:
- dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false:
- token/endpoint searches:
- text encoding check:
```

- [x] **Step 8: Commit review record**

Run:

```powershell
git add .ccg\tasks\line-messaging-sdk-p0-fixes\review.md
git commit -m "chore: record LINE SDK P0 external review"
```

---

## Self-Review Checklist

- [x] Every P0 item in the matrix is covered by a task.
- [x] No P1/P2 implementation is included.
- [x] Every endpoint bug has a failing test before implementation.
- [x] Public compatibility is preserved for old `MarkAsReadAsync(string)`.
- [x] Token cleanup removes literals without forcing ChurchReport-wide refactoring in the same task.
- [x] Final review requires both Gemini and Claude.
