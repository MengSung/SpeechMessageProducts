// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/Package01OperationRegistryTests.cs
// 用途：鎖定 Package 0/1 operation registry 的封閉回應契約、有限 paging 政策與雜湊材料。
//
// 安全與生命週期邊界：
// 1. registry 是產品與 Web API runtime 之間唯一可序列化回應形狀的來源；測試禁止退回 object、
//    JsonElement 或 OData 欄位名稱，避免 CRM 路由、continuation 或擴充資料跨越產品邊界。
// 2. 每個已登錄作業都必須有有限的頁數、單頁位元組與累積位元組上限；後續 connector 只可在
//    該政策內持有 page stream、buffer 與 continuation 狀態，並在完成、取消或拒絕時釋放它們。
// 3. Package 1 feature gate 維持關閉，因此本檔只驗證未來啟用前必須滿足的安全契約，不建立任何
//    CRM 連線、Token、背景工作或使用者工作階段。
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Package 0/1 registry 在 process-static 建立後只暴露固定 capability、封閉回應種類與有限資源政策。
/// 測試不持有 runtime、HTTP client 或取消註冊；所有序列化文件都在 method scope 內立即 dispose，避免測試
/// 自己掩蓋 response-buffer 或 JsonDocument 的生命週期問題。
/// </summary>
public sealed class Package01OperationRegistryTests
{
    private const int ConservativeMaximumPageCount = 4;
    private const int ConservativeMaximumPageBytes = 64 * 1024;
    private const int ConservativeMaximumCumulativeResponseBytes =
        ConservativeMaximumPageCount * ConservativeMaximumPageBytes;
    private const int ConservativeMaximumResultItemCount = 4096;

    /// <summary>
    /// 確認目前九個 registry capability 完整存在，避免 matrix、connector 與產品在 feature gate 尚未開啟前
    /// 各自發明未經審查的作業 ID。此檢查只讀取 immutable registry，不配置外部資源。
    /// </summary>
    [Fact]
    public void Package01_registry_contains_exactly_expected_runtime_and_fee_read_operations()
    {
        var ids = Package01OperationRegistry.All.Select(x => x.CapabilityOperationId).OrderBy(x => x).ToArray();

        ids.Should().Contain(new[]
        {
            OperationIds.RuntimeHealthWhoAmI,
            OperationIds.RuntimePoolValidateConnection,
            OperationIds.MetadataOptionSetByAttribute,
            OperationIds.FeeDedicationRetrieveByContact,
            OperationIds.FeeDedicationRetrieveByContactDateRange,
            OperationIds.FeesRetrieveByDedicationPeriod,
            OperationIds.FeesEditorLoadByDiscipleLesson,
            OperationIds.LessonsStorRetrieveByContact,
            OperationIds.LessonsStorRetrieveByDiscipleLesson
        });

        ids.Should().HaveCount(9);
    }

    /// <summary>
    /// 確認 fee 查詢的必要 typed parameter 仍由 server-owned template 約束，防止呼叫端以遺漏欄位或 raw
    /// FetchXML 繞過既有 allowlist。此處不送出請求，因此不存在跨測試的認證或連線狀態。
    /// </summary>
    [Theory]
    [InlineData(OperationIds.FeeDedicationRetrieveByContact, "contactId")]
    [InlineData(OperationIds.FeeDedicationRetrieveByContactDateRange, "startDate")]
    [InlineData(OperationIds.FeesRetrieveByDedicationPeriod, "paidPeriod")]
    public void Fee_read_operations_require_expected_parameters(string operationId, string requiredParameter)
    {
        Package01OperationRegistry.TryGet(operationId, out var definition).Should().BeTrue();
        definition!.Parameters.Should().Contain(p => p.Name == requiredParameter && p.Required);
    }

    /// <summary>
    /// 驗證每個已登錄 capability 都宣告封閉回應 discriminator 與同一組保守、有限的 page/byte 上限。
    /// 四頁、每頁 64 KiB 且累積 256 KiB 讓關閉中的 Package 1 在尚無實測容量證據前保持小而可預測的
    /// 記憶體與 credential-bearing request 範圍；metadata 則明確 Unsupported，以失敗關閉取代 raw metadata 回傳。
    /// </summary>
    [Theory]
    [InlineData(OperationIds.RuntimeHealthWhoAmI, OperationResponseKind.WhoAmI)]
    [InlineData(OperationIds.RuntimePoolValidateConnection, OperationResponseKind.WhoAmI)]
    [InlineData(OperationIds.MetadataOptionSetByAttribute, OperationResponseKind.Unsupported)]
    [InlineData(OperationIds.FeeDedicationRetrieveByContact, OperationResponseKind.Package01FeeRecords)]
    [InlineData(OperationIds.FeeDedicationRetrieveByContactDateRange, OperationResponseKind.Package01FeeRecords)]
    [InlineData(OperationIds.FeesRetrieveByDedicationPeriod, OperationResponseKind.Package01FeeRecords)]
    [InlineData(OperationIds.FeesEditorLoadByDiscipleLesson, OperationResponseKind.Package01StorLessonRecords)]
    [InlineData(OperationIds.LessonsStorRetrieveByContact, OperationResponseKind.Package01StorLessonRecords)]
    [InlineData(OperationIds.LessonsStorRetrieveByDiscipleLesson, OperationResponseKind.Package01StorLessonRecords)]
    public void Registered_operations_declare_closed_response_kind_and_conservative_finite_paging_policy(
        string operationId,
        OperationResponseKind expectedResponseKind)
    {
        Package01OperationRegistry.TryGet(operationId, out var definition).Should().BeTrue();

        definition!.ResponseKind.Should().Be(expectedResponseKind);
        definition.MaximumPageCount.Should().Be(ConservativeMaximumPageCount);
        definition.MaximumPageBytes.Should().Be(ConservativeMaximumPageBytes);
        definition.MaximumCumulativeResponseBytes.Should().Be(ConservativeMaximumCumulativeResponseBytes);
        definition.MaximumResultItemCount.Should().Be(ConservativeMaximumResultItemCount);
    }

    /// <summary>
    /// 驗證 template revision 不僅識別 URI 範本，也綁定 response kind 與三個上限。若日後放寬 page 或
    /// buffer 政策，雜湊必定變更，佇列、audit 與 matrix 不會在舊 revision 下悄悄承接不同的資源風險。
    /// </summary>
    [Fact]
    public void Template_hash_includes_closed_response_kind_and_finite_response_policy()
    {
        foreach (var definition in Package01OperationRegistry.All)
        {
            var material = string.Join("|",
                definition.TemplateKind,
                definition.TemplateId,
                definition.CapabilityOperationId,
                definition.ResponseKind,
                definition.MaximumPageCount,
                definition.MaximumPageBytes,
                definition.MaximumCumulativeResponseBytes,
                definition.MaximumResultItemCount);
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();

            definition.TemplateHash.Should().Be(expected, definition.CapabilityOperationId);
        }
    }

    /// <summary>
    /// 驗證 Package 1 safe wire records 的 JSON 只含已登錄產品欄位與目前選定的 fee branch。null branch
    /// 必須省略，因而不會把不同操作的資料、OData annotation、CRM logical name 或 continuation 留在
    /// Gateway/ProductClient 邊界；JsonDocument 在 assertion 結束前由 using 確定釋放。
    /// </summary>
    [Fact]
    public void Package01_fee_response_serializes_only_selected_safe_branch_without_odata_field_names()
    {
        var response = OperationResponseData.ForPackage01FeeRecords(
            OperationIds.FeeDedicationRetrieveByContact,
            "v9.1",
            [new Package01FeeRecord
            {
                FeeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Amount = 250m,
                PayWayLabel = "現金",
                CategoryLabel = "奉獻"
            }]);

        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("operationId").GetString().Should().Be(OperationIds.FeeDedicationRetrieveByContact);
        root.GetProperty("ceVersion").GetString().Should().Be("v9.1");
        root.GetProperty("responseKind").GetString().Should().Be(nameof(OperationResponseKind.Package01FeeRecords));
        root.TryGetProperty("feeRecords", out var feeRecords).Should().BeTrue();
        feeRecords.GetArrayLength().Should().Be(1);
        feeRecords[0].GetProperty("feeId").GetString().Should().Be("11111111-1111-1111-1111-111111111111");
        feeRecords[0].GetProperty("amount").GetDecimal().Should().Be(250m);
        root.TryGetProperty("whoAmI", out _).Should().BeFalse();
        root.TryGetProperty("storLessonRecords", out _).Should().BeFalse();
        json.Should().NotContain("new_feeid");
        json.Should().NotContain("@odata");
        json.Should().NotContain("@OData.Community.Display.V1.FormattedValue");
    }

    /// <summary>
    /// 驗證 stor-lesson branch 的 nullable compatibility 欄位與 fee amount null 值可以安全序列化，且不會
    /// 夾帶 fee branch。這保留 ProductClient DTO 的 null/default 相容性，同時讓後續 mapper 不必重新解析
    /// raw OData JSON 或延長任何上游 response stream 的生命週期。
    /// </summary>
    [Fact]
    public void Package01_stor_lesson_response_serializes_only_selected_safe_branch()
    {
        var response = OperationResponseData.ForPackage01StorLessonRecords(
            OperationIds.LessonsStorRetrieveByContact,
            "v8.2",
            [new Package01StorLessonRecord
            {
                StorLessonId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ContactName = "測試會友",
                FeeAmount = null
            }]);

        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("responseKind").GetString().Should().Be(nameof(OperationResponseKind.Package01StorLessonRecords));
        root.TryGetProperty("storLessonRecords", out var storLessonRecords).Should().BeTrue();
        storLessonRecords.GetArrayLength().Should().Be(1);
        storLessonRecords[0].GetProperty("storLessonId").GetString().Should().Be("22222222-2222-2222-2222-222222222222");
        storLessonRecords[0].GetProperty("feeAmount").ValueKind.Should().Be(JsonValueKind.Null);
        root.TryGetProperty("feeRecords", out _).Should().BeFalse();
        root.TryGetProperty("whoAmI", out _).Should().BeFalse();
        json.Should().NotContain("new_stor_lessonsid");
        json.Should().NotContain("@odata");
    }

    /// <summary>
    /// 確認成功結果只能持有封閉 envelope，而不是任意 object。這使失敗結果可維持 null data，成功結果則讓
    /// Gateway 與產品對 discriminated union 做明確驗證，避免把可延伸的上游 payload 留在長生命週期物件中。
    /// </summary>
    [Fact]
    public void Operation_execution_success_retains_only_closed_response_envelope()
    {
        var response = OperationResponseData.ForWhoAmI(
            OperationIds.RuntimeHealthWhoAmI,
            "v9.1",
            new WhoAmIResponseData
            {
                UserId = Guid.Parse("33333333-3333-3333-3333-333333333333")
            });

        var result = OperationExecutionResult.Success(response);

        result.Data.Should().BeSameAs(response);
    }

    /// <summary>
    /// Capability ID 與 revision hash 是 matrix/queue/audit 的固定鍵，必須維持可預測格式；本檢查只讀取
    /// immutable 字串，沒有配置可釋放資源或讓 registry 保留測試輸入。
    /// </summary>
    [Fact]
    public void Capability_operation_ids_match_required_pattern()
    {
        foreach (var definition in Package01OperationRegistry.All)
        {
            definition.CapabilityOperationId.Should().MatchRegex("^[a-z0-9]+(\\.[a-z0-9]+)+$");
            definition.TemplateHash.Should().HaveLength(64);
        }
    }
}
