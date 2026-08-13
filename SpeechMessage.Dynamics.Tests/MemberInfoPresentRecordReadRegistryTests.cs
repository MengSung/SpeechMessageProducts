// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MemberInfoPresentRecordReadRegistryTests.cs
// 用途：驗證 ORG-CALL-00026 的伺服器所有 registry 契約與封閉 wire response union。
//
// 安全與生命週期邊界：
// 1. 測試只建立純量 request-local DTO；不會建立 CE 連線、fixture、SDK Entity、client 或背景工作。
// 2. registry 只能宣告已授權 contact 的 GUID，不能讓呼叫端傳入 profile、endpoint、credential、connector、
//    排序或查詢內容，避免未驗證的路由資訊成為跨使用者或跨租戶資料存取權限。
// 3. response factory 必須快照複製呼叫端集合；集合後續變動不得令另一個 request 看見先前 response 的列。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 ORG-CALL-00026 的 operation registry 與 response union 是否維持封閉且可稽核的資料邊界。
/// 此測試保護的契約是：呼叫端僅能傳送已授權 contact locator，而 connector、ProductClient 與 MVC consumer
/// 只能接收 immutable 的純量出席紀錄列。任何未宣告的 capability branch、可變集合保留或無界回應皆必須
/// 在 abstraction 邊界失敗關閉，避免個人資料跨 request、profile 或 tenant 外洩。
/// </summary>
public sealed class MemberInfoPresentRecordReadRegistryTests
{
    private const string OperationId = "memberinfo.present.retrieve.by.contact";
    private const string TemplateId = "memberinfo.present.by.contact.v1";

    /// <summary>
    /// 驗證 registry 精確宣告 server-owned dispatch schema、固定 template 與單頁限制。
    /// 失敗注入包含將 capability contract 意外擴充為其他參數、template、response kind 或 paging policy；
    /// 決定性斷言確認唯一必要輸入是經上層授權的 contact GUID，且操作仍是可稽核的 read-only 個資讀取。
    /// </summary>
    [Fact]
    public void ORG_CALL_00026_registry_declares_the_exact_contact_present_record_read_contract()
    {
        OperationIds.MemberInfoPresentRetrieveByContact.Should().Be(OperationId);
        Package01OperationRegistry.TryGet(OperationId, out var definition).Should().BeTrue();

        definition.Should().NotBeNull();
        definition!.OperationKind.Should().Be("read");
        definition.TemplateKind.Should().Be("queryexpression");
        definition.TemplateId.Should().Be(TemplateId);
        definition.ResponseKind.Should().Be(OperationResponseKind.MemberInfoPresentRecordReadRecords);
        definition.DataClassification.Should().Be("personal-data");
        definition.AuditRequirement.Should().Be("read-audit");
        definition.IdempotencyClass.Should().Be("read-only");
        definition.MaximumPageCount.Should().Be(1);
        definition.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required,
            parameter.EncodingContext
        }).Should().BeEquivalentTo(
            [
                new
                {
                    Name = "contactId",
                    Type = "guid",
                    Required = true,
                    EncodingContext = "queryexpression-condition"
                }
            ],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// 驗證 response envelope 立即複製輸入列，並拒絕重複或空白 ID、超限文字、無效日期、過大回應與多重 branch。
    /// 此測試以呼叫端在 factory 後清空原始 List 模擬可變集合污染，並以各種不合法列模擬 connector 或 transport
    /// fault；決定性斷言是有效快照仍保有原始列，而所有不合法輸入都在建立 envelope 前拋出例外，不會發布 partial
    /// response 或保存任何共享資源。
    /// </summary>
    [Fact]
    public void Present_record_response_branch_defensively_copies_and_fail_closes_invalid_or_multiple_branches()
    {
        var presentRecordId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var rows = new List<MemberInfoPresentRecordReadRecord>
        {
            CreateValidRow(presentRecordId)
        };

        var response = OperationResponseData.ForMemberInfoPresentRecordReadRecords(
            OperationId,
            "v9.1",
            rows);

        rows.Clear();
        response.ResponseKind.Should().Be(OperationResponseKind.MemberInfoPresentRecordReadRecords);
        response.MemberInfoPresentRecordReadRecords.Should().ContainSingle();
        response.MemberInfoPresentRecordReadRecords![0].PresentRecordId.Should().Be(presentRecordId);

        var duplicate = new[] { CreateValidRow(presentRecordId), CreateValidRow(presentRecordId) };
        var emptyId = new[] { CreateValidRow(Guid.Empty) };
        var oversizedText = new[] { CreateValidRow(Guid.NewGuid(), prayItem: new string('禱', 513)) };
        var invalidDate = new[] { CreateValidRow(Guid.NewGuid(), sundayDate: new DateTime(1, 1, 1)) };
        var oversizedResponse = Enumerable.Range(0, 128)
            .Select(_ => CreateValidRow(Guid.NewGuid(), prayItem: new string('禱', 512)) with
            {
                ContactFullName = new string('名', 512)
            })
            .ToArray();

        Action createDuplicate = () => OperationResponseData.ForMemberInfoPresentRecordReadRecords(OperationId, "v9.1", duplicate);
        Action createEmptyId = () => OperationResponseData.ForMemberInfoPresentRecordReadRecords(OperationId, "v9.1", emptyId);
        Action createOversizedText = () => OperationResponseData.ForMemberInfoPresentRecordReadRecords(OperationId, "v9.1", oversizedText);
        Action createInvalidDate = () => OperationResponseData.ForMemberInfoPresentRecordReadRecords(OperationId, "v9.1", invalidDate);
        Action createOversizedResponse = () => OperationResponseData.ForMemberInfoPresentRecordReadRecords(OperationId, "v9.1", oversizedResponse);
        Action createMultipleBranches = () => new OperationResponseData(
            OperationId,
            "v9.1",
            OperationResponseKind.MemberInfoPresentRecordReadRecords,
            feeRecords: [],
            memberInfoPresentRecordReadRecords: [CreateValidRow(Guid.NewGuid())]);

        createDuplicate.Should().Throw<ArgumentException>();
        createEmptyId.Should().Throw<ArgumentException>();
        createOversizedText.Should().Throw<ArgumentException>();
        createInvalidDate.Should().Throw<ArgumentException>();
        createOversizedResponse.Should().Throw<ArgumentException>();
        createMultipleBranches.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// 建立一筆符合封閉純量合約的測試列，不包含 CRM Entity、contact session、profile、credential、lease、
    /// stream 或 cancellation token。<paramref name="sundayDate" /> 保留 <see cref="DateTime" /> 語意，
    /// 不在測試 helper 進行 UTC 或本機時區轉換，藉此保護既有 Sunday-date 顯示相容性。
    /// </summary>
    /// <param name="presentRecordId">不可為空且在單一 response 中唯一的出席紀錄識別碼。</param>
    /// <param name="prayItem">可為 <see langword="null" /> 的有界代禱文字。</param>
    /// <param name="sundayDate">可為 <see langword="null" /> 的 Sunday 日期；未指定時使用一般有效日期。</param>
    /// <returns>可安全交給 response union 建立 request-local 快照的純量出席紀錄列。</returns>
    private static MemberInfoPresentRecordReadRecord CreateValidRow(
        Guid presentRecordId,
        string? prayItem = "代禱內容",
        DateTime? sundayDate = null)
        => new()
        {
            PresentRecordId = presentRecordId,
            ContactFullName = "測試姓名",
            SundayDate = sundayDate ?? new DateTime(2026, 8, 9),
            Sunday = true,
            SmallGroup = false,
            PrayItem = prayItem
        };
}
