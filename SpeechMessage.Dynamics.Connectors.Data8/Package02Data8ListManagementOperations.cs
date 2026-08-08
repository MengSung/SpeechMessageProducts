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
/// P7.2 Slice C 的 connector-internal template owner。所有 operation 都是固定商業語意；本類別不提供
/// generic CRUD、任意欄位 map、FetchXML 或 OrganizationRequest 代理。每個 mutation 都在同一個 lease scope
/// 完成 pre-read、單一 action/update（必要時）與完整 read-back，unknown timeout 絕不盲目重送。
/// </summary>
internal static class Package02Data8ListManagementOperations
{
    private const string Ce91 = "9.1";
    private const int MaximumMemberIds = 1000;
    private const int MembershipQueryChunkSize = 500;
    private const string ListEntityName = "list";
    private const string ContactEntityName = "contact";
    private const string SystemUserEntityName = "systemuser";
    private const string WeeklyReportEntityName = "new_group_present_weekly_report";
    private const string PresentRecordEntityName = "new_present_record";
    private const string WeeklyReportListAttribute = "new_list_group_present_weekly_report";
    private const string DateAttribute = "new_sunday_date";
    private const string PresentRecordWeeklyReportAttribute = "new_group_present_weekly_report_prese";
    private const string PresentRecordContactAttribute = "new_contact_new_present_record";
    private const string PresentRecordListAttribute = "new_list_new_present_record";
    private const string ContactPrimaryListAttribute = "new_cell_list_contact";
    private const string AreaLeaderAttribute = "new_contact_list_arealeader";
    private const string AreaNameAttribute = "new_area_name";
    private const string RaceLeaderAttribute = "new_contact_race_leager_list";
    private const string CoAreaLeaderAttribute = "new_contact_list_co_arealeader";
    private const string CoRaceLeaderAttribute = "new_contact_co_race_leager_list";
    private const string ViceFamilyLeaderAttribute = "new_contact_list_vice_family_leader";
    private static readonly string[] SmallGroupFieldNames =
    [
        AreaLeaderAttribute,
        AreaNameAttribute,
        RaceLeaderAttribute,
        CoAreaLeaderAttribute,
        CoRaceLeaderAttribute,
        ViceFamilyLeaderAttribute
    ];

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
        var missingMemberIds = memberIds
            .Where(memberId => !baseline.Contains(memberId))
            .ToArray();
        if (missingMemberIds.Length == 0)
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
            MemberIds = missingMemberIds
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
    /// 執行 small-group 的兩個固定模式。先讀完整六欄 baseline，再由 server-owned relationship 解析 area
    /// leader/name（僅 area mode），並以固定欄位建立一次 update；write 後再次讀完整六欄確認。race mode
    /// 只改 race-leader 欄位，area mode 才設定三欄並清除三個 deputy lookup，caller 無法提供任意欄位 map。
    /// </summary>
    internal static OperationResponseData ExecuteUpdateSmallGroupFields(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ValidateSmallGroupOperation(service, operation, ceVersion);
        var listId = ReadGuid(operation.Parameters, "listId");
        var mode = ReadMode(operation.Parameters);
        var targetLeaderId = ReadGuid(operation.Parameters, "targetLeaderContactId");
        var baseline = ReadSmallGroupFields(service, listId);

        SmallGroupFields expected;
        if (string.Equals(mode, "change-area-leader", StringComparison.Ordinal))
        {
            var area = ResolveAreaLeader(service, targetLeaderId);
            expected = new SmallGroupFields(
                area.AreaLeader,
                area.AreaName,
                new EntityReference(ContactEntityName, targetLeaderId),
                null,
                null,
                null);
        }
        else
        {
            expected = baseline with
            {
                RaceLeader = new EntityReference(ContactEntityName, targetLeaderId)
            };
        }

        if (IsMatchingSmallGroupFields(baseline, expected))
        {
            return OperationResponseData.ForSmallGroupFixedFieldsMutation(
                operation.OperationId,
                ceVersion,
                P72ControlledMutationDisposition.NoChange,
                P72ControlledMutationCorrelationCategory.NoDispatch);
        }

        var update = new Entity(ListEntityName, listId)
        {
            [RaceLeaderAttribute] = new EntityReference(ContactEntityName, targetLeaderId)
        };
        if (string.Equals(mode, "change-area-leader", StringComparison.Ordinal))
        {
            update[AreaLeaderAttribute] = expected.AreaLeader;
            update[AreaNameAttribute] = expected.AreaName;
            update[CoAreaLeaderAttribute] = null;
            update[CoRaceLeaderAttribute] = null;
            update[ViceFamilyLeaderAttribute] = null;
        }

        service.Update(update);
        var readBack = ReadSmallGroupFields(service, listId);
        if (!IsMatchingSmallGroupFields(readBack, expected))
        {
            throw new InvalidOperationException("The Data8 small-group read-back is invalid.");
        }

        return OperationResponseData.ForSmallGroupFixedFieldsMutation(
            operation.OperationId,
            ceVersion,
            P72ControlledMutationDisposition.Changed,
            P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
    }

    /// <summary>
    /// 執行固定 contact owner Assign。先確認目標 systemuser 為 active，再讀 contact owner baseline；已符合時
    /// 不取得 CRM action dispatch，否則只送一次 AssignRequest 並以 ownerid read-back 確認。timeout 或例外
    /// 不在此層重送，交由 fixture reconciliation 判斷目前是 baseline、target 或 ambiguous。
    /// </summary>
    internal static OperationResponseData ExecuteAssignContactOwner(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ValidateOwnerOperation(service, operation, ceVersion);
        var contactId = ReadGuid(operation.Parameters, "contactId");
        var ownerId = ReadGuid(operation.Parameters, "ownerSystemUserId");

        var targetUser = service.Retrieve(
            SystemUserEntityName,
            ownerId,
            new ColumnSet("isdisabled"));
        if (targetUser is null ||
            !string.Equals(targetUser.LogicalName, SystemUserEntityName, StringComparison.Ordinal) ||
            targetUser.Id != ownerId ||
            !targetUser.Attributes.TryGetValue("isdisabled", out var disabledValue) ||
            disabledValue is not bool isDisabled ||
            isDisabled)
        {
            throw new InvalidOperationException("The Data8 contact owner target is not active.");
        }

        var baselineOwnerId = ReadOwnerId(service.Retrieve(
            ContactEntityName,
            contactId,
            new ColumnSet("ownerid")), contactId);
        if (baselineOwnerId == ownerId)
        {
            return OperationResponseData.ForContactOwnerAssignment(
                operation.OperationId,
                ceVersion,
                P72ControlledMutationDisposition.NoChange,
                P72ControlledMutationCorrelationCategory.NoDispatch);
        }

        service.Execute(new AssignRequest
        {
            Target = new EntityReference(ContactEntityName, contactId),
            Assignee = new EntityReference(SystemUserEntityName, ownerId)
        });

        var readBackOwnerId = ReadOwnerId(service.Retrieve(
            ContactEntityName,
            contactId,
            new ColumnSet("ownerid")), contactId);
        if (readBackOwnerId != ownerId)
        {
            throw new InvalidOperationException("The Data8 contact owner read-back is invalid.");
        }

        return OperationResponseData.ForContactOwnerAssignment(
            operation.OperationId,
            ceVersion,
            P72ControlledMutationDisposition.Changed,
            P72ControlledMutationCorrelationCategory.ReadBackConfirmed);
    }

    /// <summary>
    /// 執行固定 contact list-transfer composite：target add、optional source remove、target list weekly report
    /// 對應的單一 present record、primary-list lookup 與 optional owner assignment。所有 baseline 與 target graph
    /// 都以 server-owned QueryExpression/欄位讀回；若觀察到既非完整 baseline 也非完整 target 的 partial state，
    /// 立即 fail closed，絕不重開 composite 或猜測補償。這裡只回傳封閉 response，不保存 fixture identity。
    /// </summary>
    internal static OperationResponseData ExecuteTransferContactBetweenLists(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ValidateTransferOperation(service, operation, ceVersion);
        var contactId = ReadGuid(operation.Parameters, "contactId");
        var sourceListId = ReadOptionalGuid(operation.Parameters, "sourceListId");
        var targetListId = ReadGuid(operation.Parameters, "targetListId");
        var weekStartDate = ReadUtcSunday(operation.Parameters);
        var ownerId = ReadOptionalGuid(operation.Parameters, "ownerSystemUserId");

        if (sourceListId == targetListId)
        {
            throw new InvalidOperationException("The Data8 transfer source and target lists must differ.");
        }

        if (ownerId is Guid targetOwnerId)
        {
            EnsureActiveSystemUser(service, targetOwnerId);
        }

        var sourceHasMember = sourceListId is Guid sourceId &&
                              ReadMemberships(service, sourceId, [contactId]).Contains(contactId);
        var targetHasMember = ReadMemberships(service, targetListId, [contactId]).Contains(contactId);
        var weeklyReportId = ResolveWeeklyReport(service, targetListId, weekStartDate);
        var presentRecords = ReadPresentRecords(service, weeklyReportId, contactId, weekStartDate);
        var primaryListId = ReadPrimaryListId(service, contactId);
        Guid? baselineOwnerId = ownerId is null ? null : ReadOwnerId(
            service.Retrieve(ContactEntityName, contactId, new ColumnSet("ownerid")),
            contactId);

        var targetStateIsComplete = targetHasMember &&
                                     !sourceHasMember &&
                                     presentRecords.Count == 1 &&
                                     IsMatchingPresentRecord(
                                         presentRecords[0],
                                         weeklyReportId,
                                         contactId,
                                         targetListId,
                                         weekStartDate) &&
                                     primaryListId == targetListId &&
                                     (ownerId is null || baselineOwnerId == ownerId);
        if (targetStateIsComplete)
        {
            return OperationResponseData.ForContactListTransfer(
                operation.OperationId,
                ceVersion,
                P72ControlledMutationDisposition.NoChange,
                P72ControlledMutationCorrelationCategory.NoDispatch);
        }

        var baselineStateIsComplete =
            (!sourceListId.HasValue || sourceHasMember) &&
            !targetHasMember &&
            presentRecords.Count == 0 &&
            primaryListId != targetListId &&
            (ownerId is null || baselineOwnerId != ownerId);
        if (!baselineStateIsComplete)
        {
            throw new InvalidOperationException("The Data8 transfer graph is partial or ambiguous.");
        }

        service.Execute(new AddListMembersListRequest
        {
            ListId = targetListId,
            MemberIds = [contactId]
        });
        EnsureMembershipState(service, targetListId, contactId, expectedPresent: true);

        if (sourceListId is Guid sourceList)
        {
            service.Execute(new RemoveMemberListRequest
            {
                ListId = sourceList,
                EntityId = contactId
            });
            EnsureMembershipState(service, sourceList, contactId, expectedPresent: false);
        }

        var presentRecordId = service.Create(new Entity(PresentRecordEntityName)
        {
            [PresentRecordWeeklyReportAttribute] = new EntityReference(WeeklyReportEntityName, weeklyReportId),
            [PresentRecordContactAttribute] = new EntityReference(ContactEntityName, contactId),
            [PresentRecordListAttribute] = new EntityReference(ListEntityName, targetListId),
            [DateAttribute] = weekStartDate.UtcDateTime
        });
        if (presentRecordId == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 transfer present record create result is invalid.");
        }

        service.Update(new Entity(ContactEntityName, contactId)
        {
            [ContactPrimaryListAttribute] = new EntityReference(ListEntityName, targetListId)
        });

        if (ownerId is Guid ownerSystemUserId && baselineOwnerId != ownerSystemUserId)
        {
            service.Execute(new AssignRequest
            {
                Target = new EntityReference(ContactEntityName, contactId),
                Assignee = new EntityReference(SystemUserEntityName, ownerSystemUserId)
            });
        }

        // 最後一次完整 graph reconciliation 是成功回應的唯一依據；任一 component 不符都交由上層
        // fixture cleanup owner 處理，connector 不會對未知狀態再送任何 action。
        EnsureMembershipState(service, targetListId, contactId, expectedPresent: true);
        if (sourceListId is Guid finalSourceList)
        {
            EnsureMembershipState(service, finalSourceList, contactId, expectedPresent: false);
        }

        var finalWeeklyReportId = ResolveWeeklyReport(service, targetListId, weekStartDate);
        var finalPresentRecords = ReadPresentRecords(service, finalWeeklyReportId, contactId, weekStartDate);
        if (finalPresentRecords.Count != 1 ||
            finalPresentRecords[0].Id != presentRecordId ||
            !IsMatchingPresentRecord(
                finalPresentRecords[0],
                finalWeeklyReportId,
                contactId,
                targetListId,
                weekStartDate) ||
            ReadPrimaryListId(service, contactId) != targetListId ||
            ownerId is Guid finalOwnerId &&
            ReadOwnerId(service.Retrieve(ContactEntityName, contactId, new ColumnSet("ownerid")), contactId) != finalOwnerId)
        {
            throw new InvalidOperationException("The Data8 transfer graph read-back is invalid.");
        }

        return OperationResponseData.ForContactListTransfer(
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

    /// <summary>重驗 small-group registry、CE 版本與三個固定參數；任何未知欄位在 CRM 呼叫前拒絕。</summary>
    private static void ValidateSmallGroupOperation(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(ceVersion, Ce91, StringComparison.Ordinal) ||
            !string.Equals(operation.OperationId, OperationIds.ListManagementSmallGroupUpdateFields, StringComparison.Ordinal) ||
            !Package01OperationRegistry.TryGet(operation.OperationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.OperationKind, "write", StringComparison.Ordinal) ||
            !string.Equals(definition.TemplateId, "listmanagement.smallgroup.fixed.fields.v1", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.SmallGroupFixedFieldsMutation ||
            !string.Equals(definition.AuditRequirement, "write-audit", StringComparison.Ordinal) ||
            !string.Equals(definition.IdempotencyClass, "caller-idempotency-key-required", StringComparison.Ordinal) ||
            operation.Parameters is null ||
            operation.Parameters.Count != 3 ||
            operation.Parameters.Keys.Any(static name => name is not "listId" and not "mode" and not "targetLeaderContactId"))
        {
            throw new InvalidOperationException("The Data8 small-group operation is not permitted.");
        }

        _ = ReadGuid(operation.Parameters, "listId");
        _ = ReadGuid(operation.Parameters, "targetLeaderContactId");
        _ = ReadMode(operation.Parameters);
    }

    /// <summary>重驗 contact owner action 的固定 registry/template/response 與兩個 GUID 參數。</summary>
    private static void ValidateOwnerOperation(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(ceVersion, Ce91, StringComparison.Ordinal) ||
            !string.Equals(operation.OperationId, OperationIds.ContactAssignOwner, StringComparison.Ordinal) ||
            !Package01OperationRegistry.TryGet(operation.OperationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.OperationKind, "action", StringComparison.Ordinal) ||
            !string.Equals(definition.TemplateId, "contact.assign.owner.v1", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.ContactOwnerAssignment ||
            !string.Equals(definition.AuditRequirement, "write-audit", StringComparison.Ordinal) ||
            !string.Equals(definition.IdempotencyClass, "caller-idempotency-key-required", StringComparison.Ordinal) ||
            operation.Parameters is null ||
            operation.Parameters.Count != 2 ||
            operation.Parameters.Keys.Any(static name => name is not "contactId" and not "ownerSystemUserId"))
        {
            throw new InvalidOperationException("The Data8 contact owner operation is not permitted.");
        }

        _ = ReadGuid(operation.Parameters, "contactId");
        _ = ReadGuid(operation.Parameters, "ownerSystemUserId");
    }

    /// <summary>讀取固定 small-group mode；不接受大小寫變形、任意 enum 或 caller 欄位 map。</summary>
    private static string ReadMode(IReadOnlyDictionary<string, object?> parameters)
        => parameters.TryGetValue("mode", out var value) &&
           value is string mode &&
           mode is "change-race-leader" or "change-area-leader"
            ? mode
            : throw new InvalidOperationException("The Data8 small-group mode is invalid.");

    /// <summary>讀取並驗證完整六欄 list baseline；缺欄可代表 CRM 的 null，但錯型別或錯 identity 必須拒絕。</summary>
    private static SmallGroupFields ReadSmallGroupFields(IOrganizationService service, Guid listId)
    {
        var entity = service.Retrieve(ListEntityName, listId, new ColumnSet(SmallGroupFieldNames));
        if (entity is null ||
            !string.Equals(entity.LogicalName, ListEntityName, StringComparison.Ordinal) ||
            entity.Id != listId)
        {
            throw new InvalidOperationException("The Data8 small-group baseline is invalid.");
        }

        return new SmallGroupFields(
            ReadOptionalContactReference(entity, AreaLeaderAttribute),
            ReadOptionalText(entity, AreaNameAttribute),
            ReadOptionalContactReference(entity, RaceLeaderAttribute),
            ReadOptionalContactReference(entity, CoAreaLeaderAttribute),
            ReadOptionalContactReference(entity, CoRaceLeaderAttribute),
            ReadOptionalContactReference(entity, ViceFamilyLeaderAttribute));
    }

    /// <summary>依 target race-leader relationship 解析唯一 area leader/name；多筆、缺欄或錯型別均 fail closed。</summary>
    private static (EntityReference AreaLeader, string AreaName) ResolveAreaLeader(
        IOrganizationService service,
        Guid targetLeaderId)
    {
        var query = new QueryExpression(ListEntityName)
        {
            ColumnSet = new ColumnSet(AreaLeaderAttribute, AreaNameAttribute),
            TopCount = 2,
            NoLock = true
        };
        query.Criteria.AddCondition(RaceLeaderAttribute, ConditionOperator.Equal, targetLeaderId);
        var rows = service.RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The Data8 area-leader relationship response is missing.");
        if (rows.MoreRecords || rows.Entities.Count != 1)
        {
            throw new InvalidOperationException("The Data8 area-leader relationship is ambiguous.");
        }

        var row = rows.Entities[0];
        if (!string.Equals(row.LogicalName, ListEntityName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 area-leader relationship row is invalid.");
        }

        var areaLeader = ReadRequiredContactReference(row, AreaLeaderAttribute);
        var areaName = ReadRequiredText(row, AreaNameAttribute);
        return (areaLeader, areaName);
    }

    /// <summary>比較六欄 projection；只有完全相同才可回傳 no-change 或 read-back confirmed。</summary>
    private static bool IsMatchingSmallGroupFields(SmallGroupFields actual, SmallGroupFields expected)
        => SameReference(actual.AreaLeader, expected.AreaLeader) &&
           string.Equals(actual.AreaName, expected.AreaName, StringComparison.Ordinal) &&
           SameReference(actual.RaceLeader, expected.RaceLeader) &&
           SameReference(actual.CoAreaLeader, expected.CoAreaLeader) &&
           SameReference(actual.CoRaceLeader, expected.CoRaceLeader) &&
           SameReference(actual.ViceFamilyLeader, expected.ViceFamilyLeader);

    /// <summary>只比較 EntityReference logical name 與 GUID，不保留 SDK reference 物件。</summary>
    private static bool SameReference(EntityReference? left, EntityReference? right)
        => left?.Id == right?.Id &&
           (left is null || right is null || string.Equals(left.LogicalName, right.LogicalName, StringComparison.Ordinal));

    /// <summary>讀取可為 null 的 contact lookup；不接受任意 entity logical name 或文字轉型。</summary>
    private static EntityReference? ReadOptionalContactReference(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value is EntityReference reference &&
               reference.Id != Guid.Empty &&
               string.Equals(reference.LogicalName, ContactEntityName, StringComparison.Ordinal)
            ? new EntityReference(ContactEntityName, reference.Id)
            : throw new InvalidOperationException("The Data8 small-group lookup is invalid.");
    }

    /// <summary>讀取可為 null 的 bounded string；CRM 回傳其他 SDK 型別時拒絕。</summary>
    private static string? ReadOptionalText(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value is string text ? text : throw new InvalidOperationException("The Data8 small-group text is invalid.");
    }

    /// <summary>讀取 area relationship 必須存在的 contact lookup。</summary>
    private static EntityReference ReadRequiredContactReference(Entity entity, string attributeName)
        => ReadOptionalContactReference(entity, attributeName)
            ?? throw new InvalidOperationException("The Data8 area-leader lookup is missing.");

    /// <summary>讀取 area name 必須存在且非空的 server-owned string。</summary>
    private static string ReadRequiredText(Entity entity, string attributeName)
        => ReadOptionalText(entity, attributeName) is { Length: > 0 } text
            ? text
            : throw new InvalidOperationException("The Data8 area name is missing.");

    /// <summary>讀取 contact.ownerid，僅允許 active systemuser reference 且 identity 必須與 contact entity 相符。</summary>
    private static Guid ReadOwnerId(Entity? entity, Guid expectedContactId)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            entity.Id != expectedContactId ||
            !entity.Attributes.TryGetValue("ownerid", out var value) ||
            value is not EntityReference reference ||
            reference.Id == Guid.Empty ||
            !string.Equals(reference.LogicalName, SystemUserEntityName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 contact owner read-back is invalid.");
        }

        return reference.Id;
    }

    /// <summary>重驗 transfer composite 的固定 registry、CE 版本與有限參數名稱。</summary>
    private static void ValidateTransferOperation(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(ceVersion, Ce91, StringComparison.Ordinal) ||
            !string.Equals(operation.OperationId, OperationIds.NewPersonContactTransferBetweenLists, StringComparison.Ordinal) ||
            !Package01OperationRegistry.TryGet(operation.OperationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.OperationKind, "write", StringComparison.Ordinal) ||
            !string.Equals(definition.TemplateId, "newperson.contact.transfer.between.lists.v1", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.ContactListTransfer ||
            !string.Equals(definition.AuditRequirement, "write-audit", StringComparison.Ordinal) ||
            !string.Equals(definition.IdempotencyClass, "caller-idempotency-key-required", StringComparison.Ordinal) ||
            operation.Parameters is null ||
            operation.Parameters.Count is < 3 or > 5 ||
            operation.Parameters.Keys.Any(static name => name is not "contactId" and
                not "sourceListId" and not "targetListId" and not "weekStartDate" and not "ownerSystemUserId"))
        {
            throw new InvalidOperationException("The Data8 transfer operation is not permitted.");
        }

        _ = ReadGuid(operation.Parameters, "contactId");
        _ = ReadGuid(operation.Parameters, "targetListId");
        _ = ReadUtcSunday(operation.Parameters);
        _ = ReadOptionalGuid(operation.Parameters, "sourceListId");
        _ = ReadOptionalGuid(operation.Parameters, "ownerSystemUserId");
    }

    /// <summary>讀取 optional non-empty GUID；欄位缺少代表未啟用該 composite branch，null/錯型別則拒絕。</summary>
    private static Guid? ReadOptionalGuid(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.ContainsKey(name))
        {
            return null;
        }

        return ReadGuid(parameters, name);
    }

    /// <summary>讀取 executor 已正規化的 UTC Sunday；connector 不使用主機 local timezone。</summary>
    private static DateTimeOffset ReadUtcSunday(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("weekStartDate", out var value) ||
            value is not DateTimeOffset date ||
            date.Offset != TimeSpan.Zero ||
            date.DayOfWeek != DayOfWeek.Sunday)
        {
            throw new InvalidOperationException("The Data8 transfer week start date is invalid.");
        }

        return date;
    }

    /// <summary>驗證 owner 是 active systemuser；此讀取只在目前 transfer scope 使用，不保存 user/session state。</summary>
    private static void EnsureActiveSystemUser(IOrganizationService service, Guid ownerId)
    {
        var entity = service.Retrieve(SystemUserEntityName, ownerId, new ColumnSet("isdisabled"));
        if (entity is null ||
            !string.Equals(entity.LogicalName, SystemUserEntityName, StringComparison.Ordinal) ||
            entity.Id != ownerId ||
            !entity.Attributes.TryGetValue("isdisabled", out var value) ||
            value is not bool isDisabled ||
            isDisabled)
        {
            throw new InvalidOperationException("The Data8 transfer owner target is not active.");
        }
    }

    /// <summary>
    /// 以 target list relationship 與 UTC Sunday 唯一解析 weekly report。query 只使用 connector-owned
    /// entity/attribute/filter；零筆、多筆、paging continuation 或空 identity 都是不可執行 fixture 狀態。
    /// </summary>
    private static Guid ResolveWeeklyReport(
        IOrganizationService service,
        Guid targetListId,
        DateTimeOffset weekStartDate)
    {
        var query = new QueryExpression(WeeklyReportEntityName)
        {
            ColumnSet = new ColumnSet("new_group_present_weekly_reportid"),
            TopCount = 2,
            NoLock = true
        };
        query.Criteria.AddCondition(WeeklyReportListAttribute, ConditionOperator.Equal, targetListId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(DateAttribute, ConditionOperator.Equal, weekStartDate.UtcDateTime);
        var rows = service.RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The Data8 transfer weekly-report response is missing.");
        if (rows.MoreRecords || rows.Entities.Count != 1)
        {
            throw new InvalidOperationException("The Data8 transfer weekly-report relationship is ambiguous.");
        }

        var row = rows.Entities[0];
        if (!string.Equals(row.LogicalName, WeeklyReportEntityName, StringComparison.Ordinal) || row.Id == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 transfer weekly-report row is invalid.");
        }

        return row.Id;
    }

    /// <summary>
    /// 查詢指定週報/contact/date 的 present record。最多允許一列；任何多列或 malformed SDK graph 都視為
    /// partial/ambiguous state，不讓 composite 以猜測方式覆寫既有出席資料。
    /// </summary>
    private static IReadOnlyList<TransferPresentRecord> ReadPresentRecords(
        IOrganizationService service,
        Guid weeklyReportId,
        Guid contactId,
        DateTimeOffset weekStartDate)
    {
        var query = new QueryExpression(PresentRecordEntityName)
        {
            ColumnSet = new ColumnSet(
                PresentRecordWeeklyReportAttribute,
                PresentRecordContactAttribute,
                PresentRecordListAttribute,
                DateAttribute),
            TopCount = 2,
            NoLock = true
        };
        query.Criteria.AddCondition(PresentRecordWeeklyReportAttribute, ConditionOperator.Equal, weeklyReportId);
        query.Criteria.AddCondition(PresentRecordContactAttribute, ConditionOperator.Equal, contactId);
        query.Criteria.AddCondition(DateAttribute, ConditionOperator.Equal, weekStartDate.UtcDateTime);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var rows = service.RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The Data8 transfer present-record response is missing.");
        if (rows.MoreRecords || rows.Entities.Count > 1)
        {
            throw new InvalidOperationException("The Data8 transfer present-record state is ambiguous.");
        }

        return rows.Entities.Select(row => new TransferPresentRecord(
                row.Id,
                ReadReferenceId(row, PresentRecordWeeklyReportAttribute, WeeklyReportEntityName),
                ReadReferenceId(row, PresentRecordContactAttribute, ContactEntityName),
                ReadReferenceId(row, PresentRecordListAttribute, ListEntityName),
                ReadUtcDate(row, DateAttribute)))
            .ToArray();
    }

    /// <summary>讀取 contact 的 primary-list lookup；缺欄/null 代表 baseline 沒有 list，錯 logical name 一律拒絕。</summary>
    private static Guid? ReadPrimaryListId(IOrganizationService service, Guid contactId)
    {
        var entity = service.Retrieve(
            ContactEntityName,
            contactId,
            new ColumnSet(ContactPrimaryListAttribute));
        if (entity is null ||
            !string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            entity.Id != contactId)
        {
            throw new InvalidOperationException("The Data8 transfer contact lookup is invalid.");
        }

        if (!entity.Attributes.TryGetValue(ContactPrimaryListAttribute, out var value) || value is null)
        {
            return null;
        }

        return value is EntityReference reference &&
               reference.Id != Guid.Empty &&
               string.Equals(reference.LogicalName, ListEntityName, StringComparison.Ordinal)
            ? reference.Id
            : throw new InvalidOperationException("The Data8 transfer contact lookup is invalid.");
    }

    /// <summary>確認單一 membership target state；不回傳 row，避免 SDK identity 穿越 connector boundary。</summary>
    private static void EnsureMembershipState(
        IOrganizationService service,
        Guid listId,
        Guid contactId,
        bool expectedPresent)
    {
        var actual = ReadMemberships(service, listId, [contactId]).Contains(contactId);
        if (actual != expectedPresent)
        {
            throw new InvalidOperationException("The Data8 transfer membership read-back is invalid.");
        }
    }

    /// <summary>比對 transfer present record 的完整固定 graph projection。</summary>
    private static bool IsMatchingPresentRecord(
        TransferPresentRecord record,
        Guid weeklyReportId,
        Guid contactId,
        Guid targetListId,
        DateTimeOffset weekStartDate)
        => record.Id != Guid.Empty &&
           record.WeeklyReportId == weeklyReportId &&
           record.ContactId == contactId &&
           record.ListId == targetListId &&
           record.WeekStartUtc == weekStartDate.UtcDateTime;

    /// <summary>讀取固定 lookup reference 並驗證 logical name；不以 ToString 寬鬆轉型。</summary>
    private static Guid ReadReferenceId(Entity entity, string attributeName, string expectedLogicalName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) ||
            value is not EntityReference reference ||
            reference.Id == Guid.Empty ||
            !string.Equals(reference.LogicalName, expectedLogicalName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 transfer lookup is invalid.");
        }

        return reference.Id;
    }

    /// <summary>將 CE DateTime 轉為 UTC；未指定 Kind 的測試/SDK 值按 UTC 解讀，避免 host timezone 漂移。</summary>
    private static DateTime ReadUtcDate(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is not DateTime date)
        {
            throw new InvalidOperationException("The Data8 transfer date is invalid.");
        }

        return date.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : date.ToUniversalTime();
    }

    /// <summary>transfer present-record 的 connector-internal pure snapshot，不跨 request 或保存 SDK object。</summary>
    private sealed record TransferPresentRecord(
        Guid Id,
        Guid WeeklyReportId,
        Guid ContactId,
        Guid ListId,
        DateTime WeekStartUtc);

    /// <summary>small-group 六欄純值 snapshot；只在目前 connector stack 存活，不跨 request 或保存 service。</summary>
    private sealed record SmallGroupFields(
        EntityReference? AreaLeader,
        string? AreaName,
        EntityReference? RaceLeader,
        EntityReference? CoAreaLeader,
        EntityReference? CoRaceLeader,
        EntityReference? ViceFamilyLeader);

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
        var result = new HashSet<Guid>();
        // CRM 的 IN 條件固定以 500 筆分塊；即使 caller 只提供 1,000 筆上限，也不能把 1,000
        // 個 GUID 放進同一個 QueryExpression。每個 chunk 都在目前 method scope materialize，
        // 不保存 query、EntityCollection 或跨 request 的 membership state。
        foreach (var chunk in memberIds.Chunk(MembershipQueryChunkSize))
        {
            var query = new QueryExpression("listmember")
            {
                ColumnSet = new ColumnSet("listid", "entityid"),
                NoLock = true,
                TopCount = MembershipQueryChunkSize
            };
            query.Criteria.AddCondition("listid", ConditionOperator.Equal, listId);
            query.Criteria.AddCondition("entityid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            var rows = service.RetrieveMultiple(query)
                ?? throw new InvalidOperationException("The Data8 static-list read-back is invalid.");
            if (rows.MoreRecords || rows.Entities.Count > chunk.Length)
            {
                throw new InvalidOperationException("The Data8 static-list read-back is invalid.");
            }

            var allowedMembers = chunk.ToHashSet();
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
        }

        return result;
    }
}
