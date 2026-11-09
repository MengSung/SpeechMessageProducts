/*
檔案整體說明

目的與定位：保護 P3-06 課程簽到與同工聚會的核心資料契約，驗證移植自
LineMessagePlugIn/Attendance 的實際欄位、角色限制、正式 rowversion 更新及
跨來源隔離。

主要功能與流程：測試以窄化的 IOrganizationService 合成 new_disciple_lessons、
new_stor_lessons、contact 及 new_present_record；課程簽到故意讓 actor Contact
與顯示來源 Contact 不同，確認課程名稱只來自已驗證的 display Contact，而
課程記錄的授權 lookup 仍使用 actor Contact。同工聚會測試既有週記錄更新、
允許／拒絕的 customertypecode、查無記錄的具名延後及失敗時零寫入。

使用邊界與維護：本檔不連線、不讀設定或秘密，也不代表 D365 實機結果。每個
案例獨占 SDK、Entity 與更新清單；SDK 故障、重複列、角色不符及缺記錄均由
決定性斷言鎖住。測試 owner 不擁有任何外部資源，所有結果只存活於單次測試。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Attendance;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>驗證課程與同工出席的實際欄位更新及 fail-closed 邊界。</summary>
public sealed class WebhookAttendanceContractTests
{
    /// <summary>
    /// 顯示來源與 actor 不同時，課程查詢只能使用已驗證的顯示 Contact 名稱作提示，
    /// 但必須經 actor 的正式課程關聯取得課程；寫入則必須鎖定 actor 的正式
    /// Contact lookup 及 rowversion。
    /// </summary>
    [Fact]
    public void ClassAttendance_UsesDisplayContactForLessonAndActorForEnrollment()
    {
        var sdk = new AttendanceSdk();

        var result = WebhookCourseAttendanceHandler.Handle(
            sdk,
            sdk.Actor,
            sdk.DisplayContact,
            sdk.DisplaySourceId,
            "第一課簽到",
            sdk.Clock,
            CancellationToken.None);

        Assert.Equal(WebhookBusinessStatus.Applied, result.Status);
        var update = Assert.Single(sdk.Updates);
        Assert.Equal(sdk.StoredLesson.Id, update.Id);
        Assert.True(update.GetAttributeValue<bool>("new_1_present"));
        Assert.Equal(new DateTime(2025, 1, 5), update.GetAttributeValue<DateTime>("new_1_signon_time"));
        Assert.DoesNotContain(sdk.Queries, query => query.EntityName == "new_disciple_lessons");
        Assert.Contains(sdk.Queries, query =>
            query.EntityName == "new_stor_lessons" &&
            query.Criteria.Conditions.Any(condition =>
                condition.AttributeName == "new_contact_new_stor_lessons" &&
                Equals(condition.Values.Single(), sdk.Actor.ContactId)) &&
            query.LinkEntities.Any(link =>
                link.LinkToEntityName == "new_disciple_lessons" &&
                link.LinkCriteria.Conditions.Any(condition =>
                    condition.AttributeName == "new_name" &&
                    Equals(condition.Values.Single(), "Displayed learner"))));
    }

    /// <summary>課程候選出現重複正式列時必須拒絕發布，不能任意取第一筆。</summary>
    [Fact]
    public void ClassAttendance_DuplicateCourseRows_FailsClosedWithoutUpdate()
    {
        var sdk = new AttendanceSdk { DuplicateLessons = true };

        Assert.Throws<InvalidOperationException>(() => WebhookCourseAttendanceHandler.Handle(
            sdk,
            sdk.Actor,
            sdk.DisplayContact,
            sdk.DisplaySourceId,
            "第一課簽到",
            sdk.Clock,
            CancellationToken.None));

        Assert.Empty(sdk.Updates);
    }

    /// <summary>允許的門徒角色只能更新既有本週記錄，並帶正式 rowversion 條件。</summary>
    [Fact]
    public void StaffMeeting_AllowedRole_UpdatesExistingWeeklyRecord()
    {
        var sdk = new AttendanceSdk
        {
            ActorCustomertypecode = 100000006
        };

        var result = WebhookStaffMeetingHandler.Handle(
            sdk,
            sdk.Actor,
            sdk.ActorContact,
            "門徒訓練班",
            sdk.Clock,
            CancellationToken.None);

        Assert.Equal(WebhookBusinessStatus.Applied, result.Status);
        var update = Assert.Single(sdk.Updates);
        Assert.Equal(sdk.PresentRecord.Id, update.Id);
        Assert.Equal(1, update.GetAttributeValue<int>("new_child_number"));
        Assert.Equal(new DateTime(2025, 1, 5), update.GetAttributeValue<DateTime>("new_child_on_time"));
        Assert.Equal("7", update.RowVersion);
    }

    /// <summary>不在該聚會角色白名單的 Contact 不得寫入任何出席欄位。</summary>
    [Fact]
    public void StaffMeeting_DisallowedRole_RejectsBeforeUpdate()
    {
        var sdk = new AttendanceSdk
        {
            ActorCustomertypecode = 100000001
        };

        var result = WebhookStaffMeetingHandler.Handle(
            sdk,
            sdk.Actor,
            sdk.ActorContact,
            "領袖大聚",
            sdk.Clock,
            CancellationToken.None);

        Assert.Equal(WebhookBusinessStatus.Rejected, result.Status);
        Assert.Equal("StaffMeetingRoleNotAllowed", result.Code);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>查無本週正式記錄時只能具名延後，避免 read-then-create 競態產生重複列。</summary>
    [Fact]
    public void StaffMeeting_MissingWeeklyRecord_IsDeferredWithoutCreate()
    {
        var sdk = new AttendanceSdk { MissingPresentRecord = true };

        var result = WebhookStaffMeetingHandler.Handle(
            sdk,
            sdk.Actor,
            sdk.ActorContact,
            "門徒大聚",
            sdk.Clock,
            CancellationToken.None);

        Assert.Equal(WebhookBusinessStatus.Deferred, result.Status);
        Assert.Equal("StaffMeetingWeeklyRecordRequired", result.Code);
        Assert.Empty(sdk.Updates);
        Assert.Empty(sdk.Creates);
    }

    /// <summary>固定教會時間，不依賴測試主機 local timezone 或當下時鐘。</summary>
    private sealed class FixedClock : IChurchClock
    {
        /// <summary>固定 UTC 瞬間，對應測試的 2025-01-05 教會日期。</summary>
        public DateTimeOffset UtcNow => new(2025, 1, 5, 0, 0, 0, TimeSpan.Zero);

        /// <summary>測試只使用固定日期，不執行外部時區轉換。</summary>
        public DateTime ToChurchTime(DateTime utcNow, string timeZone) => utcNow;

        /// <summary>固定教會日期。</summary>
        public DateTime TodayInChurch(string timeZone) => new(2025, 1, 5);

        /// <summary>本測試不需要 local 反向轉換。</summary>
        public DateTime ToUtc(LocalDateTimeOfChurch local) => throw new InvalidOperationException("Unexpected local-time conversion.");
    }

    /// <summary>
    /// 只提供本測試需要的資料表與操作；所有非白名單 SDK 操作均失敗，
    /// 防止核心以未核定的查詢或建立路徑掩蓋錯誤。
    /// </summary>
    private sealed class AttendanceSdk : IOrganizationService
    {
        /// <summary>建立獨立的組織、教會、actor、display、課程與本週記錄。</summary>
        public AttendanceSdk()
        {
            Actor = new WebhookActor(
                Guid.NewGuid(),
                "123",
                Guid.NewGuid(),
                Guid.NewGuid(),
                "U" + new string('0', 32),
                "Asia/Taipei");
            ActorContact = new Entity("contact", Actor.ContactId)
            {
                ["parentcustomerid"] = new EntityReference("account", Actor.ChurchId),
                ["new_lineid"] = Actor.UserId,
                ["fullname"] = "Actor",
                ["customertypecode"] = new OptionSetValue(100000006)
            };
            DisplaySourceId = "U" + new string('1', 32);
            DisplayContact = new Entity("contact", Guid.NewGuid())
            {
                ["parentcustomerid"] = new EntityReference("account", Actor.ChurchId),
                ["new_lineid"] = DisplaySourceId,
                ["fullname"] = "Displayed learner"
            };
            Lesson = new Entity("new_disciple_lessons", Guid.NewGuid())
            {
                ["new_name"] = "Displayed learner",
                ["statecode"] = new OptionSetValue(0)
            };
            StoredLesson = new Entity("new_stor_lessons", Guid.NewGuid())
            {
                ["new_contact_new_stor_lessons"] = new EntityReference("contact", Actor.ContactId),
                ["new_new_disciple_lessons_new_stor_les"] = new EntityReference("new_disciple_lessons", Lesson.Id),
                ["new_enroll_status"] = new OptionSetValue(100000001),
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
                RowVersion = "7"
            };
            PresentRecord = new Entity("new_present_record", Guid.NewGuid())
            {
                ["new_contact_new_present_record"] = new EntityReference("contact", Actor.ContactId),
                ["new_sunday_date"] = new DateTime(2025, 1, 5),
                RowVersion = "7"
            };
        }

        /// <summary>本測試的 actor 授權邊界。</summary>
        public WebhookActor Actor { get; }

        /// <summary>Actor 的完整查詢結果，包含角色值。</summary>
        public Entity ActorContact { get; }

        /// <summary>只供課程名稱比對的另一個已驗證顯示來源。</summary>
        public Entity DisplayContact { get; }

        /// <summary>顯示來源的正式 LINE ID。</summary>
        public string DisplaySourceId { get; }

        /// <summary>可供測試直接使用的課程候選。</summary>
        public Entity Lesson { get; }

        /// <summary>可供測試直接使用的課程記錄。</summary>
        public Entity StoredLesson { get; }

        /// <summary>可供測試直接使用的本週出席記錄。</summary>
        public Entity PresentRecord { get; }

        /// <summary>固定時間 owner。</summary>
        public IChurchClock Clock { get; } = new FixedClock();

        /// <summary>測試可注入重複課程候選。</summary>
        public bool DuplicateLessons { get; set; }

        /// <summary>測試可注入查無本週記錄。</summary>
        public bool MissingPresentRecord { get; set; }

        /// <summary>測試可注入 customertypecode，模擬不同角色。</summary>
        public int ActorCustomertypecode
        {
            get => ActorContact.GetAttributeValue<OptionSetValue>("customertypecode")?.Value ?? 0;
            set => ActorContact["customertypecode"] = new OptionSetValue(value);
        }

        /// <summary>記錄所有 QueryExpression，以確認查詢使用正式 ID 與顯示來源名稱的正確邊界。</summary>
        public List<QueryExpression> Queries { get; } = [];

        /// <summary>記錄條件更新候選。</summary>
        public List<Entity> Updates { get; } = [];

        /// <summary>記錄不應發生的直接建立。</summary>
        public List<Entity> Creates { get; } = [];

        /// <summary>依本測試白名單回傳 Contact、課程及出席記錄。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (entityName == "contact" && id == ActorContact.Id) return ActorContact;
            if (entityName == "contact" && id == DisplayContact.Id) return DisplayContact;
            if (entityName == "new_disciple_lessons" && id == Lesson.Id) return Lesson;
            if (entityName == "new_stor_lessons" && id == StoredLesson.Id) return StoredLesson;
            if (entityName == "new_present_record" && id == PresentRecord.Id) return PresentRecord;
            throw new InvalidOperationException("Unexpected retrieve.");
        }

        /// <summary>依實體名稱回傳完整候選集合，不靜默略過重複或缺少資料。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            Queries.Add(expression);
            return expression.EntityName switch
            {
                "new_stor_lessons" when DuplicateLessons => new EntityCollection(new[]
                {
                    StoredLesson,
                    new Entity(StoredLesson.LogicalName, Guid.NewGuid())
                    {
                        ["new_contact_new_stor_lessons"] = new EntityReference("contact", Actor.ContactId),
                        ["new_new_disciple_lessons_new_stor_les"] = new EntityReference("new_disciple_lessons", Lesson.Id),
                        ["new_enroll_status"] = new OptionSetValue(100000001),
                        ["statecode"] = new OptionSetValue(0),
                        ["statuscode"] = new OptionSetValue(1),
                        RowVersion = "8"
                    }
                }),
                "new_stor_lessons" => new EntityCollection(new[] { StoredLesson }),
                "new_present_record" when MissingPresentRecord => new EntityCollection(),
                "new_present_record" => new EntityCollection(new[] { PresentRecord }),
                _ => throw new InvalidOperationException("Unexpected query.")
            };
        }

        /// <summary>只接受 rowversion 條件更新；其他 request 代表核心越界。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is UpdateRequest update)
            {
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
                Updates.Add(update.Target);
                return new UpdateResponse();
            }

            throw new InvalidOperationException("Unexpected execute.");
        }

        /// <summary>直接建立在本包是禁止路徑，保留紀錄供缺記錄測試斷言。</summary>
        public Guid Create(Entity entity)
        {
            Creates.Add(entity);
            throw new InvalidOperationException("Direct create is not permitted.");
        }

        /// <summary>不允許不帶 rowversion 的更新。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Unconditional update.");

        /// <summary>本包不允許刪除。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected delete.");

        /// <summary>本包不允許關聯建立。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected associate.");

        /// <summary>本包不允許關聯移除。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected disassociate.");
    }
}
