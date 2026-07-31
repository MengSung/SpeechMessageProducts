// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs
// 目的：用 fake HttpMessageHandler 驗證 live WhoAmI 與 fee FetchXML 路徑。
//
// 保母教學：
// - 這些測試不連真實 CRM。
// - 重點是 URL、編碼、錯誤碼與「禁止呼叫端自帶 FetchXML」。
// ============================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class DynamicsWebApiClientTests
{
    [Fact]
    public async Task WhoAmI_calls_approved_root_function()
    {
        HttpRequestMessage? seen = null;
        var client = CreateClient(request =>
        {
            seen = request;
            return JsonResponse("""{"BusinessUnitId":"22222222-2222-2222-2222-222222222222"}""");
        });

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.WhoAmI);
        result.Data.WhoAmI!.BusinessUnitId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        seen.Should().NotBeNull();
        seen!.Method.Should().Be(HttpMethod.Get);
        seen.RequestUri!.AbsoluteUri.Should().Be("https://crm.example.local/org/api/data/v8.2/WhoAmI");
        seen.Headers.Accept.ToString().Should().Contain("application/json");
        seen.Headers.GetValues("Prefer").Should().ContainSingle()
            .Which.Should().Be("odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");
    }

    /// <summary>
    /// 驗證成功結果只能向產品呼叫端公開受控 operation ID、CE 版本與上游資料，不能把
    /// <c>ApprovedWebApiRoot</c> 的 CRM hostname 或 <c>/api/data/</c> 路徑跨越 Gateway 信任邊界。
    /// 測試刻意使用可辨識的假主機與 v8.2 路徑，並序列化真實 <see cref="OperationExecutionResult.Data"/>，
    /// 因為 HTTP Gateway 會把這個物件直接寫入回應；若內部路由中繼資料再次被加入匿名 payload，
    /// 鍵名與實際 URI 內容的雙重 assertion 都會立即失敗。Fake transport 不建立背景工作、Timer、
    /// Stream 或共用 Session，要求與回應仍由 Production Client 的既有 using／Dispose 路徑負責回收，
    /// 因此本測試只保護資訊揭露契約，不改變取消、連線池或資源 owner 的行為。
    /// </summary>
    [Fact]
    public async Task Successful_result_does_not_disclose_internal_web_api_root()
    {
        var client = CreateClient(_ =>
            JsonResponse("""{"BusinessUnitId":"22222222-2222-2222-2222-222222222222"}"""));

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();

        var serializedData = JsonSerializer.Serialize(result.Data);
        using var document = JsonDocument.Parse(serializedData);
        document.RootElement.TryGetProperty("approvedWebApiRoot", out _).Should().BeFalse();
        serializedData.Contains("crm.example.local", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        serializedData.Contains("/api/data/", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        document.RootElement.GetProperty("operationId").GetString()
            .Should().Be(OperationIds.RuntimeHealthWhoAmI);
        document.RootElement.GetProperty("ceVersion").GetString().Should().Be("8.2");
        document.RootElement.TryGetProperty("data", out _).Should().BeFalse();
        document.RootElement.GetProperty("whoAmI").GetProperty("businessUnitId").GetString()
            .Should().Be("22222222-2222-2222-2222-222222222222");
    }

    /// <summary>
    /// 驗證上游 WhoAmI 的已知 <c>@odata.context</c> 僅在 connector 內部使用後丟棄，產品信封只保留
    /// 封閉的 <c>whoAmI</c> branch。這可避免 CRM host、API root 或 OData routing annotation 跨越 boundary；
    /// 非 WhoAmI allowlist 的資料由另一個 fail-closed projector 拒絕，而不是遞迴保留任意巢狀 JSON。
    /// Fake handler、回應內容和 <see cref="JsonDocument"/> 都只由本測試 scope 擁有並以 using 釋放；
    /// Production client 仍是其 HttpResponseMessage、stream、pooled buffer 清零與歸還路徑的唯一 owner。
    /// </summary>
    [Fact]
    public async Task Successful_result_removes_upstream_odata_routing_annotations()
    {
        var client = CreateClient(_ => JsonResponse("""
            {
              "@odata.context":"https://crm.example.local/org/api/data/v8.2/$metadata#WhoAmIResponse",
              "UserId":"33333333-3333-3333-3333-333333333333"
            }
            """));

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        document.RootElement.TryGetProperty("@odata.context", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("@odata.nextLink", out _).Should().BeFalse();
        document.RootElement.GetProperty("whoAmI").GetProperty("userId").GetString()
            .Should().Be("33333333-3333-3333-3333-333333333333");
        JsonSerializer.Serialize(result.Data).Should().NotContain("crm.example.local");
    }

    [Fact]
    public async Task Fee_dedication_by_contact_uses_server_owned_fetchxml_and_encodes_guid()
    {
        HttpRequestMessage? seen = null;
        var contactId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var client = CreateClient(request =>
        {
            seen = request;
            return JsonResponse("""{"value":[{"new_feeid":"ffffffff-1111-2222-3333-444444444444"}]}""");
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["contactId"] = contactId,
                ["contactName"] = "O'Brien & Sons"
            });

        result.Succeeded.Should().BeTrue();
        seen.Should().NotBeNull();
        seen!.RequestUri.Should().NotBeNull();
        seen.RequestUri!.AbsolutePath.Should().Be("/org/api/data/v8.2/new_fees");

        var fetchXml = ExtractFetchXml(seen.RequestUri);
        fetchXml.Should().NotBeNullOrWhiteSpace();
        fetchXml.Should().Contain("new_fee");
        fetchXml.Should().Contain("{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}");
        fetchXml.Should().Contain("uiname=\"O&apos;Brien &amp; Sons\"");
        fetchXml.Should().NotContain("{{contactId}}");
    }

    /// <summary>
    /// 驗證 Package 1 回應投影只接受該 capability 明確登錄的 CRM 欄位。
    /// 上游即使回傳可序列化的任意欄位，也不得讓它跨越 WebApi 到 Gateway/ProductClient；
    /// 因為這會把 CRM schema、PII 或未審核的延伸資料變成產品契約。此測試不建立真實 CRM
    /// 連線，fake transport 的 request/response、stream 與暫存 JSON 均仍由單一呼叫 scope 擁有並釋放。
    /// </summary>
    [Fact]
    public async Task Fee_projection_rejects_unregistered_upstream_field()
    {
        var client = CreateClient(_ => JsonResponse("""
            {
              "value": [
                {
                  "new_feeid": "ffffffff-1111-2222-3333-444444444444",
                  "unregistered_crm_field": "must-not-cross-product-boundary"
                }
              ]
            }
            """));

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["contactId"] = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
    }

    /// <summary>
    /// 驗證 continuation URL 是 WebApi 的內部 SSRF 邊界，不是產品可見或可自由追蹤的資料。
    /// 不同 origin 的 nextLink 必須在下一個 request、Authorization header、token 取得或連線資源建立之前
    /// 失敗；request 計數恰為一，確保惡意連結不會取得任何認證後的第二次 outbound 呼叫。
    /// </summary>
    [Fact]
    public async Task Fee_continuation_cross_origin_is_rejected_before_second_request()
    {
        var requestCount = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return JsonResponse("""
                {
                  "value": [],
                  "@odata.nextLink": "https://untrusted.example.invalid/api/data/v8.2/new_fees?$skiptoken=forbidden"
                }
                """);
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["contactId"] = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        requestCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 continuation 即使表面上使用已核准 CRM 主機，仍不得藉由 user-info、fragment 或編碼的
    /// 路徑分隔符號改變 URI 語意。connector 必須在目前頁面的 response 已釋放、下一個 request
    /// 尚未建立與附加 Windows/Kerberos 或 Bearer 驗證前失敗；因此測試只允許第一頁到達 fake transport，
    /// 不會建立跨 profile、跨 session 或跨 target 的憑證請求。
    /// </summary>
    [Theory]
    [InlineData("https://ignored-user@crm.example.local/org/api/data/v8.2/new_fees?$skiptoken=user-info")]
    [InlineData("https://crm.example.local/org/api/data/v8.2/new_fees%2Fencoded-separator?$skiptoken=encoded")]
    [InlineData("https://crm.example.local/org/api/data/v8.2/new_fees?$skiptoken=fragment#forbidden")]
    public async Task Fee_unsafe_continuation_is_rejected_before_second_request(string unsafeContinuation)
    {
        var requestCount = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return JsonResponse($$"""
                {
                  "value": [],
                  "@odata.nextLink": "{{unsafeContinuation}}"
                }
                """);
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        requestCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 CRM 的合法相對 continuation 只由 WebApi 在同一個有界 request scope 內追蹤，
    /// 而不是把 nextLink 或原始 <c>value</c> JSON 交給產品自行續頁。兩頁資料要依原始
    /// template 順序匯總成封閉的 <c>feeRecords</c> 契約，且回傳 JSON 不能包含 OData annotation、
    /// CRM hostname 或 API root。每個 fake response 都由 connector 的 <c>using</c> scope 確定釋放。
    /// </summary>
    [Fact]
    public async Task Fee_relative_continuation_is_followed_and_projected_to_closed_records()
    {
        var requestedUris = new List<Uri>();
        var client = CreateClient(request =>
        {
            requestedUris.Add(request.RequestUri!);
            return requestedUris.Count switch
            {
                1 => JsonResponse("""
                    {
                      "value": [{ "new_feeid": "11111111-1111-1111-1111-111111111111" }],
                      "@odata.nextLink": "new_fees?$skiptoken=page-two"
                    }
                    """),
                2 => JsonResponse("""
                    {
                      "value": [{ "new_feeid": "22222222-2222-2222-2222-222222222222" }]
                    }
                    """),
                _ => throw new InvalidOperationException("The bounded paging loop made an unexpected extra request.")
            };
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["contactId"] = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            });

        result.Succeeded.Should().BeTrue();
        requestedUris.Should().HaveCount(2);
        requestedUris[1].AbsoluteUri.Should().Be(
            "https://crm.example.local/org/api/data/v8.2/new_fees?$skiptoken=page-two");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        document.RootElement.GetProperty("feeRecords").GetArrayLength().Should().Be(2);
        JsonSerializer.Serialize(result.Data).Should().NotContain("@odata.nextLink");
        JsonSerializer.Serialize(result.Data).Should().NotContain("crm.example.local");
    }

    /// <summary>
    /// 驗證 server-side paging 不會因重複 continuation 而無限持有 response、list、buffer 或 token。
    /// 第二頁再次宣告第一頁已見過的同一 URL 時，循環必須在第三次認證 outbound request 前被偵測並
    /// 以受控 upstream failure 結束；因此 request scope 的 visited-set 有唯一 owner，完成後不留 static
    /// 或 profile 共用狀態。
    /// </summary>
    [Fact]
    public async Task Fee_continuation_cycle_is_rejected_before_third_request()
    {
        const string repeatedLink = "https://crm.example.local/org/api/data/v8.2/new_fees?$skiptoken=loop";
        var requestCount = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return JsonResponse($$"""
                {
                  "value": [],
                  "@odata.nextLink": "{{repeatedLink}}"
                }
                """);
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["contactId"] = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        requestCount.Should().Be(2);
    }

    /// <summary>
    /// 驗證同一個已核准 Web API root 的絕對 continuation 會由 connector 在目前 request scope 內續頁。
    /// 此測試釘住虛擬目錄 <c>/org/</c> 與版本 <c>v8.2</c>：只有完全落在該 root 下的 URL 可以
    /// 在建立下一個認證 request 前通過；visited set 與暫存列集合皆是呼叫區域資源，結束後不保留。
    /// </summary>
    [Fact]
    public async Task Fee_same_root_absolute_continuation_is_followed()
    {
        var requestCount = 0;
        var client = CreateClient(_ => Interlocked.Increment(ref requestCount) switch
        {
            1 => JsonResponse("""
                {
                  "value": [{ "new_feeid": "11111111-1111-1111-1111-111111111111" }],
                  "@odata.nextLink": "https://crm.example.local/org/api/data/v8.2/new_fees?$skiptoken=next"
                }
                """),
            2 => JsonResponse("""
                { "value": [{ "new_feeid": "22222222-2222-2222-2222-222222222222" }] }
                """),
            _ => throw new InvalidOperationException("Unexpected continuation request.")
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeTrue();
        result.Data!.FeeRecords.Should().HaveCount(2);
        requestCount.Should().Be(2);
    }

    /// <summary>
    /// 驗證 continuation 即使指向同一 host，只要越過已核准的組織虛擬目錄，就必須在第二個認證 request
    /// 前 fail-closed。這可避免被 CRM 回應中的 URL 導向另一個站台或另一個 organization path。
    /// </summary>
    [Fact]
    public async Task Fee_continuation_wrong_base_path_is_rejected_before_second_request()
    {
        var requestCount = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return JsonResponse("""
                {
                  "value": [],
                  "@odata.nextLink": "https://crm.example.local/other/api/data/v8.2/new_fees?$skiptoken=forbidden"
                }
                """);
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        requestCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證同 root host 但錯誤 CE API version 的 continuation 不會借用目前 profile 的授權繼續呼叫。
    /// 版本是 immutable profile generation 的一部分；錯誤版本須在 request 建立與 token 套用前停止。
    /// </summary>
    [Fact]
    public async Task Fee_continuation_wrong_api_version_is_rejected_before_second_request()
    {
        var requestCount = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return JsonResponse("""
                {
                  "value": [],
                  "@odata.nextLink": "https://crm.example.local/org/api/data/v9.1/new_fees?$skiptoken=forbidden"
                }
                """);
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        requestCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證無法解析的 continuation 字串會在保留目前 page 的 response cleanup 後失敗，而不是讓 URI parser
    /// 的例外外洩或改用任意 fallback。此處只有第一個受控 request，沒有 token、session 或 connection 跨 scope 保留。
    /// </summary>
    [Fact]
    public async Task Fee_malformed_continuation_is_rejected_before_second_request()
    {
        var requestCount = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return JsonResponse("""
                {
                  "value": [],
                  "@odata.nextLink": "https://[not-a-valid-host"
                }
                """);
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        requestCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 page-count policy 在第一頁已達上限而又收到 continuation 時停止，不會因「再試一頁」而建立
    /// 額外認證 request。限制來自 immutable operation definition，非 caller 或 profile 的可變選項。
    /// </summary>
    [Fact]
    public async Task Fee_continuation_page_limit_is_rejected_before_second_request()
    {
        var requestCount = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return JsonResponse("""
                {
                  "value": [],
                  "@odata.nextLink": "new_fees?$skiptoken=second-page"
                }
                """);
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var source)
            .Should().BeTrue();
        var definition = WithResponseLimits(source!, maximumPageCount: 1);

        var result = await client.ExecuteRegisteredOperationAsync(
            definition,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        requestCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 connector 使用 operation-owned 單頁 byte limit，而不是僅依全域 host 設定。超限 page 的 content
    /// 在解析或投影前被拒絕，response/content stream 仍由 using scope 與 ArrayPool cleanup 正常釋放。
    /// </summary>
    [Fact]
    public async Task Fee_page_byte_limit_is_rejected_before_projection()
    {
        var client = CreateClient(_ => JsonResponse("""
            { "value": [{ "new_feeid": "11111111-1111-1111-1111-111111111111", "new_name": "over-limit" }] }
            """));

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var source)
            .Should().BeTrue();
        var definition = WithResponseLimits(source!, maximumPageBytes: 32, maximumCumulativeResponseBytes: 64);

        var result = await client.ExecuteRegisteredOperationAsync(
            definition,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
    }

    /// <summary>
    /// 驗證 cumulative byte budget 以已讀取 page 的實際 bytes 累計，第二頁越限後回傳受控失敗且不回傳部分列。
    /// 總量與 list 都只存活於單一呼叫 scope，避免多頁 CRM 回應把記憶體或 token-bearing continuation 無限延長。
    /// </summary>
    [Fact]
    public async Task Fee_cumulative_byte_limit_rejects_partial_result()
    {
        var requestCount = 0;
        var client = CreateClient(_ => Interlocked.Increment(ref requestCount) switch
        {
            1 => JsonResponse("""
                {
                  "value": [{ "new_feeid": "11111111-1111-1111-1111-111111111111", "new_name": "first-page" }],
                  "@odata.nextLink": "new_fees?$skiptoken=second"
                }
                """),
            2 => JsonResponse("""
                { "value": [{ "new_feeid": "22222222-2222-2222-2222-222222222222", "new_name": "second-page" }] }
                """),
            _ => throw new InvalidOperationException("Unexpected cumulative-limit request.")
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var source)
            .Should().BeTrue();
        var definition = WithResponseLimits(source!, maximumPageBytes: 512, maximumCumulativeResponseBytes: 180);

        var result = await client.ExecuteRegisteredOperationAsync(
            definition,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.Data.Should().BeNull();
        requestCount.Should().Be(2);
    }

    /// <summary>
    /// 驗證 result-row policy 在 projection 前阻擋超過上限的 CRM rows。這個獨立上限避免很多極小 JSON
    /// objects 仍能在 byte limit 內造成大量 record 物件與 GC 壓力；不得回傳被截斷的產品清單。
    /// </summary>
    [Fact]
    public async Task Fee_result_row_limit_rejects_partial_result()
    {
        var client = CreateClient(_ => JsonResponse("""
            {
              "value": [
                { "new_feeid": "11111111-1111-1111-1111-111111111111" },
                { "new_feeid": "22222222-2222-2222-2222-222222222222" }
              ]
            }
            """));

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var source)
            .Should().BeTrue();
        var definition = WithResponseLimits(source!, maximumResultItemCount: 1);

        var result = await client.ExecuteRegisteredOperationAsync(
            definition,
            new Dictionary<string, object?> { ["contactId"] = Guid.NewGuid() });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task Missing_required_fee_parameter_fails_before_http()
    {
        var called = false;
        var client = CreateClient(_ =>
        {
            called = true;
            return JsonResponse("{}");
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContactDateRange, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["contactId"] = Guid.NewGuid().ToString()
            });

        called.Should().BeFalse();
        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
        result.ErrorMessage.Should().Contain("startDate");
    }

    /// <summary>
    /// 驗證尚未建立封閉產品回應 branch 的 metadata capability 會在 root、template、HTTP request、
    /// authorization 與 transport ownership 開始前 fail-closed。這防止未投影的 CRM metadata、
    /// annotation 或 continuation 被暫時包進泛型 result，也確保 feature-disabled 的路徑不會留下
    /// request、stream、token 或 session 資源。
    /// </summary>
    [Fact]
    public async Task Unsupported_metadata_operation_fails_before_http()
    {
        var requestCount = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return JsonResponse("{}");
        });

        Package01OperationRegistry.TryGet(OperationIds.MetadataOptionSetByAttribute, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["entityLogicalName"] = "contact",
                ["attributeLogicalName"] = "new_category"
            });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.NotImplemented);
        requestCount.Should().Be(0);
    }

    [Fact]
    public async Task Adfs_oauth_sends_bearer_token_from_secret_reference()
    {
        HttpRequestMessage? seen = null;
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            CredentialReferenceName = "ADFS_TOKEN",
            TimeoutSeconds = 10
        });

        var transport = new DynamicsHttpTransport(
            new StubHandler(request =>
            {
                seen = request;
                return JsonResponse("""{"UserId":"33333333-3333-3333-3333-333333333333"}""");
            }),
            NullLogger<DynamicsHttpTransport>.Instance);

        // CredentialReferenceName 的 bearer 是由 AdfsOAuthTokenProvider 解析，不是 client 自己讀。
        var secrets = new DictionarySecretResolver(new Dictionary<string, string>
        {
            ["ADFS_TOKEN"] = "test-access-token"
        });
        var tokenProvider = new AdfsOAuthTokenProvider(
            options,
            secrets,
            NullLogger<AdfsOAuthTokenProvider>.Instance);

        var client = new DynamicsWebApiClient(
            options,
            transport,
            secrets,
            tokenProvider,
            NullLogger<DynamicsWebApiClient>.Instance);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        seen!.Headers.Authorization.Should().NotBeNull();
        seen.Headers.Authorization!.Scheme.Should().Be("Bearer");
        seen.Headers.Authorization.Parameter.Should().Be("test-access-token");
        seen.RequestUri!.AbsoluteUri.Should().Be("https://crm.example.local/org/api/data/v9.1/WhoAmI");
    }

    [Fact]
    public async Task Content_length_over_response_limit_is_rejected_before_body_read()
    {
        var content = new ThrowOnReadContent(contentLength: 2048);
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }, options => options.MaxResponseBytes = 1024);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        content.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Chunked_response_over_limit_is_rejected_and_stream_is_disposed()
    {
        var content = new TrackingStreamContent(new byte[2048]);
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }, options => options.MaxResponseBytes = 1024);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        content.StreamDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Compressed_response_is_rejected_before_parsing()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        content.Headers.ContentEncoding.Add("gzip");
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
    }

    [Fact]
    public async Task Upstream_error_body_is_not_buffered_or_exposed()
    {
        var content = new ThrowOnReadContent(contentLength: null);
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = content
        });

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain("upstream-sensitive-body");
        content.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Token_provider_exception_text_is_not_returned_to_caller()
    {
        const string sensitiveText = "secret-token-fragment-should-never-leave-host";
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            CredentialReferenceName = "ADFS_TOKEN",
            TimeoutSeconds = 10
        });
        var transport = new DynamicsHttpTransport(
            new StubHandler(_ => JsonResponse("{}")),
            NullLogger<DynamicsHttpTransport>.Instance);
        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new ThrowingTokenProvider(sensitiveText),
            NullLogger<DynamicsWebApiClient>.Instance);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain(sensitiveText);
        result.ErrorMessage.Should().Be("AdfsOAuth token acquisition failed.");
    }

    /// <summary>
    /// 呼叫端取消不是認證失敗；Token provider 觀察到同一 CancellationToken 後，取消必須原樣向上傳播，
    /// 不能被轉換成 Unauthorized，否則上層會錯誤記錄登入失敗並失去要求中止語意。
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_during_token_acquisition_is_propagated()
    {
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            CredentialReferenceName = "ADFS_TOKEN",
            TimeoutSeconds = 10
        });
        var transport = new DynamicsHttpTransport(
            new StubHandler(_ => throw new InvalidOperationException("transport must not run after cancellation")),
            NullLogger<DynamicsHttpTransport>.Instance);
        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new CancellationObservingTokenProvider(),
            NullLogger<DynamicsWebApiClient>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => client.WhoAmIAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Token provider 若超過整體要求逾時，對外應回傳 UpstreamTimeout，而不是把內部 linked token 的取消例外洩漏給呼叫端。
    /// 這與呼叫端主動取消不同：前者是可觀測的上游逾時結果，後者必須保留 OperationCanceledException。
    /// </summary>
    [Fact]
    public async Task Token_acquisition_timeout_returns_bounded_upstream_timeout()
    {
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            CredentialReferenceName = "ADFS_TOKEN",
            TimeoutSeconds = 1
        });
        var transport = new DynamicsHttpTransport(
            new StubHandler(_ => throw new InvalidOperationException("transport must not run after token timeout")),
            NullLogger<DynamicsHttpTransport>.Instance);
        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new BlockingTokenProvider(),
            NullLogger<DynamicsWebApiClient>.Instance);

        var result = await client.WhoAmIAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamTimeout);
        result.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task Transport_exception_details_are_not_logged_or_returned()
    {
        const string sensitiveText = "https://crm.example.local/?token=sensitive-fragment";
        var logger = new CapturingLogger<DynamicsWebApiClient>();
        var client = CreateClient(
            _ => throw new HttpRequestException(sensitiveText),
            logger: logger);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain(sensitiveText);
        logger.Exception.Should().BeNull("upstream exceptions can contain URLs, credentials, or tokens");
        logger.Message.Should().NotContain(sensitiveText);
    }

    [Fact]
    public async Task Redirect_location_details_are_not_logged_or_returned()
    {
        const string sensitiveLocation = "https://adfs.example.local/adfs/ls/?wctx=sensitive-context";
        var logger = new CapturingLogger<DynamicsWebApiClient>();
        var client = CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri(sensitiveLocation) }
            },
            logger: logger);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain(sensitiveLocation);
        logger.Message.Should().NotContain(sensitiveLocation);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Retryable_read_honors_bounded_retry_and_disposes_previous_response(
        HttpStatusCode retryStatus)
    {
        var attempts = 0;
        var firstContent = new TrackingDisposeContent("retry-body-must-not-be-read");
        var client = CreateClient(request =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                var retry = new HttpResponseMessage(retryStatus) { Content = firstContent };
                retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return retry;
            }

            return JsonResponse("{\"UserId\":\"33333333-3333-3333-3333-333333333333\"}");
        }, options =>
        {
            options.MaxRetryAttempts = 1;
            options.MaxRetryDelaySeconds = 1;
        });

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(2);
        firstContent.Disposed.Should().BeTrue();
        firstContent.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Non_retryable_failure_is_not_replayed()
    {
        var attempts = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref attempts);
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }, options => options.MaxRetryAttempts = 3);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        attempts.Should().Be(1);
    }

    /// <summary>
    /// 依既有 immutable registry definition 建立只供 fake-transport regression 使用的限制副本。
    /// 測試不修改 static registry 或共享 profile state，因此平行執行時不會把 page、byte 或 row policy
    /// 洩漏給其他案例；每個測試呼叫擁有自己的 definition，結束後沒有可釋放或跨測試保留的資源。
    /// </summary>
    private static OperationDefinition WithResponseLimits(
        OperationDefinition source,
        int? maximumPageCount = null,
        int? maximumPageBytes = null,
        int? maximumCumulativeResponseBytes = null,
        int? maximumResultItemCount = null)
    {
        return new OperationDefinition
        {
            CapabilityOperationId = source.CapabilityOperationId,
            OperationKind = source.OperationKind,
            TemplateKind = source.TemplateKind,
            TemplateId = source.TemplateId,
            TemplateHash = source.TemplateHash,
            ResponseKind = source.ResponseKind,
            MaximumPageCount = maximumPageCount ?? source.MaximumPageCount,
            MaximumPageBytes = maximumPageBytes ?? source.MaximumPageBytes,
            MaximumCumulativeResponseBytes = maximumCumulativeResponseBytes ?? source.MaximumCumulativeResponseBytes,
            MaximumResultItemCount = maximumResultItemCount ?? source.MaximumResultItemCount,
            DataClassification = source.DataClassification,
            AuditRequirement = source.AuditRequirement,
            IdempotencyClass = source.IdempotencyClass,
            Parameters = source.Parameters,
            Package = source.Package
        };
    }

    private static DynamicsWebApiClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Action<DynamicsWebApiOptions>? configure = null,
        ILogger<DynamicsWebApiClient>? logger = null)
    {
        var configured = new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "8.2",
            AuthMode = DynamicsAuthMode.Windows,
            CredentialSource = DynamicsCredentialSource.HostIdentity,
            TimeoutSeconds = 10
        };
        configure?.Invoke(configured);
        var options = Options.Create(configured);

        var transport = new DynamicsHttpTransport(
            new StubHandler(responder),
            NullLogger<DynamicsHttpTransport>.Instance);

        return new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new StaticAdfsOAuthTokenProvider("unused-for-windows"),
            logger ?? NullLogger<DynamicsWebApiClient>.Instance);
    }

    private static string? ExtractFetchXml(Uri requestUri)
    {
        var query = requestUri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..idx]);
            if (!string.Equals(key, "fetchXml", StringComparison.Ordinal))
            {
                continue;
            }

            return Uri.UnescapeDataString(part[(idx + 1)..]);
        }

        return null;
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    /// <summary>
    /// 測試用固定 token 提供者。Windows 模式不會被呼叫；AdfsOAuth 直接 bearer 秘密時也不會被呼叫。
    /// </summary>
    private sealed class StaticAdfsOAuthTokenProvider : IAdfsOAuthTokenProvider
    {
        private readonly string _token;

        public StaticAdfsOAuthTokenProvider(string token) => _token = token;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_token);
    }

    private sealed class ThrowingTokenProvider : IAdfsOAuthTokenProvider
    {
        private readonly string _message;

        public ThrowingTokenProvider(string message) => _message = message;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromException<string>(new InvalidOperationException(_message));
    }

    /// <summary>
    /// 測試用 provider 只回傳由呼叫端權杖建立的取消 Task，不保存 Token、要求或其他跨測試狀態。
    /// </summary>
    private sealed class CancellationObservingTokenProvider : IAdfsOAuthTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromCanceled<string>(cancellationToken);
    }

    /// <summary>
    /// 模擬永不自行完成、只接受取消的 Token I/O；取消後 Task 立即結束，不會留下背景工作或 timer。
    /// </summary>
    private sealed class BlockingTokenProvider : IAdfsOAuthTokenProvider
    {
        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "unreachable";
        }
    }

    private sealed class ThrowOnReadContent : HttpContent
    {
        private bool _readAttempted;

        public ThrowOnReadContent(long? contentLength)
        {
            Headers.ContentLength = contentLength;
        }

        public bool ReadAttempted => Volatile.Read(ref _readAttempted);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Volatile.Write(ref _readAttempted, true);
            return Task.FromException(new InvalidOperationException("upstream-sensitive-body"));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength ?? 0;
            return Headers.ContentLength.HasValue;
        }
    }

    private sealed class TrackingStreamContent : HttpContent
    {
        private readonly byte[] _bytes;
        private TrackingMemoryStream? _stream;

        public TrackingStreamContent(byte[] bytes) => _bytes = bytes;

        public bool StreamDisposed => _stream?.Disposed == true;

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            _stream = new TrackingMemoryStream(_bytes);
            return Task.FromResult<Stream>(_stream);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream(byte[] bytes) : base(bytes, writable: false)
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingDisposeContent : StringContent
    {
        private int _disposed;
        private int _readAttempted;

        public TrackingDisposeContent(string content) : base(content, Encoding.UTF8, "text/plain")
        {
        }

        public bool Disposed => Volatile.Read(ref _disposed) == 1;
        public bool ReadAttempted => Volatile.Read(ref _readAttempted) == 1;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Volatile.Write(ref _readAttempted, 1);
            return base.SerializeToStreamAsync(stream, context);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Volatile.Write(ref _disposed, 1);
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? Exception { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exception = exception;
            Message = formatter(state, exception);
        }
    }
}
