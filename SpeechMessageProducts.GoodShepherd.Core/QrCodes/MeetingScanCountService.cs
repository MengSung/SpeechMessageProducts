/*
 * 檔案整體說明
 * 目的與定位：GoodShepherd Core 的 P5-09 掃描統計入口，供活動外殼借入已授權 SDK，
 * 以權威會議主鍵重算出席列的三十八組非空掃描欄位，不依姓名或會員人數去重。
 * 欄位對照採操作局部二維字串陣列，避免將 ValueTuple 型別依賴帶入部分信任沙箱。
 * 主要功能與流程：先讀 meeting 確認正式身分，再按 lookup 精確讀取出席，完整驗證候選
 * 後以單筆最小 patch 發布全部計數；空集合仍覆寫為零，讀取失敗不可發布部分候選。
 * 每頁最多五百列、整輪一百頁／一萬列、每個 cookie 至多 32768 字元；超量即拒絕，
 * 不把截斷集合當完整計數。以正式主鍵穩定排序，保留無 cookie 的有界簡單分頁。
 * 使用邊界與維護：服務不保存 SDK、Entity、使用者或組織狀態；SDK 只在同步入口借用，
 * 由外殼擁有逾時、授權、告警與排空責任，不建立背景工作、快取、timer 或連線。
 * 本地候選只存活於本次 Execute，最多保留一頁 Entity、一萬個 ID 與一百個 cookie；
 * 不承諾跨頁資料庫快照或跨呼叫樂觀並行保護，並行相同會議仍為最後完成寫入者勝出。
 * SDK 故障保留原例外交外殼；Update 結果不確定時不自動重試或宣稱回滾。
 * 驗證以離線雙宿主正式規則測試保護欄位對照、身分與發布邊界，不冒充實機結果。
 */
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessageProducts.GoodShepherd.Core.QrCodes;

/// <summary>無請求欄位的統計服務；同一實例可供不同授權 SDK 並行呼叫，沒有共享候選。</summary>
public sealed class MeetingScanCountService
{
    /// <summary>重算全部掃描欄位，唯一發布點必須位於完整讀取與驗證之後；SDK 由外殼管理同步期限。</summary>
    /// <param name="service">原授權範圍的借用 SDK，不保存、不 Dispose，也不提升權限。</param>
    /// <param name="meetingId">平台主要會議正式主鍵；仍須由 SDK 權威讀回確認存在與同一身分。</param>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The instance shape is retained for activity-shell compatibility and stores no execution state.")]
    public void Execute(IOrganizationService service, Guid meetingId)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(service);
#else
        if (service is null) throw new ArgumentNullException(nameof(service));
#endif
        if (meetingId == Guid.Empty) throw new ArgumentException("A meeting identity is required.", nameof(meetingId));
        var meeting = service.Retrieve("new_meeting_statistics", meetingId, new ColumnSet("new_meeting_statisticsid"));
        ValidateIdentity(meeting, "new_meeting_statistics");
        if (meeting.Id != meetingId) throw Invalid("Unexpected meeting identity.");

        // 常數欄位對照也採操作局部陣列，不保存任何跨活動可變集合。
        var pairs = FieldPairs();
        var fieldCount = pairs.GetLength(0);
        var counts = new int[fieldCount];
        var columns = new string[fieldCount + 2];
        for (var i = 0; i < fieldCount; i++) columns[i] = pairs[i, 0];
        columns[fieldCount] = "new_present_recordid";
        columns[fieldCount + 1] = "new_meeting_statistics_new_present_re";
        var query = new QueryExpression("new_present_record")
        {
            ColumnSet = new ColumnSet(columns),
            PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 }
        };
        query.Criteria.AddCondition("new_meeting_statistics_new_present_re", ConditionOperator.Equal, meeting.Id);
        query.AddOrder("new_present_recordid", OrderType.Ascending);
        var identities = new HashSet<Guid>();
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        for (var pageNumber = 1; ; pageNumber++)
        {
            query.PageInfo.PageNumber = pageNumber;
            var page = service.RetrieveMultiple(query);
            if (page is null || page.Entities.Count > PageSize)
                throw Invalid("Invalid attendance page.");
            foreach (var row in page.Entities)
            {
                ValidateIdentity(row, "new_present_record");
                if (!identities.Add(row.Id)) throw Invalid("Duplicate attendance identity.");
                if (identities.Count > MaximumRows) throw Invalid("Attendance row capacity exceeded.");
                if (!row.Attributes.TryGetValue("new_meeting_statistics_new_present_re", out var scope) ||
                    scope is not EntityReference parent || parent.LogicalName != "new_meeting_statistics" || parent.Id != meeting.Id)
                    throw Invalid("Attendance meeting scope conflict.");
                // 舊規則只比較 not-null；不比較日期、不強制先簽到，也不以同名或同會員縮減列數。
                for (var i = 0; i < fieldCount; i++)
                    if (row.Attributes.TryGetValue(pairs[i, 0], out var value) && value is not null)
                        counts[i]++;
            }
            if (!page.MoreRecords) break;
            if (page.Entities.Count == 0 || pageNumber >= MaximumPages || identities.Count >= MaximumRows)
                throw Invalid("Incomplete attendance paging or capacity exceeded.");
            var cookie = page.PagingCookie;
            if (!string.IsNullOrEmpty(cookie) && (cookie.Length > MaximumCookieLength || !cookies.Add(cookie)))
                throw Invalid("Invalid attendance paging cookie.");
            // cookie 是 SDK 的不透明續頁資料；不解析、不寫日誌，缺值時用頁碼而非提早結束。
            query.PageInfo.PagingCookie = cookie;
        }
        var patch = new Entity("new_meeting_statistics", meeting.Id);
        for (var i = 0; i < fieldCount; i++) patch[pairs[i, 1]] = counts[i];
        // 唯一發布點，絕不送出 Retrieve 原物件；同步故障保留給外殼統一告警，不重試不確定寫入。
        service.Update(patch);
    }

    /// <summary>本地單頁傳輸預算，不宣稱為已驗證的組織服務上限。</summary>
    private const int PageSize = 500;
    /// <summary>最大正式列數；即使後頁還有資料也必須失敗，不能發布部分統計。</summary>
    private const int MaximumRows = 10000;
    /// <summary>對每頁很少資料或永遠宣告續頁的 SDK 設定同步呼叫上限。</summary>
    private const int MaximumPages = 100;
    /// <summary>限制不透明 cookie 的保留量；每輪最多一百個，沒有跨請求快取。</summary>
    private const int MaximumCookieLength = 32768;

    /// <summary>正式 ID 必須非空且同表；SDK 有附帶主鍵屬性時也須與 Entity.Id 一致，不補造身分。</summary>
    private static void ValidateIdentity(Entity? row, string table)
    {
        if (row is null || row.LogicalName != table || row.Id == Guid.Empty)
            throw Invalid("Missing or unexpected database identity.");
        if (row.Attributes.TryGetValue(table + "id", out var key) && (key is not Guid id || id != row.Id))
            throw Invalid("Conflicting database identity.");
    }

    /// <summary>固定診斷不包含會員姓名、cookie、來源值或憑證；由外殼保留例外及安全事件身分。</summary>
    private static InvalidOperationException Invalid(string reason) => new(reason);

    /// <summary>
    /// reading 的十八組固定與二十組動態欄位；首堂簽退確實缺少 _time，不得依對稱名稱修正。
    /// 每次回傳獨占二維陣列，第零欄為來源、第一欄為計數目標；不使用沙箱禁止的 ValueTuple。
    /// 只有固定 schema 字串，沒有姓名、Entity、授權或 SDK 參照，陣列不對外發布或跨呼叫保留。
    /// </summary>
    private static string[,] FieldPairs() => new string[,]
    {
        { "new_sunday_first_qr_on_time", "new_sunday_first_qr_on_number" },
        { "new_sunday_second_qr_on_time", "new_sunday_second_qr_on_number" },
        { "new_saturday_worship_on_time", "new_saturday_worship_on_number" },
        { "new_yongmen_on_time", "new_yongmen_on_number" },
        { "new_prayer_meeting_on_time", "new_prayer_meeting_qr_on_number" },
        { "new_child_on_time", "new_child_on_number" },
        { "new_big_disciple_on_time", "new_big_disciple_qr_on_number" },
        { "new_leadership_small_lecture_on_time", "new_leadership_small_lecture_qr_on_number" },
        { "new_leaders_gather_on_time", "new_leaders_gather_qr_on_number" },
        { "new_sunday_first_qr_off", "new_sunday_first_qr_off_number" },
        { "new_sunday_second_qr_off_time", "new_sunday_second_qr_off_number" },
        { "new_saturday_worship_off_time", "new_saturday_worship_off_number" },
        { "new_yongmen_off_time", "new_yongmen_off_number" },
        { "new_prayer_meeting_off_time", "new_prayer_meeting_qr_off_number" },
        { "new_child_off_time", "new_child_off_number" },
        { "new_big_disciple_off_time", "new_big_disciple_qr_off_number" },
        { "new_leadership_small_lecture_off_time", "new_leadership_small_lecture_qr_off_number" },
        { "new_leaders_gather_off_time", "new_leaders_gather_qr_off_number" },
        { "new_1_sign_on_time", "new_1_sign_on_number" }, { "new_1_sign_off_time", "new_1_sign_off_number" },
        { "new_2_sign_on_time", "new_2_sign_on_number" }, { "new_2_sign_off_time", "new_2_sign_off_number" },
        { "new_3_sign_on_time", "new_3_sign_on_number" }, { "new_3_sign_off_time", "new_3_sign_off_number" },
        { "new_4_sign_on_time", "new_4_sign_on_number" }, { "new_4_sign_off_time", "new_4_sign_off_number" },
        { "new_5_sign_on_time", "new_5_sign_on_number" }, { "new_5_sign_off_time", "new_5_sign_off_number" },
        { "new_6_sign_on_time", "new_6_sign_on_number" }, { "new_6_sign_off_time", "new_6_sign_off_number" },
        { "new_7_sign_on_time", "new_7_sign_on_number" }, { "new_7_sign_off_time", "new_7_sign_off_number" },
        { "new_8_sign_on_time", "new_8_sign_on_number" }, { "new_8_sign_off_time", "new_8_sign_off_number" },
        { "new_9_sign_on_time", "new_9_sign_on_number" }, { "new_9_sign_off_time", "new_9_sign_off_number" },
        { "new_10_sign_on_time", "new_10_sign_on_number" }, { "new_10_sign_off_time", "new_10_sign_off_number" }
    };
}
