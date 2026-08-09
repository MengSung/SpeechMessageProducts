// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStore.cs
// 用途：實作 P7.2 Slice C fixture bridge 所需的固定 Data8／IOrganizationService store。
//       這是 operator-only live evidence 元件，不是 ChurchReport production repository。
//
// 信任與生命週期：
// 1. 本類別只接受由 test composition root 建立、已解析到 sunnyvalechback CE 9.1 的單一
//    OnPremiseClient；建構後接手該 service 的唯一 Dispose ownership，禁止跨 test/request 重用。
// 2. 每個 query 的 entity、欄位、filter、page/chunk 上限都寫死在本檔；不接受 FetchXML、任意
//    QueryBase、欄位 map、endpoint、OrganizationId、credential 或 caller-selected entity name。
// 3. cleanup 只對先前 read-back 已證明屬於本 fixture 的 graph 做固定增減／update／delete；任何
//    partial 或 malformed graph 都拋出 sanitized 上層可分類的例外，不猜測、不自動重試。
// 4. SDK Entity、EntityCollection、query 與 response 只存在目前 synchronous method scope；回傳
//    前全部投影成 P7.2 純值 snapshot，避免 CRM graph、session、token、buffer 或 exception detail 留存。
// ============================================================================

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.ProductClient.ListManagement;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// Slice C live fixture 的固定 Data8 store。它刻意不是 generic repository：每個 public method 只擁有
/// 一個 allowlisted business graph；service 由建構子接手，Dispose 先 exchange 清空引用，再釋放 WCF channel。
/// </summary>
internal sealed class P72Data8ListManagementFixtureStore : IP72ListManagementFixtureStore
{
    private const string ListEntityName = "list";
    private const string ListMemberEntityName = "listmember";
    private const string ContactEntityName = "contact";
    private const string SystemUserEntityName = "systemuser";
    private const string WeeklyReportEntityName = "new_group_present_weekly_report";
    private const string PresentRecordEntityName = "new_present_record";
    private const string WeeklyReportIdAttribute = "new_group_present_weekly_reportid";
    private const string WeeklyReportListAttribute = "new_list_group_present_weekly_report";
    private const string PresentRecordWeeklyReportAttribute = "new_group_present_weekly_report_prese";
    private const string PresentRecordContactAttribute = "new_contact_new_present_record";
    private const string PresentRecordListAttribute = "new_list_new_present_record";
    private const string DateAttribute = "new_sunday_date";
    private const string ContactPrimaryListAttribute = "new_cell_list_contact";
    private const string OwnerAttribute = "ownerid";
    private const string ListNameAttribute = "listname";
    private const string ListTypeAttribute = "type";
    private const string ListCreatedFromCodeAttribute = "createdfromcode";
    private const string ContactDescriptionAttribute = "description";
    private const string ContactFullNameAttribute = "fullname";
    private const string AreaLeaderAttribute = "new_contact_list_arealeader";
    private const string AreaNameAttribute = "new_area_name";
    private const string RaceLeaderAttribute = "new_contact_race_leager_list";
    private const string CoAreaLeaderAttribute = "new_contact_list_co_arealeader";
    private const string CoRaceLeaderAttribute = "new_contact_co_race_leager_list";
    private const string ViceFamilyLeaderAttribute = "new_contact_list_vice_family_leader";
    private const int MembershipChunkSize = 500;
    private const int MaximumMemberIds = 1000;
    private const int ContactListCreatedFromCodeValue = 2;
    private const string SliceCListMarkerPrefix = "P7.2-SC-";
    private const string SliceASourceContactMarker = "p7.2-contact-basic-info";

    private static readonly string[] SmallGroupFields =
    [
        AreaLeaderAttribute,
        AreaNameAttribute,
        RaceLeaderAttribute,
        CoAreaLeaderAttribute,
        CoRaceLeaderAttribute,
        ViceFamilyLeaderAttribute
    ];

    private IOrganizationService? _service;
    private IDisposable? _disposableService;

    /// <summary>
    /// 接手單一 Data8 Organization service 的完整 ownership。不可 Dispose 的 service 直接拒絕，避免
    /// live evidence 結束後遺留 WCF channel、native handle、credential graph 或 pooled session。
    /// </summary>
    /// <param name="service">只連到 task-owned sunnyvalechback CE 9.1 Data8 profile 的 OnPremiseClient。</param>
    internal P72Data8ListManagementFixtureStore(IOrganizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service is not IDisposable disposable)
        {
            throw new ArgumentException("The fixture service must support deterministic disposal.", nameof(service));
        }

        _service = service;
        _disposableService = disposable;
    }

    /// <summary>
    /// 以固定 listmember projection 讀取 bounded contact subset 的目前 membership。每個 500-ID chunk
    /// 都同時固定 listid 與 entityid IN filter，並拒絕 paging、額外 row、錯 logical name 或錯 lookup
    /// identity；回傳前只保留純 GUID snapshot，不讓 SDK row 或 CRM session 跨出目前 fixture scope。
    /// </summary>
    public P72MembershipSnapshot ReadMembership(Guid listId, IReadOnlyList<Guid> contactIds)
    {
        RequireGuid(listId, nameof(listId));
        var members = CopyDistinctMembers(contactIds);
        var service = GetService();
        var present = new HashSet<Guid>();
        foreach (var chunk in members.Chunk(MembershipChunkSize))
        {
            var query = new QueryExpression(ListMemberEntityName)
            {
                ColumnSet = new ColumnSet("listid", "entityid"),
                NoLock = true,
                TopCount = MembershipChunkSize
            };
            query.Criteria.AddCondition("listid", ConditionOperator.Equal, listId);
            query.Criteria.AddCondition("entityid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            var rows = service.RetrieveMultiple(query)
                ?? throw new InvalidOperationException("The fixture membership response is missing.");
            if (rows.MoreRecords || rows.Entities.Count > chunk.Length)
            {
                throw new InvalidOperationException("The fixture membership response exceeded its bound.");
            }

            var allowed = chunk.ToHashSet();
            foreach (var row in rows.Entities)
            {
                if (!string.Equals(row.LogicalName, ListMemberEntityName, StringComparison.Ordinal) ||
                    ReadListMemberLookupId(row, "listid", ListEntityName) != listId)
                {
                    throw new InvalidOperationException("The fixture membership projection is invalid.");
                }

                var entityId = ReadListMemberLookupId(row, "entityid", ContactEntityName);
                if (entityId == Guid.Empty || !allowed.Contains(entityId) || !present.Add(entityId))
                {
                    throw new InvalidOperationException("The fixture membership projection is invalid.");
                }
            }
        }

        return new P72MembershipSnapshot(present);
    }

    /// <summary>
    /// 將指定 contact subset 還原到 captured membership baseline。先讀取同一 bounded subset，再只移除
    /// evidence 新增的 member 或補回 baseline 原有的 member；不掃描或改寫其他 listmember，read-back
    /// 由 bridge 的唯一 cleanup state machine 完成，任何 CRM fault 都保留給它 fail closed。
    /// </summary>
    public void RestoreMembership(Guid listId, IReadOnlyList<Guid> contactIds, P72MembershipSnapshot baseline)
    {
        RequireGuid(listId, nameof(listId));
        ArgumentNullException.ThrowIfNull(baseline);
        var members = CopyDistinctMembers(contactIds);
        var current = ReadMembership(listId, members);
        var service = GetService();

        var toRemove = current.PresentMemberIds
            .Where(memberId => !baseline.PresentMemberIds.Contains(memberId))
            .ToArray();
        foreach (var memberId in toRemove)
        {
            service.Execute(new RemoveMemberListRequest
            {
                ListId = listId,
                EntityId = memberId
            });
        }

        var toAdd = baseline.PresentMemberIds
            .Where(memberId => !current.PresentMemberIds.Contains(memberId))
            .ToArray();
        if (toAdd.Length > 0)
        {
            service.Execute(new AddListMembersListRequest
            {
                ListId = listId,
                MemberIds = toAdd
            });
        }
    }

    /// <summary>
    /// 讀取 task-owned list 的完整六欄 small-group projection。list logical name 與 ID 必須精確相符，
    /// lookup 只接受 contact reference、文字欄位只接受 string；null 是可還原 baseline，型別漂移則在
    /// dispatch 前停止，避免不完整 data 成為 cleanup 的基礎。
    /// </summary>
    public P72SmallGroupFixedFieldsSnapshot ReadSmallGroupFields(Guid listId)
    {
        RequireGuid(listId, nameof(listId));
        var entity = GetService().Retrieve(ListEntityName, listId, new ColumnSet(SmallGroupFields));
        if (entity is null ||
            !string.Equals(entity.LogicalName, ListEntityName, StringComparison.Ordinal) ||
            entity.Id != listId)
        {
            throw new InvalidOperationException("The fixture small-group projection is invalid.");
        }

        return new P72SmallGroupFixedFieldsSnapshot(
            ReadOptionalReference(entity, AreaLeaderAttribute, ContactEntityName),
            ReadOptionalText(entity, AreaNameAttribute),
            ReadOptionalReference(entity, RaceLeaderAttribute, ContactEntityName),
            ReadOptionalReference(entity, CoAreaLeaderAttribute, ContactEntityName),
            ReadOptionalReference(entity, CoRaceLeaderAttribute, ContactEntityName),
            ReadOptionalReference(entity, ViceFamilyLeaderAttribute, ContactEntityName));
    }

    /// <summary>
    /// 以固定的 CE projection 證明 Slice C descriptor 只指向 task-owned static list 與已標記的 source
    /// contact。這個 validation 是第一次 bridge dispatch 前的最後一層遠端 provenance 邊界：本機 JSON、
    /// Windows identity 或目前 graph shape 都不能單獨授權可存取的任意 CRM record。方法只接受 Slice C
    /// descriptor 的八個固定 identity，對六個 list 讀取 <c>listname/type</c>、對 source contact 讀取
    /// <c>description</c>、對 target leader 讀取 <c>fullname</c>；不接受 generic discovery、欄位 map、
    /// endpoint、credential 或快取，且不會建立、更新、刪除或關聯任何 CRM data。
    /// </summary>
    /// <remarks>
    /// 每個 SDK Entity 僅存在目前同步方法 scope，回傳值只有布林分類；任何 query、型別、logical name、
    /// static type 或 marker 不可信都固定回傳 <see langword="false"/>。Store 仍由 evidence runner 唯一
    /// Dispose，故此唯讀 proof 不延長 Data8 channel、session、credential 或 fixture identity 的生命週期。
    /// </remarks>
    /// <param name="fixtureContactId">由已驗證 Slice A descriptor 帶入的 source contact。</param>
    /// <param name="addListId">add-members capability 唯一允許的 static list。</param>
    /// <param name="removeListId">remove-member capability 唯一允許的 static list。</param>
    /// <param name="smallGroupListId">small-group fixed-field capability 的 target list。</param>
    /// <param name="targetLeaderContactId">server-owned relationship 必須使用的 task-owned race leader。</param>
    /// <param name="expectedRelationshipListId">提供 area leader/name projection 的 task-owned relationship list。</param>
    /// <param name="transferSourceListId">transfer baseline 的 task-owned source static list。</param>
    /// <param name="transferTargetListId">transfer target 與 weekly-report 關係的 task-owned static list。</param>
    /// <returns>所有固定 remote provenance 都可證明時為 <see langword="true"/>；其餘情況一律為 <see langword="false"/>。</returns>
    internal bool TryValidateTaskOwnedSliceCFixtureGraph(
        Guid fixtureContactId,
        Guid addListId,
        Guid removeListId,
        Guid smallGroupListId,
        Guid targetLeaderContactId,
        Guid expectedRelationshipListId,
        Guid transferSourceListId,
        Guid transferTargetListId)
    {
        try
        {
            RequireGuid(fixtureContactId, nameof(fixtureContactId));
            RequireGuid(targetLeaderContactId, nameof(targetLeaderContactId));
            var listIds = new[]
            {
                addListId,
                removeListId,
                smallGroupListId,
                expectedRelationshipListId,
                transferSourceListId,
                transferTargetListId
            };
            if (listIds.Any(static id => id == Guid.Empty) ||
                listIds.Distinct().Count() != listIds.Length ||
                !HasSourceFixtureContactMarker(fixtureContactId) ||
                !HasTaskOwnedContactName(targetLeaderContactId))
            {
                return false;
            }

            return listIds.All(IsTaskOwnedStaticList);
        }
        catch (Exception)
        {
            // Data8、CRM metadata 或 projection failure 可能攜帶 endpoint、credential 或 fixture identity；
            // provenance boundary 只需要 fail-closed bool，故不保存或轉送原始例外。
            return false;
        }
    }

    /// <summary>
    /// 舊三參數入口無法證明 expected area leader/name 來自 task-owned relationship list，故刻意
    /// fail closed。live runner 必須將 descriptor 的專用 relationship list ID 傳入四參數 overload；
    /// 不得回退為只用 target leader 搜尋全組織的 list，否則其他 fixture 或使用者的資料可能成為
    /// target small-group mutation 的 expected state。
    /// </summary>
    /// <param name="listId">欲變更的 task-owned small-group list ID。</param>
    /// <param name="mode">固定 small-group mode。</param>
    /// <param name="targetLeaderContactId">固定 target race-leader contact ID。</param>
    /// <returns>此入口永不產生 expected projection。</returns>
    /// <exception cref="InvalidOperationException">缺少 descriptor-bound relationship identity 時擲回。</exception>
    public P72SmallGroupFixedFieldsSnapshot ResolveSmallGroupExpected(
        Guid listId,
        SmallGroupFixedFieldsUpdateMode mode,
        Guid targetLeaderContactId)
        => throw new InvalidOperationException("The fixture expected relationship list identity is required.");

    /// <summary>
    /// 以 descriptor 指定的 task-owned relationship list 建立 small-group expected projection。area mode
    /// 的 area leader/name 只可由該 list 與 target race leader 的交集讀取；target list 與 relationship
    /// list 必須不同，避免把欲寫入的 baseline 自行當成外部 relationship 證據。query、SDK row 與結果
    /// 都只停留在目前同步 method scope，不保存跨 test/request 的 CRM session 或 identity。
    /// </summary>
    /// <param name="listId">欲變更且已由 fixture descriptor 證明 task-owned 的 small-group list。</param>
    /// <param name="mode">只允許 change-race-leader 或 change-area-leader。</param>
    /// <param name="targetLeaderContactId">已由 descriptor 綁定的 target race-leader contact。</param>
    /// <param name="expectedRelationshipListId">提供 expected area leader/name 的專用 task-owned list。</param>
    /// <returns>只含六個固定欄位純值的 expected projection。</returns>
    /// <exception cref="ArgumentException">identity 缺失、重用 target list 或 mode 未被允許時擲回。</exception>
    /// <exception cref="InvalidOperationException">relationship query 缺列、多列、錯列或欄位型別不可信時擲回。</exception>
    public P72SmallGroupFixedFieldsSnapshot ResolveSmallGroupExpected(
        Guid listId,
        SmallGroupFixedFieldsUpdateMode mode,
        Guid targetLeaderContactId,
        Guid expectedRelationshipListId)
    {
        RequireGuid(listId, nameof(listId));
        RequireGuid(targetLeaderContactId, nameof(targetLeaderContactId));
        RequireGuid(expectedRelationshipListId, nameof(expectedRelationshipListId));
        if (listId == expectedRelationshipListId)
        {
            throw new ArgumentException("The expected relationship list must be distinct from the target list.", nameof(expectedRelationshipListId));
        }

        if (mode is not SmallGroupFixedFieldsUpdateMode.ChangeRaceLeader and not SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The fixture small-group mode is unsupported.");
        }

        var query = new QueryExpression(ListEntityName)
        {
            ColumnSet = new ColumnSet(AreaLeaderAttribute, AreaNameAttribute),
            NoLock = true,
            TopCount = 2
        };
        query.Criteria.AddCondition("listid", ConditionOperator.Equal, expectedRelationshipListId);
        query.Criteria.AddCondition(RaceLeaderAttribute, ConditionOperator.Equal, targetLeaderContactId);
        var rows = GetService().RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The fixture area-leader relationship response is missing.");
        if (rows.MoreRecords || rows.Entities.Count != 1)
        {
            throw new InvalidOperationException("The fixture area-leader relationship is ambiguous.");
        }

        var row = rows.Entities[0];
        if (!string.Equals(row.LogicalName, ListEntityName, StringComparison.Ordinal) ||
            row.Id != expectedRelationshipListId)
        {
            throw new InvalidOperationException("The fixture area-leader relationship row is invalid.");
        }

        var areaLeader = ReadRequiredReference(row, AreaLeaderAttribute, ContactEntityName);
        var areaName = ReadRequiredText(row, AreaNameAttribute);
        if (!HasTaskOwnedContactName(areaLeader))
        {
            throw new InvalidOperationException("The fixture area-leader contact provenance is invalid.");
        }

        if (mode == SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader)
        {
            return new P72SmallGroupFixedFieldsSnapshot(areaLeader, areaName, targetLeaderContactId, null, null, null);
        }

        // race mode 只變更 race-leader；其餘四欄必須保留目前 baseline，不能由 store
        // 擅自清除，否則 live evidence 的 rollback 會掩蓋 connector 契約漂移。
        var baseline = ReadSmallGroupFields(listId);
        return baseline with { RaceLeaderId = targetLeaderContactId };
    }

    /// <summary>
    /// 以固定六欄 Entity update 還原 small-group baseline。所有欄位名稱與 logical name 都由 store
    /// 擁有，null 明確表示清除對應 lookup；呼叫端不得提供 field map，bridge 會在 update 後重新讀取
    /// 全投影，確保寫入 fault 不會被誤認為 cleanup 成功。
    /// </summary>
    public void RestoreSmallGroupFields(Guid listId, P72SmallGroupFixedFieldsSnapshot baseline)
    {
        RequireGuid(listId, nameof(listId));
        ArgumentNullException.ThrowIfNull(baseline);
        var update = new Entity(ListEntityName, listId)
        {
            [AreaLeaderAttribute] = ToReference(baseline.AreaLeaderId, ContactEntityName),
            [AreaNameAttribute] = baseline.AreaName,
            [RaceLeaderAttribute] = ToReference(baseline.RaceLeaderId, ContactEntityName),
            [CoAreaLeaderAttribute] = ToReference(baseline.CoAreaLeaderId, ContactEntityName),
            [CoRaceLeaderAttribute] = ToReference(baseline.CoRaceLeaderId, ContactEntityName),
            [ViceFamilyLeaderAttribute] = ToReference(baseline.ViceFamilyLeaderId, ContactEntityName)
        };
        GetService().Update(update);
    }

    /// <summary>
    /// 讀取同一 task-owned contact 的 ownerid，且只接受 active-owner cleanup contract 所需的
    /// systemuser EntityReference。contact row identity、logical name 與 lookup type 任一不符即
    /// fail closed，不把 team 或其他 entity 混入 assignment rollback。
    /// </summary>
    public Guid ReadOwnerId(Guid contactId)
    {
        RequireGuid(contactId, nameof(contactId));
        var entity = GetService().Retrieve(ContactEntityName, contactId, new ColumnSet(OwnerAttribute));
        if (entity is null ||
            !string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            entity.Id != contactId)
        {
            throw new InvalidOperationException("The fixture contact owner projection is invalid.");
        }

        return ReadRequiredReference(entity, OwnerAttribute, SystemUserEntityName);
    }

    /// <summary>
    /// 以固定 AssignRequest 將 contact 還原到已讀取的 baseline systemuser。這個方法不重試，也不
    /// 接受任意 Target/Assignee entity；bridge 必須以後續 owner read-back 決定 cleanup 是否真的完成。
    /// </summary>
    public void RestoreOwner(Guid contactId, Guid baselineOwnerId)
    {
        RequireGuid(contactId, nameof(contactId));
        RequireGuid(baselineOwnerId, nameof(baselineOwnerId));
        GetService().Execute(new AssignRequest
        {
            Target = new EntityReference(ContactEntityName, contactId),
            Assignee = new EntityReference(SystemUserEntityName, baselineOwnerId)
        });
    }

    /// <summary>
    /// 讀取 transfer composite 的完整固定 graph：source/target membership、唯一 weekly report、最多一筆
    /// present record、contact primary-list lookup 與 owner。每個 query 均有固定 entity、欄位、date 和
    /// row bound，結果只投影成純值 snapshot，讓 partial 或跨 fixture record 在 mutation 前保持 no-go。
    /// </summary>
    public P72TransferGraphSnapshot ReadTransferGraph(P72TransferFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateTransferFixture(fixture);
        var sourcePresent = fixture.SourceListId is Guid sourceListId &&
                            ReadMembership(sourceListId, [fixture.ContactId]).PresentMemberIds.Contains(fixture.ContactId);
        var targetPresent = ReadMembership(fixture.TargetListId, [fixture.ContactId]).PresentMemberIds.Contains(fixture.ContactId);
        var weeklyReportId = ResolveWeeklyReport(fixture.TargetListId, fixture.WeekStartDate);
        var presentRecord = ReadPresentRecord(weeklyReportId, fixture);
        var primaryListId = ReadOptionalReference(
            GetService().Retrieve(ContactEntityName, fixture.ContactId, new ColumnSet(ContactPrimaryListAttribute)),
            ContactPrimaryListAttribute,
            ListEntityName);
        var ownerId = ReadOwnerId(fixture.ContactId);

        return new P72TransferGraphSnapshot(
            sourcePresent,
            targetPresent,
            presentRecord?.Id,
            presentRecord is not null &&
            presentRecord.WeeklyReportId == weeklyReportId &&
            presentRecord.ContactId == fixture.ContactId &&
            presentRecord.ListId == fixture.TargetListId &&
            presentRecord.WeekStartUtc == fixture.WeekStartDate.UtcDateTime,
            primaryListId,
            ownerId);
    }

    /// <summary>
    /// 對已由 bridge read-back 證實的 expected transfer graph 執行有序 rollback。先再次確認即將刪除的
    /// present record 仍是同一 fixture identity，接著刪除該 record、還原 primary list/owner/source
    /// membership 並移除 target membership；任一步失敗都拋回 bridge，禁止猜測或刪除未證實的資料。
    /// </summary>
    public void RestoreTransferGraph(
        P72TransferFixture fixture,
        P72TransferGraphSnapshot baseline,
        P72TransferGraphSnapshot expected)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(expected);
        ValidateTransferFixture(fixture);
        if (expected.PresentRecordId is not Guid presentRecordId ||
            presentRecordId == Guid.Empty ||
            !expected.PresentRecordMatches)
        {
            throw new InvalidOperationException("The fixture transfer cleanup record is not proven.");
        }

        var weeklyReportId = ResolveWeeklyReport(fixture.TargetListId, fixture.WeekStartDate);
        var observed = ReadPresentRecord(weeklyReportId, fixture);
        if (observed is null || observed.Id != presentRecordId ||
            observed.WeeklyReportId != weeklyReportId ||
            observed.ContactId != fixture.ContactId ||
            observed.ListId != fixture.TargetListId ||
            observed.WeekStartUtc != fixture.WeekStartDate.UtcDateTime)
        {
            throw new InvalidOperationException("The fixture transfer cleanup record changed or is ambiguous.");
        }

        var service = GetService();
        service.Delete(PresentRecordEntityName, presentRecordId);

        service.Update(new Entity(ContactEntityName, fixture.ContactId)
        {
            [ContactPrimaryListAttribute] = ToReference(baseline.PrimaryListId, ListEntityName)
        });

        if (fixture.TargetOwnerId.HasValue && baseline.OwnerId is Guid baselineOwnerId)
        {
            service.Execute(new AssignRequest
            {
                Target = new EntityReference(ContactEntityName, fixture.ContactId),
                Assignee = new EntityReference(SystemUserEntityName, baselineOwnerId)
            });
        }

        if (fixture.SourceListId is Guid sourceList && baseline.SourceMembershipPresent)
        {
            RestoreMembership(sourceList, [fixture.ContactId], new P72MembershipSnapshot([fixture.ContactId]));
        }

        RestoreMembership(
            fixture.TargetListId,
            [fixture.ContactId],
            new P72MembershipSnapshot(baseline.TargetMembershipPresent ? [fixture.ContactId] : Array.Empty<Guid>()));

    }

    /// <summary>釋放唯一 Data8 service；即使底層 Dispose 失敗，引用仍先清空而不會被重用。</summary>
    public void Dispose()
    {
        _service = null;
        var disposable = Interlocked.Exchange(ref _disposableService, null);
        disposable?.Dispose();
    }

    /// <summary>以 target list/date 唯一解析週報；零筆、多筆、paging 或 malformed row 均 fail closed。</summary>
    private Guid ResolveWeeklyReport(Guid listId, DateTimeOffset weekStartDate)
    {
        var query = new QueryExpression(WeeklyReportEntityName)
        {
            ColumnSet = new ColumnSet(WeeklyReportIdAttribute),
            NoLock = true,
            TopCount = 2
        };
        query.Criteria.AddCondition(WeeklyReportListAttribute, ConditionOperator.Equal, listId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(DateAttribute, ConditionOperator.Equal, weekStartDate.UtcDateTime);
        var rows = GetService().RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The fixture weekly-report response is missing.");
        if (rows.MoreRecords || rows.Entities.Count != 1)
        {
            throw new InvalidOperationException("The fixture weekly-report relationship is ambiguous.");
        }

        var row = rows.Entities[0];
        if (!string.Equals(row.LogicalName, WeeklyReportEntityName, StringComparison.Ordinal) || row.Id == Guid.Empty)
        {
            throw new InvalidOperationException("The fixture weekly-report projection is invalid.");
        }

        return row.Id;
    }

    /// <summary>讀取最多一筆 matching present record；多筆直接拋錯，禁止 cleanup 猜 record。</summary>
    private P72PresentRecord? ReadPresentRecord(Guid weeklyReportId, P72TransferFixture fixture)
    {
        var query = new QueryExpression(PresentRecordEntityName)
        {
            ColumnSet = new ColumnSet(
                PresentRecordWeeklyReportAttribute,
                PresentRecordContactAttribute,
                PresentRecordListAttribute,
                DateAttribute),
            NoLock = true,
            TopCount = 2
        };
        query.Criteria.AddCondition(PresentRecordWeeklyReportAttribute, ConditionOperator.Equal, weeklyReportId);
        query.Criteria.AddCondition(PresentRecordContactAttribute, ConditionOperator.Equal, fixture.ContactId);
        query.Criteria.AddCondition(DateAttribute, ConditionOperator.Equal, fixture.WeekStartDate.UtcDateTime);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var rows = GetService().RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The fixture present-record response is missing.");
        if (rows.MoreRecords || rows.Entities.Count > 1)
        {
            throw new InvalidOperationException("The fixture present-record state is ambiguous.");
        }

        if (rows.Entities.Count == 0)
        {
            return null;
        }

        var row = rows.Entities[0];
        return new P72PresentRecord(
            row.Id,
            ReadRequiredReference(row, PresentRecordWeeklyReportAttribute, WeeklyReportEntityName),
            ReadRequiredReference(row, PresentRecordContactAttribute, ContactEntityName),
            ReadRequiredReference(row, PresentRecordListAttribute, ListEntityName),
            ReadRequiredUtcDate(row, DateAttribute));
    }

    /// <summary>取得目前 service；Dispose 後所有操作 fail closed。</summary>
    private IOrganizationService GetService()
        => _service ?? throw new ObjectDisposedException(nameof(P72Data8ListManagementFixtureStore));

    /// <summary>
    /// 驗證單一 list 是 Slice C task-owned、static 且只接受 contact 成員的 marketing list。CE 9.1 的
    /// <c>type</c> 是 Boolean，<see langword="false"/> 才代表 static；<c>createdfromcode=2</c> 才代表
    /// contact-only。三個欄位共同存在才可通過，避免名稱相似、dynamic 或 Account/Lead list 在後續 mutation
    /// 才失敗。這個方法只 direct-Retrieve descriptor 已列出的 identity，拒絕使用 QueryExpression 掃描組織。
    /// </summary>
    private bool IsTaskOwnedStaticList(Guid listId)
    {
        var entity = GetService().Retrieve(
            ListEntityName,
            listId,
            new ColumnSet(ListNameAttribute, ListTypeAttribute, ListCreatedFromCodeAttribute));
        return entity is not null &&
               string.Equals(entity.LogicalName, ListEntityName, StringComparison.Ordinal) &&
               entity.Id == listId &&
               ReadRequiredText(entity, ListNameAttribute).StartsWith(SliceCListMarkerPrefix, StringComparison.Ordinal) &&
               entity.Attributes.TryGetValue(ListTypeAttribute, out var typeValue) &&
                typeValue is bool isDynamic &&
                !isDynamic &&
                entity.Attributes.TryGetValue(ListCreatedFromCodeAttribute, out var createdFromCodeValue) &&
                createdFromCodeValue is OptionSetValue { Value: ContactListCreatedFromCodeValue };
    }

    /// <summary>
    /// 驗證由 Slice A descriptor 繼承的 source contact 仍帶有遠端 fixture marker。description 是此
    /// task-owned contact 的固定 CE 標記；未標記、空值、錯 logical name 或錯 ID 都使 Slice C 在 dispatch
    /// 前停止，而不以本機 JSON 或 Windows identity 推測 record ownership。
    /// </summary>
    private bool HasSourceFixtureContactMarker(Guid contactId)
    {
        var entity = GetService().Retrieve(
            ContactEntityName,
            contactId,
            new ColumnSet(ContactDescriptionAttribute));
        return entity is not null &&
               string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
               entity.Id == contactId &&
               string.Equals(
                   ReadRequiredText(entity, ContactDescriptionAttribute),
                   SliceASourceContactMarker,
                   StringComparison.Ordinal);
    }

    /// <summary>
    /// 驗證 small-group leader 或 relationship-derived area leader 的姓名帶有 Slice C task marker。
    /// 這個固定 direct Retrieve 不接受 caller-selected field；只回傳 bool，避免 contact identity 或
    /// formatted value 跨出 store scope、寫入 evidence 或被快取到後續 request。
    /// </summary>
    private bool HasTaskOwnedContactName(Guid contactId)
    {
        RequireGuid(contactId, nameof(contactId));
        var entity = GetService().Retrieve(
            ContactEntityName,
            contactId,
            new ColumnSet(ContactFullNameAttribute));
        return entity is not null &&
               string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
               entity.Id == contactId &&
               ReadRequiredText(entity, ContactFullNameAttribute).StartsWith(SliceCListMarkerPrefix, StringComparison.Ordinal);
    }

    /// <summary>複製最多一千個 distinct contact IDs，與 connector 的 bounded membership contract 對齊。</summary>
    private static Guid[] CopyDistinctMembers(IReadOnlyList<Guid>? source)
    {
        if (source is null || source.Count is < 1 or > MaximumMemberIds)
        {
            throw new ArgumentException("The fixture member set is outside its bound.", nameof(source));
        }

        var copy = new Guid[source.Count];
        var seen = new HashSet<Guid>();
        for (var index = 0; index < source.Count; index++)
        {
            if (source[index] == Guid.Empty || !seen.Add(source[index]))
            {
                throw new ArgumentException("The fixture member set must be distinct and non-empty.", nameof(source));
            }

            copy[index] = source[index];
        }

        Array.Sort(copy);
        return copy;
    }

    /// <summary>驗證 transfer fixture 的 fixed date/list boundary；不使用本機 timezone 或隱藏 default。</summary>
    private static void ValidateTransferFixture(P72TransferFixture fixture)
    {
        RequireGuid(fixture.ContactId, nameof(fixture.ContactId));
        RequireGuid(fixture.TargetListId, nameof(fixture.TargetListId));
        if (fixture.SourceListId is Guid sourceListId)
        {
            RequireGuid(sourceListId, nameof(fixture.SourceListId));
            if (sourceListId == fixture.TargetListId)
            {
                throw new ArgumentException("The fixture source and target lists must differ.", nameof(fixture));
            }
        }

        var utc = fixture.WeekStartDate.ToUniversalTime();
        if (fixture.WeekStartDate.Offset != TimeSpan.Zero || utc.TimeOfDay != TimeSpan.Zero || utc.DayOfWeek != DayOfWeek.Sunday)
        {
            throw new ArgumentException("The fixture week start must be UTC Sunday midnight.", nameof(fixture));
        }
    }

    /// <summary>驗證 non-empty GUID，不輸出原值。</summary>
    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty fixture identity is required.", parameterName);
        }
    }

    /// <summary>讀取 nullable lookup，缺欄/null 是合法 baseline，錯 logical name 或型別則 fail closed。</summary>
    private static Guid? ReadOptionalReference(Entity entity, string attributeName, string expectedLogicalName)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value is EntityReference reference &&
               reference.Id != Guid.Empty &&
               string.Equals(reference.LogicalName, expectedLogicalName, StringComparison.Ordinal)
            ? reference.Id
            : throw new InvalidOperationException("The fixture lookup projection is invalid.");
    }

    /// <summary>讀取 required lookup，避免以 ToString 或寬鬆 cast 把其他 entity identity 混入 graph。</summary>
    private static Guid ReadRequiredReference(Entity entity, string attributeName, string expectedLogicalName)
        => ReadOptionalReference(entity, attributeName, expectedLogicalName)
            ?? throw new InvalidOperationException("The fixture required lookup is missing.");

    /// <summary>
    /// 讀取固定 <c>listmember</c> lookup 的 identity。真機 Dataverse service 會以
    /// <see cref="EntityReference"/> 交付 lookup；離線 connector fake 可能以裸 <see cref="Guid"/>
    /// 表示同一個 uniqueidentifier。為了讓 live evidence 與離線 contract 共用相同 bounded read-back，
    /// 此處只接受非空 GUID，且 reference 必須精確指向預期的 list 或 contact logical name；任何其他
    /// SDK 型別、缺欄或錯誤 entity 都 fail closed。SDK row 只在此 method scope 存活，不會放入 snapshot、
    /// log、cache 或跨 test state。
    /// </summary>
    /// <param name="entity">目前 query 回傳的短生命期 listmember row。</param>
    /// <param name="attributeName">固定 allowlisted lookup 欄位。</param>
    /// <param name="expectedLogicalName">該 lookup 必須指向的固定 logical name。</param>
    /// <returns>已驗證的非空 identity。</returns>
    /// <exception cref="InvalidOperationException">CRM lookup projection 無法證實時擲回，讓 bridge 保持 no-go。</exception>
    private static Guid ReadListMemberLookupId(Entity entity, string attributeName, string expectedLogicalName)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            throw new InvalidOperationException("The fixture membership lookup projection is invalid.");
        }

        return value switch
        {
            Guid guid when guid != Guid.Empty => guid,
            EntityReference reference when reference.Id != Guid.Empty &&
                                          string.Equals(reference.LogicalName, expectedLogicalName, StringComparison.Ordinal) => reference.Id,
            _ => throw new InvalidOperationException("The fixture membership lookup projection is invalid.")
        };
    }

    /// <summary>讀取 nullable bounded text；CRM 欄位錯型別立即拒絕，不保留 formatted value dictionary。</summary>
    private static string? ReadOptionalText(Entity entity, string attributeName)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? throw new InvalidOperationException("The fixture text projection is invalid.");
    }

    /// <summary>讀取 required bounded text，空字串或錯型別均視為 relationship ambiguity。</summary>
    private static string ReadRequiredText(Entity entity, string attributeName)
        => ReadOptionalText(entity, attributeName) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("The fixture required text is missing.");

    /// <summary>將純 GUID 還原成固定 logical-name lookup；null 的明確語意是清除該欄位。</summary>
    private static EntityReference? ToReference(Guid? id, string logicalName)
        => id is Guid value && value != Guid.Empty
            ? new EntityReference(logicalName, value)
            : null;

    /// <summary>讀取 explicit UTC DateTime；CE 未指定 Kind 時採明確 UTC，不使用主機 local timezone。</summary>
    private static DateTime ReadRequiredUtcDate(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is not DateTime date)
        {
            throw new InvalidOperationException("The fixture date projection is invalid.");
        }

        return date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date.ToUniversalTime();
    }

    /// <summary>固定 present-record projection；SDK Entity 不跨出 store method。</summary>
    private sealed record P72PresentRecord(
        Guid Id,
        Guid WeeklyReportId,
        Guid ContactId,
        Guid ListId,
        DateTime WeekStartUtc);
}
