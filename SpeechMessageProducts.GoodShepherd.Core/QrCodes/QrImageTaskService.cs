/*
 * 檔案整體說明
 * 目的與定位：GoodShepherd P5-09 QR 圖片待辦的 SDK 協調邊界，供 meeting／personal
 * 活動借用當次授權 SDK，完整驗證來源與唯一教會後才交 QrImageTaskRule 入列。
 * 主要功能與流程：個人只讀 contact.parentcustomerid(account)；會議先讀
 * new_meeting_statistics 確認存在，再依 new_meeting_statistics_new_present_re
 * 讀 new_present_record，以 new_contact_new_present_record 正式 lookup 讀 contact，
 * 由各 contact.parentcustomerid 確認唯一教會。meeting 沒有 church lookup，不猜欄位。
 * 核定差異：零出席、缺正式 lookup、多教會、重複出席 ID、缺續頁 cookie 均拒絕，
 * 不猜預設教會、不靜默合併正式記錄，也不把尚未完整驗證的前頁發布為待辦。
 * 使用邊界與維護：每輪最多一百頁、一萬列，每頁五百列；cookie 至多 32768 字元，
 * 正式 ID 集合及已驗證 contact 歸屬只存在本次呼叫，最多各一萬筆，無 static 可變資料。
 * 同會員的不同出席列仍逐列驗證，局部歸屬表僅避免重複 Retrieve，不按姓名去重。
 * 最壞來源驗證為一筆會議、百頁與一萬筆不同 contact 的同步讀取；此為呼叫數上限，
 * 不是延遲保證，SDK 逾時或任一 contact 讀取失敗都在入列前拒絕，不截斷歸屬驗證。
 * SDK 權限是實際授權邊界，不讀 token、不提升權限、不保留 Session／SDK／個資，
 * 不建立背景工作或連線。using store 清借用參照，不 Dispose 平台 SDK；同步 I/O
 * 排空後才返回，逾時由外殼配置並告警，故障保留原例外，不自動重試不確定寫入。
 * 分頁不是資料庫快照，不宣稱阻止讀取期間來源被其他交易修改；不跨呼叫快取授權。
 * 雙宿主離線測試驗入列、範圍、容量、故障及 owner 排空，不冒充實機驗收。
 */
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Core.QrCodes;

/// <summary>依當次 SDK 的權威來源關聯解析教會，全部驗證完成後才建立一筆完整圖片組待辦。</summary>
public static class QrImageTaskService
{
    /// <summary>來源查詢的單頁本地預算；不使用全欄讀取、不增加狀態或姓名過濾條件。</summary>
    private const int PageSize = 500;
    /// <summary>同步完整查詢最多一百頁，缺 cookie 不退回可能不穩定的簡單分頁。</summary>
    private const int MaximumPages = 100;
    /// <summary>正式列身分及局部會員歸屬最多各一萬筆，超量拒絕而非截斷入列。</summary>
    private const int MaximumRows = 10000;
    /// <summary>不透明 SDK cookie 的單值長度上限；最多保留一百個且不記錄內容。</summary>
    private const int MaximumCookieLength = 32768;

    /// <summary>
    /// 驗證正式會議及全部出席者的唯一教會後，入列一組三十八張圖。
    /// 無出席或歸屬不明為明確拒絕差異，不能以執行帳號或預設教會補洞。
    /// 本同步介面沒有取消 token；外殼須在入場前判斷取消並配置 SDK 逾時，
    /// 不可用背景 Task 或提早返回偽造已取消／已排空；飛行中的呼叫仍須等待 SDK 返回。
    /// </summary>
    /// <param name="sdk">平台當次授權的同步借用服務；外殼負責逾時、告警與最終 Dispose。</param>
    /// <param name="meetingId">平台主要 new_meeting_statistics 正式 ID。</param>
    /// <returns>一筆真正儲存的待辦 ID；不代表圖片已產生或已發布。</returns>
    public static IReadOnlyList<Guid> EnqueueMeeting(IOrganizationService sdk, Guid meetingId)
    {
        ValidateArguments(sdk, meetingId, nameof(meetingId));
        Read(sdk, "new_meeting_statistics", meetingId, new ColumnSet(false));
        var churchId = MeetingChurch(sdk, meetingId);
        using var store = new D365OutboundEnqueueStore(sdk);
        return QrImageTaskRule.Enqueue(store, churchId, meetingId, personal: false);
    }

    /// <summary>
    /// 只從權威 contact.parentcustomerid(account) 解析歸屬，不讀姓名、token 或其他聯絡人欄位。
    /// 此同步入口不提供取消 token，外殼負責入場前取消及 SDK 逾時；已入場 I/O 必須排空。
    /// </summary>
    /// <param name="sdk">平台當次授權服務；只借用，不建立或保留 client。</param>
    /// <param name="contactId">平台主要 contact 正式 ID，不接受姓名或 caller 提供的 church。</param>
    /// <returns>一筆真正儲存的一張個人圖片待辦 ID；不是 contact ID。</returns>
    public static IReadOnlyList<Guid> EnqueuePersonal(IOrganizationService sdk, Guid contactId)
    {
        ValidateArguments(sdk, contactId, nameof(contactId));
        var churchId = ContactChurch(sdk, contactId);
        using var store = new D365OutboundEnqueueStore(sdk);
        return QrImageTaskRule.Enqueue(store, churchId, contactId, personal: true);
    }

    /// <summary>
    /// 逐頁驗證正式列及兩段 lookup，完整完成後才回唯一教會；局部歸屬表不跨使用者重用，
    /// 同一會員多筆合法出席不會被視為重複，但同一出席 ID 即使跨頁出現也拒絕。
    /// </summary>
    private static Guid MeetingChurch(IOrganizationService sdk, Guid meetingId)
    {
        var query = new QueryExpression("new_present_record")
        {
            ColumnSet = new ColumnSet("new_present_recordid", "new_meeting_statistics_new_present_re", "new_contact_new_present_record"),
            PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 }
        };
        query.Criteria.AddCondition("new_meeting_statistics_new_present_re", ConditionOperator.Equal, meetingId);
        query.AddOrder("new_present_recordid", OrderType.Ascending);
        var rowIds = new HashSet<Guid>();
        var contacts = new Dictionary<Guid, Guid>();
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        var churchId = Guid.Empty;
        for (var pageNumber = 1; ; pageNumber++)
        {
            query.PageInfo.PageNumber = pageNumber;
            var page = sdk.RetrieveMultiple(query);
            if (page is null || page.Entities.Count == 0 || page.Entities.Count > PageSize)
                throw Invalid("Invalid or empty attendance page.");
            foreach (var row in page.Entities)
            {
                ValidateIdentity(row, "new_present_record");
                if (!rowIds.Add(row.Id)) throw Invalid("Duplicate attendance identity.");
                if (rowIds.Count > MaximumRows) throw Invalid("Attendance row capacity exceeded.");
                if (Lookup(row, "new_meeting_statistics_new_present_re", "new_meeting_statistics") != meetingId)
                    throw Invalid("Attendance meeting scope conflict.");
                var contactId = Lookup(row, "new_contact_new_present_record", "contact");
                if (!contacts.TryGetValue(contactId, out var candidate))
                {
                    candidate = ContactChurch(sdk, contactId);
                    contacts.Add(contactId, candidate);
                }
                if (churchId != Guid.Empty && churchId != candidate)
                    throw Invalid("Attendance church scope conflict.");
                churchId = candidate;
            }
            if (!page.MoreRecords) return churchId;
            if (pageNumber >= MaximumPages || rowIds.Count >= MaximumRows)
                throw Invalid("Incomplete attendance paging or capacity exceeded.");
            var cookie = page.PagingCookie;
            if (string.IsNullOrEmpty(cookie) || cookie.Length > MaximumCookieLength || !cookies.Add(cookie))
                throw Invalid("Missing or invalid attendance paging cookie.");
            query.PageInfo.PagingCookie = cookie;
        }
    }

    /// <summary>讀取已授權會友的最小歸屬欄位；parentcustomerid 指向 contact 而非 account 時拒絕。</summary>
    private static Guid ContactChurch(IOrganizationService sdk, Guid contactId) =>
        Lookup(Read(sdk, "contact", contactId, new ColumnSet("parentcustomerid")), "parentcustomerid", "account");

    /// <summary>空正式 ID 或空服務在任何 SDK 呼叫前拒絕；不將語法檢查冒充伺服器授權。</summary>
    private static void ValidateArguments(IOrganizationService sdk, Guid id, string parameterName)
    {
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#else
        ArgumentNullException.ThrowIfNull(sdk);
#endif
        if (id == Guid.Empty) throw new ArgumentException("A database QR source identity is required.", parameterName);
    }

    /// <summary>每次 Retrieve 確認正式 ID、精確表名及可選主鍵屬性一致，不返回錯來源的可變列。</summary>
    private static Entity Read(IOrganizationService sdk, string table, Guid id, ColumnSet columns)
    {
        var row = sdk.Retrieve(table, id, columns);
        ValidateIdentity(row, table);
        return row.Id == id ? row : throw Invalid("Unexpected QR source identity.");
    }

    /// <summary>來源實體須有正式非空 ID，不能用替代鍵或互相矛盾的主鍵屬性補造記錄。</summary>
    private static void ValidateIdentity(Entity? row, string table)
    {
        if (row is null || row.Id == Guid.Empty || !string.Equals(row.LogicalName, table, StringComparison.Ordinal)
            || row.KeyAttributes.Count != 0)
            throw Invalid("Invalid QR source database identity.");
        if (row.Attributes.TryGetValue(table + "id", out var key) && (key is not Guid id || id != row.Id))
            throw Invalid("Conflicting QR source database identity.");
    }

    /// <summary>只接受正確表名的非空正式 lookup；Name 或替代鍵不是正式資料庫身分。</summary>
    private static Guid Lookup(Entity row, string field, string table) =>
        row.Attributes.TryGetValue(field, out var value) && value is EntityReference reference
        && reference.Id != Guid.Empty && string.Equals(reference.LogicalName, table, StringComparison.Ordinal)
        && reference.KeyAttributes.Count == 0 ? reference.Id : throw Invalid("Missing or invalid QR source lookup.");

    /// <summary>只輸出固定診斷，不將來源 ID、姓名、cookie、SDK 本文或憑證帶入錯誤訊息。</summary>
    private static InvalidOperationException Invalid(string reason) => new(reason);
}
