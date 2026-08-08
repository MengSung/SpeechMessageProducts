// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Package02Data8ListManagementOperations.cs
// 用途：擁有 P7.2 Slice C 固定 static-list、small-group、contact-owner 與 transfer Data8 templates。
//
// 信任與生命週期邊界：
// 1. 本類別只接受 registry 已列出的五個 operation；Entity logical name、欄位、SDK message 與 read-back
//    投影均為 server-owned 常數，產品不能傳入 Entity、FetchXML、OrganizationRequest 或欄位 map。
// 2. 所有 SDK request、Entity 與集合都只活在目前 connector lease 的同步呼叫範圍；不保存 service、profile、
//    credential、session、timer、stream、buffer、cancellation registration 或 background task。
// 3. 寫入完成不等於成功；每個 capability 最終都必須由固定 read-back/reconciliation 證實。unknown timeout
//    交由上層 fixture reconciliation，絕不在此重送。
// ============================================================================

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// P7.2 Slice C 的 connector-internal template owner。目前先完成 static-list add/remove 的固定 SDK action；
/// 其餘三個 capability 會在各自 fail-first connector 測試建立後加入，避免未驗證的複合寫入提早可達。
/// </summary>
internal static class Package02Data8ListManagementOperations
{
    private const string Ce91 = "9.1";
    private const int MaximumMemberIds = 1000;

    /// <summary>
    /// 執行固定 <see cref="AddListMembersListRequest"/>。member array 會再次複製，確保即使 direct connector
    /// caller 繞過 executor，也不能在 SDK 呼叫期間改寫集合；成功 response 不攜帶 list/contact identity。
    /// </summary>
    internal static OperationResponseData ExecuteAddMembers(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ValidateOperation(service, operation, ceVersion, OperationIds.ListMembersAddMany);
        var listId = ReadGuid(operation.Parameters, "listId");
        var memberIds = ReadDistinctGuidArray(operation.Parameters, "memberIds");

        var baseline = ReadMemberships(service, listId, memberIds);
        if (baseline.Count == memberIds.Length)
        {
            return OperationResponseData.ForStaticListMembershipMutation(
                operation.OperationId,
                ceVersion,
                P72ControlledMutationDisposition.NoChange,
                P72ControlledMutationCorrelationCategory.NoDispatch);
        }

        service.Execute(new AddListMembersListRequest
        {
            ListId = listId,
            MemberIds = memberIds
        });

        if (ReadMemberships(service, listId, memberIds).Count != memberIds.Length)
        {
            throw new InvalidOperationException("The Data8 static-list add read-back is invalid.");
        }

        return OperationResponseData.ForStaticListMembershipMutation(
            operation.OperationId,
            ceVersion,
            P72ControlledMutationDisposition.Changed,
            P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
    }

    /// <summary>
    /// 執行固定 <see cref="RemoveMemberListRequest"/>。只接受一組 list/contact GUID，不公開 Delete 或任意
    /// relationship API；成功 response 不包含 SDK response、identity 或 transport metadata。
    /// </summary>
    internal static OperationResponseData ExecuteRemoveMember(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ValidateOperation(service, operation, ceVersion, OperationIds.ListMembersRemoveOne);
        var listId = ReadGuid(operation.Parameters, "listId");
        var memberId = ReadGuid(operation.Parameters, "memberId");
        if (ReadMemberships(service, listId, [memberId]).Count == 0)
        {
            return OperationResponseData.ForStaticListMembershipMutation(
                operation.OperationId,
                ceVersion,
                P72ControlledMutationDisposition.NoChange,
                P72ControlledMutationCorrelationCategory.NoDispatch);
        }

        service.Execute(new RemoveMemberListRequest
        {
            ListId = listId,
            EntityId = memberId
        });

        if (ReadMemberships(service, listId, [memberId]).Count != 0)
        {
            throw new InvalidOperationException("The Data8 static-list remove read-back is invalid.");
        }

        return OperationResponseData.ForStaticListMembershipMutation(
            operation.OperationId,
            ceVersion,
            P72ControlledMutationDisposition.Changed,
            P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
    }

    /// <summary>
    /// 驗證固定 CE 9.1、operation ID、registry kind/template/response/idempotency 與 exact parameter schema。
    /// registry 漂移或 direct connector caller 的額外欄位都在 SDK 呼叫前 fail closed。
    /// </summary>
    private static void ValidateOperation(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion,
        string expectedOperationId)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(ceVersion, Ce91, StringComparison.Ordinal) ||
            !string.Equals(operation.OperationId, expectedOperationId, StringComparison.Ordinal) ||
            !Package01OperationRegistry.TryGet(expectedOperationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.OperationKind, "action", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.StaticListMembershipMutation ||
            !string.Equals(definition.AuditRequirement, "write-audit", StringComparison.Ordinal) ||
            !string.Equals(definition.IdempotencyClass, "caller-idempotency-key-required", StringComparison.Ordinal) ||
            operation.Parameters is null ||
            operation.Parameters.Count != definition.Parameters.Count ||
            operation.Parameters.Keys.Any(name =>
                !definition.Parameters.Any(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException("The Data8 static-list operation is not permitted.");
        }

        var expectedTemplate = expectedOperationId switch
        {
            OperationIds.ListMembersAddMany => "list.members.add.many.v1",
            OperationIds.ListMembersRemoveOne => "list.members.remove.one.v1",
            _ => throw new InvalidOperationException("The Data8 static-list operation is not permitted.")
        };
        if (!string.Equals(definition.TemplateId, expectedTemplate, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 static-list registry definition is invalid.");
        }
    }

    /// <summary>讀取固定名稱的非空 GUID；string、EntityReference、JsonElement 或其他物件型別一律拒絕。</summary>
    private static Guid ReadGuid(IReadOnlyDictionary<string, object?> parameters, string name)
        => parameters.TryGetValue(name, out var value) && value is Guid guid && guid != Guid.Empty
            ? guid
            : throw new InvalidOperationException("The Data8 static-list GUID parameter is invalid.");

    /// <summary>
    /// 複製 1-1,000 筆 distinct non-empty GUID。即使 executor 已正規化，connector 仍重驗並建立自己的
    /// request-scope array，避免 direct caller 或共享 mutable list 在 SDK action 期間改寫 membership set。
    /// </summary>
    private static Guid[] ReadDistinctGuidArray(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) ||
            value is not IReadOnlyList<Guid> source ||
            source.Count is < 1 or > MaximumMemberIds)
        {
            throw new InvalidOperationException("The Data8 static-list member set is invalid.");
        }

        var copy = new Guid[source.Count];
        var seen = new HashSet<Guid>();
        for (var index = 0; index < copy.Length; index++)
        {
            var guid = source[index];
            if (guid == Guid.Empty || !seen.Add(guid))
            {
                throw new InvalidOperationException("The Data8 static-list member set is invalid.");
            }

            copy[index] = guid;
        }

        return copy;
    }

    /// <summary>
    /// 以固定 QueryExpression 讀取指定 list 與有限 contact set 的 membership。每列 logical name、listid 與
    /// entityid 都須精確相符；MoreRecords 或未預期 identity 代表 read-back 不完整，不能建構成功 response。
    /// </summary>
    private static HashSet<Guid> ReadMemberships(
        IOrganizationService service,
        Guid listId,
        IReadOnlyList<Guid> memberIds)
    {
        var query = new QueryExpression("listmember")
        {
            ColumnSet = new ColumnSet("listid", "entityid"),
            NoLock = true,
            TopCount = MaximumMemberIds
        };
        query.Criteria.AddCondition("listid", ConditionOperator.Equal, listId);
        query.Criteria.AddCondition("entityid", ConditionOperator.In, memberIds.Cast<object>().ToArray());
        var rows = service.RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The Data8 static-list read-back is invalid.");
        if (rows.MoreRecords || rows.Entities.Count > memberIds.Count)
        {
            throw new InvalidOperationException("The Data8 static-list read-back is invalid.");
        }

        var allowedMembers = memberIds.ToHashSet();
        var result = new HashSet<Guid>();
        foreach (var row in rows.Entities)
        {
            var entityId = row.GetAttributeValue<Guid>("entityid");
            if (!string.Equals(row.LogicalName, "listmember", StringComparison.Ordinal) ||
                row.GetAttributeValue<Guid>("listid") != listId ||
                entityId == Guid.Empty ||
                !allowedMembers.Contains(entityId) ||
                !result.Add(entityId))
            {
                throw new InvalidOperationException("The Data8 static-list read-back is invalid.");
            }
        }

        return result;
    }
}
