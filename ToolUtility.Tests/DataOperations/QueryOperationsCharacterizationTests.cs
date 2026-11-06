/*
 * 檔案整體說明
 *
 * 目的與定位：在搬移與安全純化之前鎖定三組 SDK-only 操作的全部查詢，而非以重寫結果反推 golden。 Golden 由固定舊來源模板填入合成資料；每案比較真正送到 SDK 的完整 XML，保留排序與欄位。
 *
 * 主要功能與流程：
 * 本檔宣告 QueryOperationsCharacterizationTests、DataOperationFixture。
 * - 列出固定有限的公開操作；不讀機器設定、網路、個資或動態列舉外部輸入。
 * - SDK 回傳兩筆同名不同 ID 與分頁，服務不得去重、改寫或丟棄分頁欄位。 此搬前基準局部固定西曆並 finally 還原，不修改程序預設文化或寫回 golden。
 * - 舊 RetrieveByDate 雖有日期參數，實際不加日期條件；本階段搬移不擅自修改這項既有契約。 比對型別化 QueryByAttribute 與 AllColumns、空條件，日期篩選維持由明確區間 API 提供。
 * - 建立同名不同 ID 的完整合成候選，確保測試能區分按名稱去重與正確 ID 驗證。
 * 測試案例入口包含 Operations、ExistingQuery_MatchesGoldenAndPreservesCompleteResult、
 * AppointmentByDate_PreservesLegacyUnfilteredQuery、EntityName、Rows、Run。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在搬移與安全純化之前鎖定三組 SDK-only 操作的全部查詢，而非以重寫結果反推 golden。
    /// Golden 由固定舊來源模板填入合成資料；每案比較真正送到 SDK 的完整 XML，保留排序與欄位。
    /// </summary>
    public sealed class QueryOperationsCharacterizationTests
    {
        /// <summary>列出固定有限的公開操作；不讀機器設定、網路、個資或動態列舉外部輸入。</summary>
        public static IEnumerable<object[]> Operations()
        {
            foreach (var name in new[]
            {
                "appointment-range", "appointment-contact", "appointment-schedule",
                "lessons-enrolled", "lessons-month", "lessons-stor", "meeting-sunday",
                "meeting-weekly", "meeting-contact", "meeting-weekly-alt", "meeting-expired", "meeting-group"
            }) yield return new object[] { name };
        }

        /// <summary>
        /// SDK 回傳兩筆同名不同 ID 與分頁，服務不得去重、改寫或丟棄分頁欄位。
        /// 此搬前基準局部固定西曆並 finally 還原，不修改程序預設文化或寫回 golden。
        /// </summary>
        /// <param name="operation">固定測試操作與 golden 檔名。</param>
        [Theory]
        [MemberData(nameof(Operations))]
        public void ExistingQuery_MatchesGoldenAndPreservesCompleteResult(string operation)
        {
            var expected = DataOperationFixture.Rows(operation);
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(operation + ".xml",
                    Assert.IsType<FetchExpression>(query).Query)).Returns(expected);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var result = DataOperationFixture.Run(operation, client.Object);
                Assert.Same(expected, result);
                Assert.Equal(2, result.Entities.Count);
                Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
                Assert.Equal(result.Entities[0]["name"], result.Entities[1]["name"]);
                Assert.True(result.MoreRecords);
                Assert.Equal("Synthetic cookie", result.PagingCookie);
            }
            finally { CultureInfo.CurrentCulture = previous; }
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 舊 RetrieveByDate 雖有日期參數，實際不加日期條件；本階段搬移不擅自修改這項既有契約。
        /// 比對型別化 QueryByAttribute 與 AllColumns、空條件，日期篩選維持由明確區間 API 提供。
        /// </summary>
        [Fact]
        public void AppointmentByDate_PreservesLegacyUnfilteredQuery()
        {
            var expected = DataOperationFixture.Rows("appointment-range");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryByAttribute>(query);
                    Assert.Equal("appointment", actual.EntityName);
                    Assert.True(actual.ColumnSet.AllColumns);
                    Assert.Empty(actual.Attributes);
                    Assert.Empty(actual.Values);
                }).Returns(expected);
            var service = new AppointmentService(new object(), client.Object);
            Assert.Same(expected, service.RetrieveByDate(new DateTime(2027, 1, 2)));
        }
    }

    /// <summary>
    /// 合成查詢測試的固定資料及真實入口轉接；不重做產品查詢樹、驗證或資料發布邏輯。
    /// 每次建立獨立 EntityCollection，無共用 SDK、憑證或可變結果。
    /// </summary>
    internal static class DataOperationFixture
    {
        internal const string PrimaryId = "11111111-1111-4111-8111-111111111111";
        internal const string SecondaryId = "22222222-2222-4222-8222-222222222222";

        /// <summary>依操作選擇應回傳根實體，只包含有限的資料庫結構名稱。</summary>
        internal static string EntityName(string operation) => operation.StartsWith("appointment-", StringComparison.Ordinal)
            ? "appointment" : operation == "lessons-stor" ? "new_stor_lessons"
            : operation.StartsWith("lessons-", StringComparison.Ordinal) ? "new_disciple_lessons" : "new_present_record";

        /// <summary>建立同名不同 ID 的完整合成候選，確保測試能區分按名稱去重與正確 ID 驗證。</summary>
        internal static EntityCollection Rows(string operation) => new EntityCollection(new[]
        {
            new Entity(EntityName(operation), new Guid(PrimaryId)) { ["name"] = "Synthetic Same Name" },
            new Entity(EntityName(operation), new Guid(SecondaryId)) { ["name"] = "Synthetic Same Name" }
        }) { MoreRecords = true, PagingCookie = "Synthetic cookie" };

        /// <summary>
        /// 直接呼叫產品方法；所有參數只活於當案，替身只能在 SDK 邊界觀察真正查詢或注入故障。
        /// 日期與 ID 可替換以重現無效區間、文化、XML 字元與格式驗證，不把驗證寫進測試轉接。
        /// </summary>
        internal static EntityCollection Run(string operation, IOrganizationService sdk,
            string? text = null, string primaryId = PrimaryId, string secondaryId = SecondaryId,
            DateTime? start = null, DateTime? end = null)
        {
            var first = start ?? new DateTime(2027, 1, 2);
            var last = end ?? new DateTime(2027, 3, 4);
            switch (operation)
            {
                case "appointment-range": return new AppointmentService(new object(), sdk).RetrieveByDateRange(first, last);
                case "appointment-contact": return new AppointmentService(new object(), sdk)
                    .RetrieveByContactWithinYear(text ?? "Synthetic Contact", primaryId);
                case "appointment-schedule": return new AppointmentService(new object(), sdk)
                    .RetrieveByDateRangeAndScheduleType(first, last, text ?? "100000001");
                case "lessons-enrolled": return new LessonsService(new object(), sdk)
                    .RetrieveEnrolledLessons(first, last, text ?? "Synthetic Contact", primaryId);
                case "lessons-month": return new LessonsService(new object(), sdk).RetrieveLessonsByMonth(first, last);
                case "lessons-stor": return new LessonsService(new object(), sdk)
                    .RetrieveStorLessons(text ?? "Synthetic Lesson", secondaryId, text ?? "Synthetic Contact", primaryId);
                case "meeting-sunday": return new MeetingStatisticsService(new object(), sdk).RetrieveBySunday(first);
                case "meeting-weekly": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveByWeeklyReportAndContact(text ?? "Synthetic Weekly", secondaryId, text ?? "Synthetic Contact", primaryId);
                case "meeting-contact": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveBySundayDateAndContact(text ?? "Synthetic Contact", primaryId, first);
                case "meeting-weekly-alt": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveByWeeklyReportAndContactAlt(text ?? "Synthetic Contact", primaryId, text ?? "Synthetic Weekly", secondaryId);
                case "meeting-expired": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveWithExpiredDateByContact(text ?? "Synthetic Contact", primaryId);
                case "meeting-group": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveByContactSmallGroupAndSundayDate(text ?? "Synthetic Contact", primaryId, text ?? "Synthetic Group", secondaryId, first);
                default: throw new ArgumentException("Unknown synthetic operation.", nameof(operation));
            }
        }
    }
}
