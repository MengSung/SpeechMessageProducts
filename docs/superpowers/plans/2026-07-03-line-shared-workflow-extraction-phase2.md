# LINE Shared Workflow Extraction Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build reusable LINE notification workflow and ASP.NET Core integration projects, then route ChurchReport's payment, text push, and member identity notification paths through the shared workflow.

**Architecture:** `Line.Messaging` remains the official SDK layer. `LineMessagingProcessor` remains the reusable LINE adapter layer. New `LineMessagingProcessor.Workflows` owns product-friendly notification request/result models and `SendAsync` / `SendOrThrowAsync`; new `LineMessagingProcessor.AspNetCore` owns DI/options integration. ChurchReport keeps CRM, payment, donation, member, controller, view, and LIFF behavior.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, `Line.Messaging`, `LineMessagingProcessor`, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options.

---

## File Map

### Create

- `LineMessagingProcessor.Workflows/LineMessagingProcessor.Workflows.csproj`
- `LineMessagingProcessor.Workflows/LineNotificationRecipientKind.cs`
- `LineMessagingProcessor.Workflows/LineNotificationStatus.cs`
- `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs`
- `LineMessagingProcessor.Workflows/LineNotificationContent.cs`
- `LineMessagingProcessor.Workflows/LineNotificationRequest.cs`
- `LineMessagingProcessor.Workflows/LineNotificationResult.cs`
- `LineMessagingProcessor.Workflows/LineNotificationException.cs`
- `LineMessagingProcessor.Workflows/ILineNotificationWorkflow.cs`
- `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs`
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj`
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs`
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs`
- `LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj`
- `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs`
- `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj`
- `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs`
- `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs`
- `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs`
- `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/MemberLineProfileWorkflowTests.cs`

### Modify

- `ChurchReport.sln`
- `ChurchReport/ChurchReport.csproj`
- `ChurchReport/Services/PaymentNotificationService.cs`
- `ChurchReport/Tools/PushUtility.cs`
- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Program.cs` or the current DI bootstrap file if LINE services are registered elsewhere

### Do Not Modify In This Phase

- `LinePayCSharp/`
- LIFF `.cshtml` / JavaScript
- Payment provider core projects
- CRM query semantics
- ChurchReport views/routes, except constructor/DI plumbing required by this plan

---

## Batch 1: Shared Workflow Core And ASP.NET Core Integration

### Task 1: Create Workflow Project And Domain Models

**Files:**
- Create: `LineMessagingProcessor.Workflows/LineMessagingProcessor.Workflows.csproj`
- Create: `LineMessagingProcessor.Workflows/LineNotificationRecipientKind.cs`
- Create: `LineMessagingProcessor.Workflows/LineNotificationStatus.cs`
- Create: `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs`
- Create: `LineMessagingProcessor.Workflows/LineNotificationContent.cs`
- Create: `LineMessagingProcessor.Workflows/LineNotificationRequest.cs`
- Create: `LineMessagingProcessor.Workflows/LineNotificationResult.cs`
- Create: `LineMessagingProcessor.Workflows/LineNotificationException.cs`

- [ ] **Step 1: Create project file**

Create `LineMessagingProcessor.Workflows/LineMessagingProcessor.Workflows.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
    <AssemblyName>LineMessagingProcessor.Workflows</AssemblyName>
    <RootNamespace>LineMessagingProcessor.Workflows</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create recipient kind enum**

Create `LineMessagingProcessor.Workflows/LineNotificationRecipientKind.cs`:

```csharp
namespace LineMessagingProcessor.Workflows;

public enum LineNotificationRecipientKind
{
    User,
    Users,
    Group,
    Room
}
```

- [ ] **Step 3: Create status enum**

Create `LineMessagingProcessor.Workflows/LineNotificationStatus.cs`:

```csharp
namespace LineMessagingProcessor.Workflows;

public enum LineNotificationStatus
{
    Succeeded,
    ValidationFailed,
    ProviderRejected,
    ProviderUnavailable,
    UnexpectedError
}
```

- [ ] **Step 4: Create recipient model**

Create `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs`:

```csharp
namespace LineMessagingProcessor.Workflows;

public sealed class LineNotificationRecipient
{
    private LineNotificationRecipient(LineNotificationRecipientKind kind, IReadOnlyList<string> ids)
    {
        Kind = kind;
        Ids = ids;
    }

    public LineNotificationRecipientKind Kind { get; }

    public IReadOnlyList<string> Ids { get; }

    public static LineNotificationRecipient User(string lineUserId)
        => new(LineNotificationRecipientKind.User, new[] { lineUserId });

    public static LineNotificationRecipient Users(IEnumerable<string> lineUserIds)
        => new(LineNotificationRecipientKind.Users, lineUserIds?.ToArray() ?? Array.Empty<string>());

    public static LineNotificationRecipient Group(string groupId)
        => new(LineNotificationRecipientKind.Group, new[] { groupId });

    public static LineNotificationRecipient Room(string roomId)
        => new(LineNotificationRecipientKind.Room, new[] { roomId });

    public string? PrimaryId => Ids.Count == 0 ? null : Ids[0];
}
```

- [ ] **Step 5: Create content model**

Create `LineMessagingProcessor.Workflows/LineNotificationContent.cs`:

```csharp
using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

public sealed class LineNotificationContent
{
    private LineNotificationContent(string? text, IReadOnlyList<ISendMessage>? sdkMessages)
    {
        Text = text;
        SdkMessages = sdkMessages;
    }

    public string? Text { get; }

    public IReadOnlyList<ISendMessage>? SdkMessages { get; }

    public static LineNotificationContent TextMessage(string message)
        => new(message, null);

    public static LineNotificationContent SdkMessagesList(IReadOnlyList<ISendMessage> messages)
        => new(null, messages);
}
```

- [ ] **Step 6: Create request model**

Create `LineMessagingProcessor.Workflows/LineNotificationRequest.cs`:

```csharp
namespace LineMessagingProcessor.Workflows;

public sealed class LineNotificationRequest
{
    public required LineNotificationRecipient Recipient { get; init; }

    public required LineNotificationContent Content { get; init; }

    public string? RetryKey { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
```

- [ ] **Step 7: Create result model**

Create `LineMessagingProcessor.Workflows/LineNotificationResult.cs`:

```csharp
namespace LineMessagingProcessor.Workflows;

public sealed class LineNotificationResult
{
    private LineNotificationResult(
        bool succeeded,
        LineNotificationStatus status,
        LineNotificationRecipient? recipient,
        string? retryKey,
        string? errorCode,
        string? errorMessage,
        string? providerResponse,
        IReadOnlyDictionary<string, string> metadata)
    {
        Succeeded = succeeded;
        Status = status;
        Recipient = recipient;
        RetryKey = retryKey;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderResponse = providerResponse;
        Metadata = metadata;
    }

    public bool Succeeded { get; }

    public LineNotificationStatus Status { get; }

    public LineNotificationRecipient? Recipient { get; }

    public string? RetryKey { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public string? ProviderResponse { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static LineNotificationResult Success(LineNotificationRequest request)
        => new(true, LineNotificationStatus.Succeeded, request.Recipient, request.RetryKey, null, null, null, request.Metadata);

    public static LineNotificationResult Failure(
        LineNotificationRequest? request,
        LineNotificationStatus status,
        string errorCode,
        string errorMessage,
        string? providerResponse = null)
        => new(false, status, request?.Recipient, request?.RetryKey, errorCode, errorMessage, providerResponse, request?.Metadata ?? new Dictionary<string, string>());
}
```

- [ ] **Step 8: Create exception model**

Create `LineMessagingProcessor.Workflows/LineNotificationException.cs`:

```csharp
namespace LineMessagingProcessor.Workflows;

public sealed class LineNotificationException : Exception
{
    public LineNotificationException(LineNotificationResult result)
        : base(result.ErrorMessage ?? "LINE notification failed.")
    {
        Result = result;
    }

    public LineNotificationResult Result { get; }
}
```

- [ ] **Step 9: Add workflow project to solution**

Run:

```powershell
dotnet sln ChurchReport.sln add LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj
```

Expected: solution adds the project.

- [ ] **Step 10: Commit Batch 1 domain models**

```powershell
git add ChurchReport.sln LineMessagingProcessor.Workflows
git commit -m "feat: add LINE notification workflow models"
```

### Task 2: Implement Workflow With TDD

**Files:**
- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`
- Create: `LineMessagingProcessor.Workflows/ILineNotificationWorkflow.cs`
- Create: `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs`
- Create: `LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj`
- Create: `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs`

- [ ] **Step 1: Create test project**

Create `LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj`:

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
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write failing workflow tests**

Create `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs`:

```csharp
using System.Net;
using System.Text;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using Xunit;

namespace LineMessagingProcessor.Workflows.Tests;

public sealed class LineNotificationWorkflowTests
{
    [Fact]
    public async Task SendAsync_text_user_notification_posts_to_push_endpoint()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, "{}");
        var client = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var workflow = new LineNotificationWorkflow(new LineMessagingProcessorClass(client));

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U123"),
            Content = LineNotificationContent.TextMessage("hello")
        });

        result.Succeeded.Should().BeTrue();
        result.Status.Should().Be(LineNotificationStatus.Succeeded);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Bodies[0].Should().Contain("\"to\":\"U123\"");
        handler.Bodies[0].Should().Contain("\"text\":\"hello\"");
    }

    [Fact]
    public async Task SendAsync_blank_user_returns_validation_failed_without_http_call()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, "{}");
        var client = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var workflow = new LineNotificationWorkflow(new LineMessagingProcessorClass(client));

        var result = await workflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(" "),
            Content = LineNotificationContent.TextMessage("hello")
        });

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineNotificationStatus.ValidationFailed);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendOrThrowAsync_provider_rejection_throws_notification_exception()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.BadRequest, """{"message":"bad request"}""");
        var client = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var workflow = new LineNotificationWorkflow(new LineMessagingProcessorClass(client));

        Func<Task> action = () => workflow.SendOrThrowAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User("U123"),
            Content = LineNotificationContent.TextMessage("hello")
        });

        var exception = await action.Should().ThrowAsync<LineNotificationException>();
        exception.Which.Result.Status.Should().Be(LineNotificationStatus.ProviderRejected);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public CapturingHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
```

- [ ] **Step 3: Run tests and verify RED**

Run:

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal
```

Expected: compile failure because `ILineNotificationWorkflow` and `LineNotificationWorkflow` do not exist.

- [ ] **Step 4: Add workflow interface**

Create `LineMessagingProcessor.Workflows/ILineNotificationWorkflow.cs`:

```csharp
namespace LineMessagingProcessor.Workflows;

public interface ILineNotificationWorkflow
{
    Task<LineNotificationResult> SendAsync(LineNotificationRequest request, CancellationToken cancellationToken = default);

    Task SendOrThrowAsync(LineNotificationRequest request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Add workflow implementation**

Create `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs`:

```csharp
using Line.Messaging;
using LineMessagingProcessor;

namespace LineMessagingProcessor.Workflows;

public sealed class LineNotificationWorkflow : ILineNotificationWorkflow
{
    private readonly LineMessagingProcessorClass _processor;

    public LineNotificationWorkflow(LineMessagingProcessorClass processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    public async Task<LineNotificationResult> SendAsync(LineNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (validation != null)
        {
            return validation;
        }

        try
        {
            var recipientId = request.Recipient.PrimaryId!;
            var messages = ResolveMessages(request.Content);

            if (!string.IsNullOrWhiteSpace(request.RetryKey) && request.Content.Text != null && request.Recipient.Kind == LineNotificationRecipientKind.User)
            {
                await _processor.SendReliableMessageAsync(recipientId, request.Content.Text, request.RetryKey).ConfigureAwait(false);
            }
            else if (messages.Count == 1 && request.Content.Text != null && request.Recipient.Kind == LineNotificationRecipientKind.User)
            {
                await _processor.SendMessage(recipientId, request.Content.Text).ConfigureAwait(false);
            }
            else
            {
                await _processor.SendMessagesAsync(recipientId, messages, request.RetryKey).ConfigureAwait(false);
            }

            return LineNotificationResult.Success(request);
        }
        catch (ArgumentException ex)
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.ValidationFailed, "validation_failed", ex.Message);
        }
        catch (LineResponseException ex)
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.ProviderRejected, "provider_rejected", ex.Message, ex.ToString());
        }
        catch (HttpRequestException ex)
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.ProviderUnavailable, "provider_unavailable", ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.ProviderUnavailable, "provider_unavailable", ex.Message);
        }
        catch (Exception ex)
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.UnexpectedError, "unexpected_error", ex.Message);
        }
    }

    public async Task SendOrThrowAsync(LineNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineNotificationException(result);
        }
    }

    private static LineNotificationResult? Validate(LineNotificationRequest request)
    {
        if (request == null)
        {
            return LineNotificationResult.Failure(null, LineNotificationStatus.ValidationFailed, "request_required", "Request is required.");
        }

        if (request.Recipient == null)
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.ValidationFailed, "recipient_required", "Recipient is required.");
        }

        if (request.Recipient.Ids.Count == 0 || request.Recipient.Ids.Any(string.IsNullOrWhiteSpace))
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.ValidationFailed, "recipient_id_required", "Recipient id is required.");
        }

        if (request.Content == null)
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.ValidationFailed, "content_required", "Content is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Content.Text) && (request.Content.SdkMessages == null || request.Content.SdkMessages.Count == 0))
        {
            return LineNotificationResult.Failure(request, LineNotificationStatus.ValidationFailed, "content_empty", "Notification content is required.");
        }

        return null;
    }

    private static IReadOnlyList<ISendMessage> ResolveMessages(LineNotificationContent content)
    {
        if (content.SdkMessages != null)
        {
            return content.SdkMessages;
        }

        return new List<ISendMessage> { new TextMessage(content.Text!) };
    }
}
```

- [ ] **Step 6: Add public processor message-list adapter**

Add this method to `LineMessagingProcessor/LineMessagingProcessorClass.cs` near the existing `SendMessage` methods:

```csharp
public async Task SendMessagesAsync(string userId, IReadOnlyList<ISendMessage> messages, string? retryKey = null)
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        throw new ArgumentException("userId is required.", nameof(userId));
    }

    if (messages == null || messages.Count == 0)
    {
        throw new ArgumentException("messages are required.", nameof(messages));
    }

    await _lineMessagingClient.PushMessageAsync(userId, messages, retryKey).ConfigureAwait(false);
}
```

This keeps the workflow on a public processor contract and avoids reflection or direct access to processor internals.

- [ ] **Step 7: Run tests and verify GREEN**

Run:

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal
```

Expected: all workflow tests pass.

- [ ] **Step 8: Add test project to solution**

Run:

```powershell
dotnet sln ChurchReport.sln add LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj
```

- [ ] **Step 9: Commit workflow implementation**

```powershell
git add ChurchReport.sln LineMessagingProcessor\LineMessagingProcessorClass.cs LineMessagingProcessor.Workflows LineMessagingProcessor.Workflows.Tests
git commit -m "feat: add LINE notification workflow"
```

### Task 3: Add ASP.NET Core Integration

**Files:**
- Create: `LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj`
- Create: `LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs`
- Create: `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs`
- Create: `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj`
- Create: `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Create ASP.NET Core integration project**

Create `LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
    <AssemblyName>LineMessagingProcessor.AspNetCore</AssemblyName>
    <RootNamespace>LineMessagingProcessor.AspNetCore</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create options**

Create `LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs`:

```csharp
namespace LineMessagingProcessor.AspNetCore;

public sealed class LineMessagingProcessorOptions
{
    public string ChannelAccessToken { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.line.me/v2";
}
```

- [ ] **Step 3: Write failing DI tests**

Create `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj`:

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
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\LineMessagingProcessor.AspNetCore\LineMessagingProcessor.AspNetCore.csproj" />
  </ItemGroup>
</Project>
```

Create `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs`:

```csharp
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLineMessagingProcessor_registers_client_processor_and_workflow()
    {
        var services = new ServiceCollection();

        services.AddLineMessagingProcessor(options =>
        {
            options.ChannelAccessToken = "test-token";
            options.ApiBaseUrl = "https://api.line.me/v2";
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<LineMessagingClient>().Should().NotBeNull();
        provider.GetRequiredService<LineMessagingProcessorClass>().Should().NotBeNull();
        provider.GetRequiredService<ILineNotificationWorkflow>().Should().NotBeNull();
    }
}
```

- [ ] **Step 4: Run tests and verify RED**

Run:

```powershell
dotnet test LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal
```

Expected: compile failure because `AddLineMessagingProcessor` does not exist.

- [ ] **Step 5: Add DI extension**

Create `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs`:

```csharp
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LineMessagingProcessor.AspNetCore;

public static class LineMessagingProcessorServiceCollectionExtensions
{
    public static IServiceCollection AddLineMessagingProcessor(
        this IServiceCollection services,
        Action<LineMessagingProcessorOptions> configure)
    {
        services.Configure(configure);

        services.AddHttpClient("LineMessagingProcessor");

        services.AddTransient(provider =>
        {
            var options = provider.GetRequiredService<IOptions<LineMessagingProcessorOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ChannelAccessToken))
            {
                throw new InvalidOperationException("LineMessagingProcessorOptions.ChannelAccessToken is required.");
            }

            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("LineMessagingProcessor");
            return new LineMessagingClient(httpClient, options.ChannelAccessToken, options.ApiBaseUrl);
        });

        services.AddTransient<LineMessagingProcessorClass>();
        services.AddTransient<ILineNotificationWorkflow, LineNotificationWorkflow>();

        return services;
    }
}
```

- [ ] **Step 6: Run DI tests and verify GREEN**

Run:

```powershell
dotnet test LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal
```

Expected: all DI tests pass.

- [ ] **Step 7: Add projects to solution**

Run:

```powershell
dotnet sln ChurchReport.sln add LineMessagingProcessor.AspNetCore\LineMessagingProcessor.AspNetCore.csproj
dotnet sln ChurchReport.sln add LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj
```

- [ ] **Step 8: Commit ASP.NET Core integration**

```powershell
git add ChurchReport.sln LineMessagingProcessor.AspNetCore LineMessagingProcessor.AspNetCore.Tests
git commit -m "feat: add LINE processor ASP.NET Core integration"
```

---

## Batch 2: Payment And Donation Notification Adoption

### Task 4: Route PaymentNotificationService Through Workflow

**Files:**
- Modify: `ChurchReport/ChurchReport.csproj`
- Modify: `ChurchReport/Services/PaymentNotificationService.cs`
- Test: `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs`

- [ ] **Step 1: Add ChurchReport references**

Run:

```powershell
dotnet add ChurchReport\ChurchReport.csproj reference LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj
dotnet add ChurchReport\ChurchReport.csproj reference LineMessagingProcessor.AspNetCore\LineMessagingProcessor.AspNetCore.csproj
```

- [ ] **Step 2: Write failing tests for payment workflow dependency**

Create `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs`:

```csharp
using ChurchReport.Services;
using FluentAssertions;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class PaymentNotificationServiceWorkflowTests
{
    [Fact]
    public void Constructor_requires_line_notification_workflow()
    {
        Action action = () => new PaymentNotificationService(
            NullLogger<PaymentNotificationService>.Instance,
            new PaymentMessageBuilder(),
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance),
            lineNotificationWorkflow: null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("lineNotificationWorkflow");
    }

    [Fact]
    public void SendLineMessage_uses_shared_workflow_with_retry_key()
    {
        var workflow = new CapturingWorkflow();
        var service = new PaymentNotificationService(
            NullLogger<PaymentNotificationService>.Instance,
            new PaymentMessageBuilder(),
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance),
            workflow);

        service.SendLineMessage("U123", "paid", "retry-1");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("U123");
        workflow.Requests[0].Content.Text.Should().Be("paid");
        workflow.Requests[0].RetryKey.Should().Be("retry-1");
    }

    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        public List<LineNotificationRequest> Requests { get; } = new();

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(LineNotificationResult.Success(request));
        }

        public Task SendOrThrowAsync(LineNotificationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 3: Run tests and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter PaymentNotificationServiceWorkflowTests -v minimal
```

Expected: compile failure because `PaymentNotificationService` does not accept `ILineNotificationWorkflow`.

- [ ] **Step 4: Modify PaymentNotificationService constructor**

Update `ChurchReport/Services/PaymentNotificationService.cs`:

```csharp
using LineMessagingProcessor.Workflows;
```

Add field:

```csharp
private readonly ILineNotificationWorkflow _lineNotificationWorkflow;
```

Update constructor:

```csharp
public PaymentNotificationService(
    ILogger<PaymentNotificationService> logger,
    PaymentMessageBuilder messageBuilder,
    PaymentFeeTypeHelper feeTypeHelper,
    ILineNotificationWorkflow lineNotificationWorkflow)
{
    _logger = logger;
    _messageBuilder = messageBuilder;
    _feeTypeHelper = feeTypeHelper;
    _lineNotificationWorkflow = lineNotificationWorkflow ?? throw new ArgumentNullException(nameof(lineNotificationWorkflow));
}
```

- [ ] **Step 5: Replace SendLineMessage body**

Replace `SendLineMessage(string lineId, string message, string? retryKey)` body with:

```csharp
public void SendLineMessage(string lineId, string message, string? retryKey)
{
    try
    {
        _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(lineId),
            Content = LineNotificationContent.TextMessage(message),
            RetryKey = retryKey
        }).GetAwaiter().GetResult();

        _logger.LogInformation($"SendLineMessage: sent - LineId: {lineId}, RetryKey: {retryKey ?? "<none>"}");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"SendLineMessage: failed - LineId: {lineId}, RetryKey: {retryKey ?? "<none>"}");
        throw;
    }
}
```

- [ ] **Step 6: Run focused tests and verify GREEN**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter PaymentNotificationServiceWorkflowTests -v minimal
```

Expected: payment workflow tests pass.

- [ ] **Step 7: Commit payment notification adoption**

```powershell
git add ChurchReport\ChurchReport.csproj ChurchReport\Services\PaymentNotificationService.cs ChurchReport.MemberInfo.Tests\LineSharedWorkflow\PaymentNotificationServiceWorkflowTests.cs
git commit -m "feat: route payment LINE notifications through shared workflow"
```

---

## Batch 3: General PushUtility Text Notification Adoption

### Task 5: Make PushUtility A Workflow-Backed Text Wrapper

**Files:**
- Modify: `ChurchReport/Tools/PushUtility.cs`
- Test: `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs`

- [ ] **Step 1: Write failing PushUtility workflow test**

Create `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs`:

```csharp
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class PushUtilityWorkflowTests
{
    [Fact]
    public async Task SendMessage_routes_text_through_shared_workflow_when_workflow_is_provided()
    {
        var workflow = new CapturingWorkflow();
        var utility = new PushUtility(new LineMessagingClient("test-token"), workflow);

        await utility.SendMessage("U123", "hello");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.PrimaryId.Should().Be("U123");
        workflow.Requests[0].Content.Text.Should().Be("hello");
    }

    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        public List<LineNotificationRequest> Requests { get; } = new();

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(LineNotificationResult.Success(request));
        }

        public Task SendOrThrowAsync(LineNotificationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Run test and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter PushUtilityWorkflowTests -v minimal
```

Expected: compile failure because `PushUtility(LineMessagingClient, ILineNotificationWorkflow)` does not exist.

- [ ] **Step 3: Add optional workflow constructor to PushUtility**

In `ChurchReport/Tools/PushUtility.cs`, add:

```csharp
using LineMessagingProcessor.Workflows;
```

Add field:

```csharp
private readonly ILineNotificationWorkflow? _lineNotificationWorkflow;
```

Update constructors:

```csharp
public PushUtility(LineMessagingClient LineMessagingClient)
    : this(LineMessagingClient, null)
{
}

public PushUtility(LineMessagingClient LineMessagingClient, ILineNotificationWorkflow? lineNotificationWorkflow)
{
    this.m_LineMessagingClient = LineMessagingClient ?? throw new ArgumentNullException(nameof(LineMessagingClient));
    _lineNotificationWorkflow = lineNotificationWorkflow;
}
```

- [ ] **Step 4: Route text SendMessage through workflow when available**

In `PushUtility.SendMessage(string UserId, string Message)`, replace the direct SDK call at the top of the method with:

```csharp
if (_lineNotificationWorkflow != null)
{
    await _lineNotificationWorkflow.SendAsync(new LineNotificationRequest
    {
        Recipient = LineNotificationRecipient.User(UserId),
        Content = LineNotificationContent.TextMessage(Message)
    });
    return;
}
```

Keep the existing direct `LineMessagingClient` fallback below this block so older construction paths keep existing behavior.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter PushUtilityWorkflowTests -v minimal
```

Expected: PushUtility workflow tests pass.

- [ ] **Step 6: Commit PushUtility adoption**

```powershell
git add ChurchReport\Tools\PushUtility.cs ChurchReport.MemberInfo.Tests\LineSharedWorkflow\PushUtilityWorkflowTests.cs
git commit -m "feat: route text PushUtility notifications through shared workflow"
```

---

## Batch 4: LINE Binding And Member Identity Adoption

### Task 6: Route Member Profile Lookup Through Processor

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`
- Test: `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/MemberLineProfileWorkflowTests.cs`

- [ ] **Step 1: Locate direct profile lookup**

Run:

```powershell
rg -n "new Line\\.Messaging\\.LineMessagingClient|GetUserProfileAsync" ChurchReport\Controllers\MemberInfoController.cs
```

Expected: find direct `LineMessagingClient` construction and `GetUserProfileAsync(lineId)`.

- [ ] **Step 2: Write failing test for adapter seam**

Create `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/MemberLineProfileWorkflowTests.cs`:

```csharp
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class MemberLineProfileWorkflowTests
{
    [Fact]
    public async Task Processor_GetUserProfileAsync_rejects_blank_line_id_before_http()
    {
        var processor = new LineMessagingProcessorClass(new LineMessagingClient("test-token"));

        Func<Task> action = () => processor.GetUserProfileAsync(" ");

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("UserId");
    }
}
```

This test locks the shared processor behavior used by MemberInfo before the controller is changed.

- [ ] **Step 3: Run focused test**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter MemberLineProfileWorkflowTests -v minimal
```

Expected: test passes if processor behavior already exists. If it fails, fix `LineMessagingProcessorClass.GetUserProfileAsync` validation before touching the controller.

- [ ] **Step 4: Replace direct client construction in MemberInfoController**

Change the target method so it uses:

```csharp
var processor = new LineMessagingProcessorClass(token);
var profile = await processor.GetUserProfileAsync(lineId);
```

Do not move CRM lookup, authorization, session, LIFF, view, or route logic.

- [ ] **Step 5: Run member tests**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal
```

Expected: all member info tests pass.

- [ ] **Step 6: Commit member profile adoption**

```powershell
git add ChurchReport\Controllers\MemberInfoController.cs ChurchReport.MemberInfo.Tests\LineSharedWorkflow\MemberLineProfileWorkflowTests.cs
git commit -m "feat: route member LINE profile lookup through processor"
```

---

## Final Validation And Review

### Task 7: Run Full Validation

- [ ] **Step 1: Run relevant tests**

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal
dotnet test LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal
```

Expected: all tests pass.

- [ ] **Step 2: Run solution build**

```powershell
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Run boundary scan**

```powershell
rg -n "ChurchReport|Microsoft\\.Xrm|IOrganizationService|\\bEntity\\b|Controller|IActionResult|DbContext" LineMessagingProcessor.Workflows LineMessagingProcessor.AspNetCore --glob "*.cs" --glob "*.csproj"
```

Expected: no matches except harmless namespace strings if explicitly reviewed and documented.

- [ ] **Step 4: Run direct LINE client surface scan**

```powershell
rg -n "new LineMessagingClient|new Line\\.Messaging\\.LineMessagingClient|GetUserProfileAsync|PushMessageAsync" ChurchReport --glob "*.cs"
```

Expected: direct usages are reduced in the Batch 2-4 target files. Remaining hits are documented as deferred slices.

- [ ] **Step 5: Check touched text file encoding**

Run a byte-level UTF-8 without BOM + CRLF check on every touched `.cs`, `.csproj`, `.sln`, `.md`, and `.json` file.

Expected: every touched text file reports UTF-8 without BOM and no LF-only lines.

- [ ] **Step 6: Run Gemini and Claude review**

Use both reviewers on the final diff:

```powershell
$repo = (Get-Location).Path
$diff = git diff HEAD~6..HEAD
$task = @"
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
Review LINE shared workflow extraction Phase 2.
Check reusable boundaries, ChurchReport coupling, tests, DI, error handling, and regressions.
DIFF:
$diff
</TASK>
OUTPUT: Critical/Warning/Info review report
"@
$env:GEMINI_CLI_TRUST_WORKSPACE='true'
$task | & "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --lite --backend gemini - $repo
```

```powershell
$repo = (Get-Location).Path
$diff = git diff HEAD~6..HEAD
$task = @"
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
Review LINE shared workflow extraction Phase 2.
Check reusable boundaries, ChurchReport coupling, tests, DI, error handling, and regressions.
DIFF:
$diff
</TASK>
OUTPUT: Critical/Warning/Info review report
"@
$task | & "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --lite --backend claude - $repo
```

Write combined findings to `.ccg/tasks/line-shared-workflow-extraction-phase2/review.md`.

- [ ] **Step 7: Commit review and task state**

Update `.ccg/tasks/line-shared-workflow-extraction-phase2/task.json` to `completed`, add `review.md`, and commit:

```powershell
git add .ccg\tasks\line-shared-workflow-extraction-phase2
git commit -m "chore: record LINE shared workflow extraction review"
```

## Self-Review Checklist

- Spec coverage: Batch 1 covers shared core and ASP.NET Core integration; Batch 2 covers payment/donation notification; Batch 3 covers text `PushUtility`; Batch 4 covers member identity/profile.
- Boundary: no shared project may reference ChurchReport, CRM, controllers, DbContext, or payment projects.
- TDD: every production change has a focused failing test first.
- Scope: Flex, Template, RichMenu, LIFF, broad official API expansion, and LinePay are explicitly deferred.
- Maintainability: public ChurchReport APIs are preserved where possible; shared models are product-friendly and do not force all future products to understand SDK message classes.
