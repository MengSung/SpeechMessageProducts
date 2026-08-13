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
    /// 確認目前二十四個 registry capability 完整存在，避免 matrix、connector 與產品在 feature gate 尚未開啟前
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
            OperationIds.PaymentsDedicationRetrieveByContact,
            OperationIds.FeesEditorLoadByDiscipleLesson,
            OperationIds.LessonsStorRetrieveByContact,
            OperationIds.LessonsStorRetrieveByDiscipleLesson,
            OperationIds.AuthenticationContactRetrieveByAccount,
            OperationIds.AuthenticationContactRetrieveByLineId,
            OperationIds.MemberInfoContactUpdateBasicInfo,
            "memberinfo.contact.update.line.profile",
            "memberinfo.contact.count.ungrouped.commitment",
            "list.members.add.many",
            "list.members.remove.one",
            "listmanagement.smallgroup.update.fields",
            "contact.assign.owner",
            "newperson.contact.transfer.between.lists",
            OperationIds.MemberInfoContactRetrieveImage,
            OperationIds.MemberInfoContactUpdateImage,
            OperationIds.NewPersonContactUpdateImage,
            OperationIds.StatsMeetingRetrieveBySunday
        });

        ids.Should().HaveCount(24);
    }

    /// <summary>
    /// 保護 Slice B1/B2 只能以固定 LINE profile write 與 ungrouped commitment function 進入 allowlist。
    /// 故障注入是尚未宣告的 operation；決定性斷言是 B1 只能接受三組 set/clear/preserve scalar，B2 只能接受
    /// bounded search，且兩者各自選擇封閉 response discriminator。測試只讀 immutable registry，不建立 CRM
    /// service、connector lease、LINE token、session、stream 或背景工作。
    /// </summary>
    [Fact]
    public void Slice_b_operations_are_registered_with_closed_scalar_only_schemas()
    {
        Package01OperationRegistry.TryGet("memberinfo.contact.update.line.profile", out var line).Should().BeTrue();
        line!.OperationKind.Should().Be("write");
        line.TemplateId.Should().Be("memberinfo.contact.line.profile.patch.v1");
        line.ResponseKind.ToString().Should().Be("ContactLineProfileUpdate");
        line.IdempotencyClass.Should().Be("caller-idempotency-key-required");
        line.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required
        })
            .Should()
            .BeEquivalentTo(
                [
                    new { Name = "contactId", Type = "guid", Required = true },
                    new { Name = "pictureMode", Type = "enum", Required = true },
                    new { Name = "pictureUrl", Type = "string", Required = false },
                    new { Name = "statusMode", Type = "enum", Required = true },
                    new { Name = "statusMessage", Type = "string", Required = false },
                    new { Name = "displayNameMode", Type = "enum", Required = true },
                    new { Name = "displayName", Type = "string", Required = false }
                ],
                options => options.WithStrictOrdering());

        Package01OperationRegistry.TryGet("memberinfo.contact.count.ungrouped.commitment", out var count).Should().BeTrue();
        count!.OperationKind.Should().Be("function");
        count.TemplateId.Should().Be("memberinfo.contact.ungrouped.commitment.count.v1");
        count.ResponseKind.ToString().Should().Be("UngroupedCommitmentCounts");
        count.IdempotencyClass.Should().Be("read-only");
        count.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required
        })
            .Should()
            .Equal(new { Name = "search", Type = "string", Required = false });
    }

    /// <summary>
    /// 保護 P7.2 Slice C 不會把 static-list association、小組固定欄位、contact owner 指派與新人轉組降級成
    /// generic Entity／FetchXML／OrganizationRequest 通道。故障注入是任何不在五個封閉 schema 中的欄位或
    /// response kind；決定性斷言是每個 operation 只宣告指定的 GUID、mode、日期與 bounded guid-array，且
    /// write/action 都要求 caller idempotency key。測試只讀取 immutable registry，不建立 CRM service、
    /// connector lease、credential、session 或背景資源。
    /// </summary>
    [Fact]
    public void Slice_c_operations_are_registered_with_closed_fixed_schemas_and_response_branches()
    {
        var expected = new[]
        {
            new
            {
                Id = "list.members.add.many",
                Kind = "action",
                Template = "list.members.add.many.v1",
                Response = "StaticListMembershipMutation",
                Parameters = new[] { "listId:guid:True", "memberIds:guid-array:True" }
            },
            new
            {
                Id = "list.members.remove.one",
                Kind = "action",
                Template = "list.members.remove.one.v1",
                Response = "StaticListMembershipMutation",
                Parameters = new[] { "listId:guid:True", "memberId:guid:True" }
            },
            new
            {
                Id = "listmanagement.smallgroup.update.fields",
                Kind = "write",
                Template = "listmanagement.smallgroup.fixed.fields.v1",
                Response = "SmallGroupFixedFieldsMutation",
                Parameters = new[] { "listId:guid:True", "mode:enum:True", "targetLeaderContactId:guid:True" }
            },
            new
            {
                Id = "contact.assign.owner",
                Kind = "action",
                Template = "contact.assign.owner.v1",
                Response = "ContactOwnerAssignment",
                Parameters = new[] { "contactId:guid:True", "ownerSystemUserId:guid:True" }
            },
            new
            {
                Id = "newperson.contact.transfer.between.lists",
                Kind = "write",
                Template = "newperson.contact.transfer.between.lists.v1",
                Response = "ContactListTransfer",
                Parameters = new[]
                {
                    "contactId:guid:True",
                    "sourceListId:guid:False",
                    "targetListId:guid:True",
                    "weekStartDate:date-time:True",
                    "ownerSystemUserId:guid:False"
                }
            }
        };

        foreach (var item in expected)
        {
            Package01OperationRegistry.TryGet(item.Id, out var definition).Should().BeTrue();
            definition!.OperationKind.Should().Be(item.Kind);
            definition.TemplateId.Should().Be(item.Template);
            definition.ResponseKind.ToString().Should().Be(item.Response);
            definition.IdempotencyClass.Should().Be("caller-idempotency-key-required");
            definition.Parameters.Select(parameter =>
                    $"{parameter.Name}:{parameter.Type}:{parameter.Required}")
                .Should()
                .Equal(item.Parameters);
        }

        Enum.GetNames<OperationResponseKind>().Should().Contain(new[]
        {
            "StaticListMembershipMutation",
            "SmallGroupFixedFieldsMutation",
            "ContactOwnerAssignment",
            "ContactListTransfer"
        });
    }

    /// <summary>
    /// 保護 P7.3 五項特殊資源能力必須各自擁有固定 operation ID、有限 policy 與封閉 response branch，不能把
    /// image、metadata 或 page 結果退化成 generic object、SDK Entity、raw stream、FetchXML 或 caller-selected
    /// schema。故障模型是 registry 尚未宣告這些 capability；決定性斷言是每個 operation 的 kind、template、
    /// idempotency 與具名參數都與安全合約一致。測試只讀 process-static registry，不建立 CRM client、buffer、
    /// stream、cache、connector lease、session 或背景資源。
    /// </summary>
    [Fact]
    public void P7_3_special_resource_operations_are_registered_with_closed_bounded_schemas()
    {
        var expected = new[]
        {
            new
            {
                Id = "memberinfo.contact.retrieve.image",
                Kind = "read",
                Template = "memberinfo.contact.entityimage.retrieve.v1",
                Response = "ContactImage",
                Idempotency = "read-only",
                Parameters = new[] { "contactId:guid:True" }
            },
            new
            {
                Id = "memberinfo.contact.update.image",
                Kind = "write",
                Template = "memberinfo.contact.entityimage.update.v1",
                Response = "ContactImageUpdate",
                Idempotency = "caller-idempotency-key-required",
                Parameters = new[] { "contactId:guid:True", "imagePayload:image-payload:True" }
            },
            new
            {
                Id = "newperson.contact.update.image",
                Kind = "write",
                Template = "newperson.contact.entityimage.update.v1",
                Response = "ContactImageUpdate",
                Idempotency = "caller-idempotency-key-required",
                Parameters = new[] { "contactId:guid:True", "imagePayload:image-payload:True" }
            },
            new
            {
                Id = "metadata.optionset.retrieve.by.attribute",
                Kind = "metadata",
                Template = "metadata.optionset.by.attribute.v2",
                Response = "OptionSetOptions",
                Idempotency = "read-only",
                Parameters = new[] { "target:metadata-optionset-target:True" }
            },
            new
            {
                Id = "stats.meeting.retrieve.by.sunday",
                Kind = "read",
                Template = "stats.meeting.by.sunday.v1",
                Response = "MeetingStatistics",
                Idempotency = "read-only",
                Parameters = new[] { "sundayDate:date-time:True" }
            }
        };

        foreach (var item in expected)
        {
            Package01OperationRegistry.TryGet(item.Id, out var definition).Should().BeTrue();
            definition!.OperationKind.Should().Be(item.Kind);
            definition.TemplateId.Should().Be(item.Template);
            definition.ResponseKind.ToString().Should().Be(item.Response);
            definition.IdempotencyClass.Should().Be(item.Idempotency);
            definition.Parameters.Select(parameter =>
                    $"{parameter.Name}:{parameter.Type}:{parameter.Required}")
                .Should()
                .Equal(item.Parameters);
            definition.MaximumPageCount.Should().Be(ConservativeMaximumPageCount);
            definition.MaximumPageBytes.Should().Be(ConservativeMaximumPageBytes);
            definition.MaximumCumulativeResponseBytes.Should().Be(ConservativeMaximumCumulativeResponseBytes);
            definition.MaximumResultItemCount.Should().Be(ConservativeMaximumResultItemCount);
        }
    }

    /// <summary>
    /// 保護 P7.3 response envelope 對 image、metadata 與 meeting 結果仍維持「剛好一個 branch」規則。故障注入
    /// 是呼叫端在 image branch 混入 metadata 集合，或在 construction 後改動 image source bytes；決定性斷言是
    /// constructor 拒絕混合 branch，且兩次讀取 image bytes 都是互不共享的 defensive copy。此測試不建立
    /// decoder、stream、CRM client、cache、lease 或 session，所有短生命週期 JSON 文件都在 method scope dispose。
    /// </summary>
    [Fact]
    public void P7_3_response_union_rejects_mixed_branches_and_defensively_copies_image_bytes()
    {
        var source = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var response = OperationResponseData.ForContactImage(
            "memberinfo.contact.retrieve.image",
            "9.1",
            new ContactImageResponseData(source, ContactImageMediaKind.Png));
        source[0] = 0;

        response.ContactImage!.GetImageBytes().Should().Equal(0x89, 0x50, 0x4E, 0x47);
        var first = response.ContactImage.GetImageBytes();
        first[0] = 0;
        response.ContactImage.GetImageBytes().Should().Equal(0x89, 0x50, 0x4E, 0x47);

        var invalid = () => new OperationResponseData(
            "memberinfo.contact.retrieve.image",
            "9.1",
            OperationResponseKind.ContactImage,
            contactImage: new ContactImageResponseData(source, ContactImageMediaKind.Png),
            optionSetOptions: [new OptionSetOptionRecord { Value = 1, Label = "測試", ConfiguredOrder = 0 }]);

        invalid.Should().Throw<ArgumentException>();
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
    /// 保護 P7.2 的會友基本資料更新只能以固定 capability 與四個具名欄位進入 registry；故障注入是目前尚未登錄的
    /// operation ID，決定性斷言是 registry 必須拒絕任意欄位 map，並且未來只允許 contact ID、電話、地址與兩個
    /// OptionSet scalar。此測試只讀取 process-static 定義，不建立 CRM client、連線、背景工作或跨測試 retained state。
    /// </summary>
    [Fact]
    public void Contact_basic_info_update_is_registered_with_a_closed_field_limited_schema()
    {
        const string operationId = "memberinfo.contact.update.basic.info";

        Package01OperationRegistry.TryGet(operationId, out var definition).Should().BeTrue();
        definition!.OperationKind.Should().Be("write");
        definition.TemplateKind.Should().Be("odata-route");
        definition.TemplateId.Should().Be("memberinfo.contact.basic.info.patch.v1");
        Enum.GetNames<OperationResponseKind>().Should().Contain("ContactBasicInfoUpdate");
        definition.ResponseKind.ToString().Should().Be("ContactBasicInfoUpdate");
        definition.IdempotencyClass.Should().Be("caller-idempotency-key-required");
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
                    new { Name = "contactId", Type = "guid", Required = true, EncodingContext = "odata-uri-segment" },
                    new { Name = "phone", Type = "string", Required = false, EncodingContext = "json-body" },
                    new { Name = "address", Type = "string", Required = false, EncodingContext = "json-body" },
                    new { Name = "membershipStatusValue", Type = "integer", Required = false, EncodingContext = "json-body" },
                    new { Name = "spiritualIdentityValue", Type = "integer", Required = false, EncodingContext = "json-body" }
                ],
                options => options.WithStrictOrdering());
    }

    /// <summary>
    /// 驗證每個已登錄 capability 都宣告封閉回應 discriminator 與同一組保守、有限的 page/byte 上限。
    /// 四頁、每頁 64 KiB 且累積 256 KiB 讓關閉中的 Package 1 與 P7.3 特殊資源在尚無實測容量證據前保持小而
    /// 可預測的記憶體與 credential-bearing request 範圍；metadata 只回傳封閉 OptionSet pure-value projection，
    /// 不可傳遞 raw metadata graph。
    /// </summary>
    [Theory]
    [InlineData(OperationIds.RuntimeHealthWhoAmI, OperationResponseKind.WhoAmI)]
    [InlineData(OperationIds.RuntimePoolValidateConnection, OperationResponseKind.WhoAmI)]
    [InlineData(OperationIds.MetadataOptionSetByAttribute, OperationResponseKind.OptionSetOptions)]
    [InlineData(OperationIds.FeeDedicationRetrieveByContact, OperationResponseKind.Package01FeeRecords)]
    [InlineData(OperationIds.FeeDedicationRetrieveByContactDateRange, OperationResponseKind.Package01FeeRecords)]
    [InlineData(OperationIds.FeesRetrieveByDedicationPeriod, OperationResponseKind.Package01FeeRecords)]
    [InlineData(OperationIds.FeesEditorLoadByDiscipleLesson, OperationResponseKind.Package01StorLessonRecords)]
    [InlineData(OperationIds.LessonsStorRetrieveByContact, OperationResponseKind.Package01StorLessonRecords)]
    [InlineData(OperationIds.LessonsStorRetrieveByDiscipleLesson, OperationResponseKind.Package01StorLessonRecords)]
    [InlineData(OperationIds.MemberInfoContactUpdateBasicInfo, OperationResponseKind.ContactBasicInfoUpdate)]
    [InlineData(OperationIds.MemberInfoContactRetrieveImage, OperationResponseKind.ContactImage)]
    [InlineData(OperationIds.MemberInfoContactUpdateImage, OperationResponseKind.ContactImageUpdate)]
    [InlineData(OperationIds.NewPersonContactUpdateImage, OperationResponseKind.ContactImageUpdate)]
    [InlineData(OperationIds.StatsMeetingRetrieveBySunday, OperationResponseKind.MeetingStatistics)]
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
    /// 保護 P7.2 會友基本資料寫入回應必須是另一個封閉 union branch。故障注入是目前尚未實作的
    /// factory／discriminator 型別；決定性斷言是 factory 只能接受具名的 changed/no-change 與 read-back
    /// correlation enum，並且序列化後不含 contact ID、電話、地址、OptionSet、CRM logical name、URL、token、
    /// cookie、baseline 或任何原始 connector 回應。測試只反射 immutable contract metadata，未建立 CRM client、
    /// connector lease、網路連線、背景工作或跨測試 retained state。
    /// </summary>
    [Fact]
    public void Contact_basic_info_update_response_exposes_only_safe_outcome_and_correlation_category()
    {
        var factory = typeof(OperationResponseData).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .SingleOrDefault(method => string.Equals(
                method.Name,
                "ForContactBasicInfoUpdate",
                StringComparison.Ordinal));

        factory.Should().NotBeNull("P7.2 寫入結果必須有封閉 factory，不能以自由字典或 raw CRM payload 組裝");
        var parameters = factory!.GetParameters();
        parameters.Select(parameter => parameter.ParameterType.Name).Should().Equal(
            "String",
            "String",
            "ContactBasicInfoUpdateDisposition",
            "ContactBasicInfoUpdateCorrelationCategory");

        var disposition = Enum.Parse(parameters[2].ParameterType, "Changed");
        var correlationCategory = Enum.Parse(parameters[3].ParameterType, "ReadBackConfirmed");
        var response = factory.Invoke(
            null,
            [
                OperationIds.MemberInfoContactUpdateBasicInfo,
                "v9.1",
                disposition,
                correlationCategory
            ]) as OperationResponseData;

        response.Should().NotBeNull();
        var json = JsonSerializer.Serialize(response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("responseKind").GetString().Should().Be(nameof(OperationResponseKind.ContactBasicInfoUpdate));
        root.TryGetProperty("contactBasicInfoUpdate", out var update).Should().BeTrue();
        update.GetProperty("disposition").GetString().Should().Be("Changed");
        update.GetProperty("correlationCategory").GetString().Should().Be("ReadBackConfirmed");
        root.TryGetProperty("feeRecords", out _).Should().BeFalse();
        root.TryGetProperty("storLessonRecords", out _).Should().BeFalse();
        root.TryGetProperty("whoAmI", out _).Should().BeFalse();
        json.Should().NotContain("contactId");
        json.Should().NotContain("mobilephone");
        json.Should().NotContain("address2_line1");
        json.Should().NotContain("customertypecode");
        json.Should().NotContain("new_spiriitual_identity");
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
