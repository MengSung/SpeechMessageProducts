/*
 * 檔案整體說明
 *
 * 目的與定位：保護 ChurchSettings Dataverse adapter 新增類別時回傳資料庫正式主鍵的契約。
 * 主要功能與流程：以測試專屬 DispatchProxy 替代 IOrganizationService.Execute，驗證 store
 * 只接受 UpsertResponse，並從 response.Target 取得 Dataverse 指派的實體 ID，而不是使用
 * 尚未持有主鍵的 request Entity。另注入錯誤 response 與空 Target ID，確認失敗明確且不偽造身分。
 * 使用邊界與維護：所有資料均為固定合成 GUID；不建立 D365、網路、檔案、快取或背景工作。
 * proxy 僅由單一測試持有，Dispose 清除回應參照；新增 SDK 操作預設拒絕，避免測試假綠。
 * 故障與決定性斷言：UpsertResponse.Target.Id 必須原樣回傳；非 UpsertResponse 或空 ID
 * 必須丟出可診斷 InvalidOperationException，且不得回傳 request Entity 的空 ID。
 */
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Areas.ChurchSettings;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Settings;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>以真正 Dataverse message response 形狀驗證類別新增的穩定資料庫身分。</summary>
public sealed class DataverseChurchSettingsStoreTests
{
    /// <summary>Upsert 由 Dataverse response 提供正式 ID；request Entity 的 ID 刻意保持空值。</summary>
    [Fact]
    public async Task Store_AddCategory_ReturnsUpsertResponseTargetId()
    {
        var expectedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var fixture = new StoreFixture();
        fixture.Sdk.Response = CreateUpsertResponse(expectedId);

        var id = await fixture.Store.AddCategoryAsync(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "custom-a1b2c3d4",
            "合成類別",
            1,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(expectedId, id);
        Assert.NotNull(fixture.Sdk.Request);
        Assert.Equal(Guid.Empty, fixture.Sdk.Request!.Id);
    }

    /// <summary>SDK 回傳錯誤 message 型別時必須 fail closed 並提供穩定診斷。</summary>
    [Fact]
    public async Task Store_AddCategory_UnexpectedResponse_ThrowsDiagnosticException()
    {
        using var fixture = new StoreFixture();
        fixture.Sdk.Response = new OrganizationResponse();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.AddCategoryAsync(Guid.NewGuid(), "custom-a1b2c3d4", "合成類別", 1,
                null, null, CancellationToken.None));

        Assert.Contains("UpsertResponse", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Upsert response 沒有正式 Target ID 時不得洩漏空或推測的資料列身分。</summary>
    [Fact]
    public async Task Store_AddCategory_EmptyTargetId_ThrowsDiagnosticException()
    {
        using var fixture = new StoreFixture();
        fixture.Sdk.Response = CreateUpsertResponse(Guid.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.AddCategoryAsync(Guid.NewGuid(), "custom-a1b2c3d4", "合成類別", 1,
                null, null, CancellationToken.None));

        Assert.Contains("Target", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 稽核只能從目前教會的覆寫與類別記錄 ID 反查最近 200 筆，並只發布白名單欄位及使用者顯示名稱。
    /// 假 SDK 同時提供另一教會的候選資料；決定性斷言確認它不會進入 Audit 的 objectid 條件。
    /// </summary>
    [Fact]
    public async Task Store_ReadAudit_IsChurchScopedBoundedAndProjectsActorName()
    {
        var churchId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var foreignChurchId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var localCategoryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var foreignCategoryId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var auditId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var actorId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var changedOnUtc = new DateTime(2026, 11, 21, 1, 2, 3, DateTimeKind.Utc);
        using var fixture = new AuditStoreFixture();
        fixture.Sdk.CategoryRows.Add(new Entity("new_offeringcategory", localCategoryId)
        {
            ["new_accountid"] = new EntityReference("account", churchId)
        });
        fixture.Sdk.CategoryRows.Add(new Entity("new_offeringcategory", foreignCategoryId)
        {
            ["new_accountid"] = new EntityReference("account", foreignChurchId)
        });
        fixture.Sdk.AuditRows.Add(new Entity("audit", auditId)
        {
            ["objectid"] = new EntityReference("new_offeringcategory", localCategoryId),
            ["createdon"] = changedOnUtc,
            ["userid"] = new EntityReference("systemuser", actorId)
        });
        fixture.Sdk.ActorRows.Add(new Entity("systemuser", actorId) { ["fullname"] = "合成管理員" });
        fixture.Sdk.AuditDetail = new AttributeAuditDetail
        {
            AuditRecord = fixture.Sdk.AuditRows[0],
            OldValue = new Entity("new_offeringcategory")
            {
                ["new_name"] = "舊名稱",
                ["new_sortorder"] = 1
            },
            NewValue = new Entity("new_offeringcategory")
            {
                ["new_name"] = "新名稱",
                ["new_sortorder"] = 2
            }
        };

        var records = await fixture.Store.ReadAuditAsync(churchId, 200, CancellationToken.None);

        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.NotEqual(Guid.Empty, record.Id));
        Assert.Equal(records.Count, records.Select(record => record.Id).Distinct().Count());
        var record = Assert.Single(records.Where(item => item.Field == "new_name"));
        Assert.Equal(churchId, record.ChurchId);
        Assert.Equal(localCategoryId,
            typeof(ChurchSettingsAuditRecord).GetProperty("TargetRecordId")?.GetValue(record));
        Assert.Equal(changedOnUtc, record.ChangedOnUtc);
        Assert.Equal("舊名稱", record.Before);
        Assert.Equal("新名稱", record.After);
        Assert.Equal("合成管理員", record.ActorDisplayName);
        Assert.Contains(fixture.Sdk.Queries, query => query.EntityName == "new_offeringcategory"
            && query.TopCount == 200
            && query.Criteria.Conditions.Any(condition => condition.AttributeName == "new_accountid"
                && condition.Values.Contains(churchId)));
        var auditQuery = Assert.Single(fixture.Sdk.Queries.Where(query => query.EntityName == "audit"));
        Assert.Equal(200, auditQuery.TopCount);
        var objectIdCondition = Assert.Single(auditQuery.Criteria.Conditions
            .Where(condition => condition.AttributeName == "objectid"));
        Assert.Equal(ConditionOperator.In, objectIdCondition.Operator);
        Assert.Contains(localCategoryId, objectIdCondition.Values);
        Assert.DoesNotContain(foreignCategoryId, objectIdCondition.Values);

        var repeated = await fixture.Store.ReadAuditAsync(churchId, 200, CancellationToken.None);
        Assert.Equal(records.Select(item => item.Id).OrderBy(id => id),
            repeated.Select(item => item.Id).OrderBy(id => id));
    }

    /// <summary>名詞覆寫先在資料來源套用 account scope，並拒絕重複 key/culture 候選。</summary>
    [Fact]
    public async Task Store_ReadTermOverrides_IsChurchScopedBoundedAndUnique()
    {
        var churchId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var foreignChurchId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var localId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var fixture = new AuditStoreFixture();
        fixture.Sdk.TermRows.Add(new Entity("new_localizedtext", localId)
        {
            ["new_accountid"] = new EntityReference("account", churchId),
            ["new_key"] = "Role.Member",
            ["new_culture"] = "zh-TW",
            ["new_text"] = "本教會覆寫"
        });
        fixture.Sdk.TermRows.Add(new Entity("new_localizedtext", Guid.NewGuid())
        {
            ["new_accountid"] = new EntityReference("account", foreignChurchId),
            ["new_key"] = "Role.Member",
            ["new_culture"] = "zh-TW",
            ["new_text"] = "外教會覆寫"
        });

        var rows = await fixture.Store.ReadTermOverridesAsync(churchId, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(localId, row.Id);
        Assert.Equal(churchId, row.ChurchId);
        Assert.Equal("本教會覆寫", row.Text);
        var query = Assert.Single(fixture.Sdk.Queries);
        Assert.Equal("new_localizedtext", query.EntityName);
        Assert.Equal(200, query.TopCount);
        Assert.Contains(query.Criteria.Conditions, condition =>
            condition.AttributeName == "new_accountid" && condition.Values.Contains(churchId));

        fixture.Sdk.TermRows.Add(new Entity("new_localizedtext", Guid.NewGuid())
        {
            ["new_accountid"] = new EntityReference("account", churchId),
            ["new_key"] = "Role.Member",
            ["new_culture"] = "zh-TW",
            ["new_text"] = "重複覆寫"
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.ReadTermOverridesAsync(churchId, CancellationToken.None));
    }

    private static UpsertResponse CreateUpsertResponse(Guid id)
    {
        var response = new UpsertResponse();
        response.Results["Target"] = new EntityReference("new_offeringcategory", id);
        response.Results["RecordCreated"] = true;
        return response;
    }

    /// <summary>擁有單次 fake 與 store；釋放只清合成參照，不觸碰任何外部連線。</summary>
    private sealed class StoreFixture : IDisposable
    {
        internal StoreFixture()
        {
            var service = DispatchProxy.Create<IOrganizationService, UpsertSdkProxy>();
            Sdk = (UpsertSdkProxy)(object)service;
            Store = new DataverseChurchSettingsStore(service);
        }

        internal UpsertSdkProxy Sdk { get; }
        internal DataverseChurchSettingsStore Store { get; }

        public void Dispose()
        {
            Sdk.Request = null;
            Sdk.Response = null;
        }
    }

    /// <summary>擁有合成 Audit SDK 與 store；Dispose 清空候選資料，不建立或保留外部服務。</summary>
    private sealed class AuditStoreFixture : IDisposable
    {
        internal AuditStoreFixture()
        {
            var service = DispatchProxy.Create<IOrganizationService, AuditSdkProxy>();
            Sdk = (AuditSdkProxy)(object)service;
            Store = new DataverseChurchSettingsStore(service);
        }

        internal AuditSdkProxy Sdk { get; }
        internal DataverseChurchSettingsStore Store { get; }

        public void Dispose()
        {
            Sdk.CategoryRows.Clear();
            Sdk.AuditRows.Clear();
            Sdk.ActorRows.Clear();
            Sdk.TermRows.Clear();
            Sdk.Queries.Clear();
            Sdk.AuditDetail = null;
        }
    }

    /// <summary>只模擬 ChurchSettings 稽核流程所需的三種限定查詢及 RetrieveAuditDetails。</summary>
    public class AuditSdkProxy : DispatchProxy
    {
        internal List<Entity> CategoryRows { get; } = new();
        internal List<Entity> AuditRows { get; } = new();
        internal List<Entity> ActorRows { get; } = new();
        internal List<Entity> TermRows { get; } = new();
        internal List<QueryExpression> Queries { get; } = new();
        internal AttributeAuditDetail? AuditDetail { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IOrganizationService.RetrieveMultiple)
                && args is { Length: > 0 }
                && args[0] is QueryExpression query)
            {
                Queries.Add(query);
                var rows = query.EntityName switch
                {
                    "new_offeringcategory" => FilterByChurch(query, CategoryRows),
                    "new_localizedtext" => FilterByChurch(query, TermRows),
                    "audit" => AuditRows,
                    "systemuser" => ActorRows,
                    _ => throw new InvalidOperationException("Unexpected query in audit contract test.")
                };
                return new EntityCollection(rows);
            }

            if (targetMethod?.Name == nameof(IOrganizationService.Execute)
                && args is { Length: > 0 }
                && args[0] is RetrieveAuditDetailsRequest)
            {
                if (AuditDetail is null)
                    throw new InvalidOperationException("Synthetic audit detail was not configured.");
                var response = new RetrieveAuditDetailsResponse();
                response.Results["AuditDetail"] = AuditDetail;
                return response;
            }

            throw new InvalidOperationException("Unexpected SDK operation in audit contract test.");
        }

        private static List<Entity> FilterByChurch(QueryExpression query, List<Entity> rows)
        {
            var condition = query.Criteria.Conditions
                .SingleOrDefault(item => item.AttributeName == "new_accountid");
            if (condition is null) return rows;
            var churchId = Assert.IsType<Guid>(Assert.Single(condition.Values));
            return rows.Where(row =>
                row.GetAttributeValue<EntityReference>("new_accountid")?.Id == churchId).ToList();
        }
    }

    /// <summary>只處理本案例預期的 Execute(UpsertRequest)，未知 SDK 呼叫一律拒絕。</summary>
    public class UpsertSdkProxy : DispatchProxy
    {
        internal Entity? Request { get; set; }
        internal OrganizationResponse? Response { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IOrganizationService.Execute)
                && args is { Length: > 0 }
                && args[0] is UpsertRequest request)
            {
                Request = request.Target;
                return Response ?? throw new InvalidOperationException("Synthetic response was not configured.");
            }
            throw new InvalidOperationException("Unexpected SDK operation in category identity test.");
        }
    }
}
