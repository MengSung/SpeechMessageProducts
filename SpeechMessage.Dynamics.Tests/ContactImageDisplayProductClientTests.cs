// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ContactImageDisplayProductClientTests.cs
// 目的：以 RED 測試鎖定 P7.4 MemberInfo 完整聯絡人圖片顯示 capability 的封閉 operation、
//       三分支回應 union 與 ProductClient 派送契約。
//
// 安全與生命週期：本檔所有替身均只保存單一測試呼叫的 immutable request 參考，沒有 CRM、HTTP、
// Session、快取、背景工作、計時器或可釋放資源。每個測試都以可辨識但非真實的資料驗證輸出
// byte array 是 caller-owned copy，避免 A/B 使用者或下一個 request 重用前一次的 mutable image buffer。
// ============================================================================

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P7.4 完整聯絡人頭像回應只能走 deployment-owned 的 display capability。
/// 此 RED suite 保護 image、LINE redirect 與 default avatar 是同一個 closed union 的三個互斥結果；
/// 它不得攜帶 CRM <c>Entity</c>、LINE credential、URL 以外的 raw profile、Session、快取項目或連線狀態。
/// </summary>
public sealed class ContactImageDisplayProductClientTests
{
    private const string DisplayOperationId = "memberinfo.contact.retrieve.image.display";
    private const string DisplayResponseKindName = "ContactImageDisplay";

    /// <summary>
    /// 保護新的 display read 有獨立 operation ID、read-only registry policy 與唯一 response discriminator。
    /// 故障注入是目前 production 尚未宣告這個 operation；決定性斷言是任何未來實作不得重用 image-only
    /// operation，也不得讓 caller 選擇 CRM entity、attribute、URL、profile 或其他 transport 路由資料。
    /// </summary>
    [Fact]
    public void Display_operation_has_an_independent_closed_registry_definition()
    {
        var field = typeof(OperationIds).GetField(
            "MemberInfoContactRetrieveImageDisplay",
            BindingFlags.Public | BindingFlags.Static);

        field.Should().NotBeNull("P7.4 必須有獨立的 server-owned operation ID，而不是重用 image-only capability");
        field!.GetRawConstantValue().Should().Be(DisplayOperationId);

        Package01OperationRegistry.TryGet(DisplayOperationId, out var definition).Should().BeTrue();
        definition!.OperationKind.Should().Be("read");
        definition.IdempotencyClass.Should().Be("read-only");
        definition.ResponseKind.ToString().Should().Be(DisplayResponseKindName);
        definition.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required,
            parameter.EncodingContext
        })
            .Should()
            .BeEquivalentTo(
                [
                    new
                    {
                        Name = "contactId",
                        Type = "guid",
                        Required = true,
                        EncodingContext = "odata-uri-segment"
                    }
                ],
                options => options.WithStrictOrdering());
    }

    /// <summary>
    /// 保護 display response union 的 image defensive copy 與三個互斥 branch。
    /// 故障注入依序為 caller 在 factory 後覆寫來源 array、呼叫端覆寫取得的 array，以及 envelope 同時帶入
    /// image-only 與 display branch；決定性斷言是 image 維持原始內容，且多 branch 會在發佈前 fail closed。
    /// </summary>
    [Fact]
    public void Display_response_union_defensively_copies_image_bytes_and_rejects_multiple_branches()
    {
        var displayType = RequireDisplayResponseType();
        var source = CreateValidOnePixelPng();
        var image = InvokeFactory(displayType, "ForImage", source, ContactImageMediaKind.Png);
        source[0] = 0;

        image.GetType().GetProperty("Kind")!.GetValue(image)!.ToString().Should().Be("Image");
        var firstCopy = InvokeImageGetter(image);
        firstCopy[0] = 0;
        InvokeImageGetter(image).Should().Equal(CreateValidOnePixelPng());

        var redirect = InvokeFactory(displayType, "ForLineRedirect", "https://profile.line-scdn.net/avatar.png");
        redirect.GetType().GetProperty("Kind")!.GetValue(redirect)!.ToString().Should().Be("LineRedirect");

        var avatar = InvokeFactory(displayType, "ForDefaultAvatar", 2);
        avatar.GetType().GetProperty("Kind")!.GetValue(avatar)!.ToString().Should().Be("DefaultAvatar");

        var constructor = typeof(OperationResponseData).GetConstructors()
            .SingleOrDefault(candidate => candidate.GetParameters()
                .Any(parameter => string.Equals(parameter.Name, "contactImageDisplay", StringComparison.Ordinal)));
        constructor.Should().NotBeNull(
            "closed envelope 必須有明確 display branch，才能在 transport 邊界拒絕混合結果");

        var arguments = CreateConstructorArguments(constructor!, image);
        var constructMixedBranches = () => constructor!.Invoke(arguments);

        constructMixedBranches.Should().Throw<TargetInvocationException>(
            "image-only branch 與 display branch 同時出現時，封閉 union 必須 fail closed");
    }

    /// <summary>
    /// 保護 stateless Package03 ProductClient 只派送固定 display operation，並轉送 caller 的取消權杖。
    /// 故障注入使用不含任何外部資源的 executor；決定性斷言是 request 只包含 contact locator、profile/workload
    /// 都會被保留在 deployment-owned typed request，而回傳的 image branch 仍是 defensive copy，沒有 legacy fallback。
    /// </summary>
    [Fact]
    public async Task Product_client_dispatches_the_fixed_display_operation_and_returns_a_defensive_result()
    {
        var displayType = RequireDisplayResponseType();
        var displayResponse = InvokeFactory(displayType, "ForImage", CreateValidOnePixelPng(), ContactImageMediaKind.Png);
        var operationResponse = CreateDisplayOperationResponse(displayResponse);
        var executor = new RecordingExecutor(OperationExecutionResult.Success(operationResponse));
        var client = new Package03SpecialResourceClient(
            executor,
            NullLogger<Package03SpecialResourceClient>.Instance);
        var method = typeof(IPackage03SpecialResourceClient).GetMethod(
            "RetrieveContactImageDisplayAsync",
            BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull(
            "P7.4 必須使用獨立 typed method，不能把 LINE redirect 或 avatar 回填到 image-only 方法");
        var request = CreateDisplayRequest(method!);
        using var cancellationSource = new CancellationTokenSource();

        var task = method!.Invoke(client, [request, cancellationSource.Token])
            .Should().BeAssignableTo<Task>().Subject;
        await task;

        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.CapabilityOperationId.Should().Be(DisplayOperationId);
        executor.LastRequest.ProfileAlias.Should().Be("crm91");
        executor.LastRequest.WorkloadSubjectId.Should().Be("churchreport-memberinfo");
        executor.LastRequest.IdempotencyKey.Should().BeNull();
        executor.LastRequest.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["contactId"] = Guid.Parse("aaaaaaaa-0000-1111-2222-bbbbbbbbbbbb")
        });
        executor.ReceivedCancellationToken.Should().Be(cancellationSource.Token);

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        result.GetType().GetProperty("Kind")!.GetValue(result)!.ToString().Should().Be("Image");
        var returnedBytes = InvokeImageGetter(result);
        returnedBytes[0] = 0;
        InvokeImageGetter(result).Should().Equal(CreateValidOnePixelPng());
    }

    /// <summary>
    /// 取得 P7.4 display response 的預期型別。此處刻意以 reflection 讓 RED suite 可在 abstraction 尚未落地時
    /// 編譯並輸出可讀失敗；實作完成後仍會強制固定型別名稱，避免悄悄退回開放 <c>object</c> payload。
    /// </summary>
    private static Type RequireDisplayResponseType()
    {
        var type = typeof(OperationIds).Assembly.GetType(
            "SpeechMessage.Dynamics.Abstractions.Operations.ContactImageDisplayResponseData");
        type.Should().NotBeNull("display union 必須是安全、可序列化且不含 SDK 型別的顯式 abstraction type");
        return type!;
    }

    /// <summary>
    /// 呼叫明確 branch factory，並確保 future implementation 不以可任意填欄位的 public constructor 取代
    /// fail-closed factory。factory 的輸入只允許該 branch 的 allowlisted scalar 或 copied image bytes。
    /// </summary>
    private static object InvokeFactory(Type displayType, string factoryName, params object?[] arguments)
    {
        var factory = displayType.GetMethod(factoryName, BindingFlags.Public | BindingFlags.Static);
        factory.Should().NotBeNull($"display union 必須提供 {factoryName} 的單一 branch factory");
        return factory!.Invoke(null, arguments)!;
    }

    /// <summary>
    /// 讀取 image branch 的 caller-owned copy。測試不會保存回傳陣列，讓每次 assertion 結束後皆可由 GC 回收，
    /// 並驗證 response 不會把 connector、cache 或前一個使用者的 mutable buffer 暴露給下一位呼叫者。
    /// </summary>
    private static byte[] InvokeImageGetter(object response)
    {
        var getter = response.GetType().GetMethod("GetImageBytes", BindingFlags.Public | BindingFlags.Instance);
        getter.Should().NotBeNull("image branch 必須只提供 defensive-copy getter，不能公開 backing array");
        return getter!.Invoke(response, null).Should().BeOfType<byte[]>().Subject;
    }

    /// <summary>
    /// 建立具有有效 P7.3 image branch 與 P7.4 display branch 的 constructor 參數。只有這個刻意不合法的
    /// 混合 envelope 使用 reflection；其目的正是確認 constructor 在任何 response 發佈或快取前拒絕兩個 branch。
    /// </summary>
    private static object?[] CreateConstructorArguments(ConstructorInfo constructor, object displayResponse)
    {
        var arguments = constructor.GetParameters()
            .Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue : null)
            .ToArray();
        SetArgument(constructor, arguments, "operationId", DisplayOperationId);
        SetArgument(constructor, arguments, "ceVersion", "v9.1");
        SetArgument(
            constructor,
            arguments,
            "responseKind",
            Enum.Parse(
                constructor.GetParameters().Single(parameter => parameter.Name == "responseKind").ParameterType,
                DisplayResponseKindName));
        SetArgument(
            constructor,
            arguments,
            "contactImage",
            new ContactImageResponseData(CreateValidOnePixelPng(), ContactImageMediaKind.Png));
        SetArgument(constructor, arguments, "contactImageDisplay", displayResponse);
        return arguments;
    }

    /// <summary>
    /// 依名稱填入 optional constructor 參數，避免測試因 parameter 順序改動失去混合 branch 的 fail-closed 覆蓋。
    /// 名稱不存在即表示 production 尚未提供 required closed union branch，並以明確 assertion 失敗。
    /// </summary>
    private static void SetArgument(ConstructorInfo constructor, object?[] arguments, string name, object? value)
    {
        var index = Array.FindIndex(constructor.GetParameters(), parameter => parameter.Name == name);
        index.Should().BeGreaterThanOrEqualTo(0, $"OperationResponseData 必須保留 {name} 封閉 branch parameter");
        arguments[index] = value;
    }

    /// <summary>
    /// 建立 display ProductClient 的唯一 allowlisted request。profile 與 workload 是 deployment-provided test
    /// scalar，contact GUID 只作 locator；替身不會把它們當成跨測試、跨使用者或跨 profile 的 shared state。
    /// </summary>
    private static object CreateDisplayRequest(MethodInfo method)
    {
        var requestType = method.GetParameters().First().ParameterType;
        var request = Activator.CreateInstance(requestType)
            ?? throw new InvalidOperationException("Display request must have a parameterless object initializer path.");
        requestType.GetProperty("ProfileAlias")!.SetValue(request, "crm91");
        requestType.GetProperty("WorkloadSubjectId")!.SetValue(request, "churchreport-memberinfo");
        requestType.GetProperty("ContactId")!.SetValue(
            request,
            Guid.Parse("aaaaaaaa-0000-1111-2222-bbbbbbbbbbbb"));
        return request;
    }

    /// <summary>
    /// 建立合法單一 display branch envelope，供 ProductClient dispatch contract 使用。若 factory、discriminator 或
    /// constructor 尚未落地，RED assertion 會在 executor 開始前失敗，證明沒有不安全的 partial response 被發布。
    /// </summary>
    private static OperationResponseData CreateDisplayOperationResponse(object displayResponse)
    {
        var constructor = typeof(OperationResponseData).GetConstructors()
            .SingleOrDefault(candidate => candidate.GetParameters()
                .Any(parameter => string.Equals(parameter.Name, "contactImageDisplay", StringComparison.Ordinal)));
        constructor.Should().NotBeNull("OperationResponseData 必須有 P7.4 display branch");

        var arguments = CreateConstructorArguments(constructor!, displayResponse);
        SetArgument(constructor!, arguments, "contactImage", null);
        return constructor!.Invoke(arguments).Should().BeOfType<OperationResponseData>().Subject;
    }

    /// <summary>
    /// 產生有效且極小的 PNG 以避免測試本身建立 unbounded buffer。這是 synthetic 固定資料，沒有帳戶、聯絡人、
    /// token 或外部圖片內容；每次呼叫都配置新 array，因此不同測試與 A/B 呼叫絕不共用 mutable byte state。
    /// </summary>
    private static byte[] CreateValidOnePixelPng()
        => Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");

    /// <summary>
    /// 模擬 executor 的純記憶體單次結果。它不擁有連線、lease、stream 或計時器，僅記錄本測試呼叫以驗證
    /// cancellation token 與固定 capability；因此不會讓前一個 request 的 profile、bytes 或身份遺留到下一個測試。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly OperationExecutionResult _result;

        /// <summary>
        /// 建立由目前測試唯一擁有的固定結果；result 本身不會被替身修改或排入背景工作。
        /// </summary>
        public RecordingExecutor(OperationExecutionResult result) => _result = result;

        /// <summary>
        /// 取得最後一個 request，僅在測試方法的同步 assertion 期間存活，避免任何 shared request retention。
        /// </summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>
        /// 取得 ProductClient 原樣傳入的取消權杖，以驗證取消所有權仍屬 request，而非由 client 建立可洩漏的 CTS。
        /// </summary>
        public CancellationToken ReceivedCancellationToken { get; private set; }

        /// <summary>
        /// 同步回傳固定結果並先觀察取消。故障注入為已取消 token 時立即擲出，防止測試替身模擬 retry、fallback
        /// 或延遲背景 I/O；真正 connector 的 lease、fault eviction 與 dispose 仍由 production owner 負責。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(_result);
        }
    }
}
