/*
 * 檔案整體說明
 *
 * 目的與定位：以完全離線的 Dataverse 替身驗證 P5-11 收費單觸發服務。測試涵蓋
 * fee→contact→account 的正式關聯、教會時區日期、每日穩定 sourceId、payload 兩欄
 * 契約與未來付款日不入列；不連 D365、永豐或 LINE，也不讀取 User Secrets。
 *
 * 主要功能與流程：每個案例建立獨立的合成 SDK、固定教會時鐘與收費單資料，呼叫真正的
 * FeeAllotTaskService.Enqueue，再檢查 SDK 收到的 new_outboundtask 欄位。SDK 不接受
 * 未列入契約的查詢、更新、刪除或 Execute，避免測試通過於錯誤的額外 I/O。
 *
 * 使用邊界與維護：測試替身由單一案例擁有，沒有 static 可變資料、背景工作、連線、
 * timer 或 disposable 資源；服務只借用 SDK。資料列唯一性仍由正式資料庫替代鍵負責，
 * 本檔只驗證服務送出的穩定鍵與內容，離線結果不冒充實機部署或實機影子驗收。
 */
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Fees;

/// <summary>驗證收費單每日撥款待辦的關聯解析、時區日期及未來日期關卡。</summary>
public sealed class FeeAllotTaskServiceTests
{
    private static readonly Guid FeeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ContactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ChurchId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TaskId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>同一收費單同一教會日送出的穩定鍵與 payload 相同，供資料庫唯一鍵合併為一筆。</summary>
    [Fact]
    public void Fees_AllotTask_SameFeeSameDay_OneTask()
    {
        var first = CreateSdk(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc));
        var second = CreateSdk(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc));

        var firstId = FeeAllotTaskService.Enqueue(first, FeeId, new FixedClock(new DateTime(2026, 11, 13)));
        var secondId = FeeAllotTaskService.Enqueue(second, FeeId, new FixedClock(new DateTime(2026, 11, 13)));

        Assert.Equal(TaskId, firstId);
        Assert.Equal(firstId, secondId);
        Assert.Equal(first.CreatedRow!["new_idempotencykey"], second.CreatedRow!["new_idempotencykey"]);
        Assert.Equal(first.CreatedRow["new_payload"], second.CreatedRow["new_payload"]);
    }

    /// <summary>教會日跨一天後必須產生新的來源識別，不把昨日待辦誤當成今日查詢。</summary>
    [Fact]
    public void Fees_AllotTask_SameFeeNextDay_NewTask()
    {
        var first = CreateSdk(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc));
        var second = CreateSdk(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc));

        FeeAllotTaskService.Enqueue(first, FeeId, new FixedClock(new DateTime(2026, 11, 13)));
        FeeAllotTaskService.Enqueue(second, FeeId, new FixedClock(new DateTime(2026, 11, 14)));

        Assert.NotEqual(first.CreatedRow!["new_idempotencykey"], second.CreatedRow!["new_idempotencykey"]);
        Assert.NotEqual(first.CreatedRow["new_payload"], second.CreatedRow["new_payload"]);
    }

    /// <summary>待辦 queryDate 使用已授權教會時鐘，不使用執行主機或 UTC 的日期。</summary>
    [Fact]
    public void Fees_AllotTask_DayUsesChurchTimeZone()
    {
        var sdk = CreateSdk(new DateTime(2026, 11, 12, 16, 30, 0, DateTimeKind.Utc));
        var clock = new FixedClock(new DateTime(2026, 11, 13));

        FeeAllotTaskService.Enqueue(sdk, FeeId, clock);

        Assert.Equal("Asia/Taipei", clock.LastTimeZoneId);
        Assert.Contains(
            "\"queryDate\":20261113",
            (string)sdk.CreatedRow!["new_payload"],
            StringComparison.Ordinal);
    }

    /// <summary>付款日位於教會今日之後時正常結束且不建立待辦。</summary>
    [Fact]
    public void Fees_AllotTask_FuturePayDate_NotEnqueued()
    {
        var sdk = CreateSdk(new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

        var result = FeeAllotTaskService.Enqueue(sdk, FeeId, new FixedClock(new DateTime(2026, 11, 13)));

        Assert.Null(result);
        Assert.Null(sdk.CreatedRow);
    }

    private static SyntheticFeeService CreateSdk(DateTime payDateUtc) =>
        new(payDateUtc);

    /// <summary>固定不讀系統時間的教會時鐘；只記錄服務傳入的已授權時區。</summary>
    private sealed class FixedClock(DateTime today) : IChurchClock
    {
        public string? LastTimeZoneId { get; private set; }
        public DateTimeOffset UtcNow => new(today, TimeSpan.Zero);
        public DateTime ToChurchTime(DateTime utc, string ianaTimeZoneId) => today;
        public DateTime TodayInChurch(string ianaTimeZoneId)
        {
            LastTimeZoneId = ianaTimeZoneId;
            return DateTime.SpecifyKind(today.Date, DateTimeKind.Unspecified);
        }
        public DateTime ToUtc(LocalDateTimeOfChurch local) => local.Value;
    }

    /// <summary>只實作收費觸發所需的三次 Retrieve 與一次 Create，其餘 SDK 入口全部拒絕。</summary>
    private sealed class SyntheticFeeService : IOrganizationService
    {
        private readonly DateTime payDateUtc;

        public SyntheticFeeService(DateTime payDateUtc) => this.payDateUtc = payDateUtc;
        public Entity? CreatedRow { get; private set; }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (entityName == "new_fee" && id == FeeId)
            {
                var fee = new Entity("new_fee", FeeId) { RowVersion = "row-1" };
                fee["new_pay_way"] = new OptionSetValue(100000002);
                fee["new_pay_date"] = payDateUtc;
                fee["new_contact_new_fee"] = new EntityReference("contact", ContactId);
                fee["new_q_pay_card_order_no"] = string.Empty;
                fee["new_q_paid_card_order_no"] = string.Empty;
                fee["new_q_pay_order_atm_no"] = "ATM-001";
                fee["new_q_paid_order_atm_no"] = string.Empty;
                return fee;
            }

            if (entityName == "contact" && id == ContactId)
                return new Entity("contact", ContactId)
                {
                    ["parentcustomerid"] = new EntityReference("account", ChurchId)
                };

            if (entityName == "account" && id == ChurchId)
                return new Entity("account", ChurchId) { ["new_timezone"] = "Asia/Taipei" };

            throw new InvalidOperationException("Unexpected synthetic retrieve.");
        }

        public Guid Create(Entity entity)
        {
            Assert.Equal("new_outboundtask", entity.LogicalName);
            CreatedRow = entity;
            return TaskId;
        }

        public void Update(Entity entity) => throw new InvalidOperationException("Unexpected update.");
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected delete.");
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected associate.");
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected disassociate.");
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException("Unexpected query.");
        public OrganizationResponse Execute(OrganizationRequest request) => throw new InvalidOperationException("Unexpected execute.");
    }
}
