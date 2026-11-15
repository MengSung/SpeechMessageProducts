/*
 * 檔案整體說明
 * 目的與定位：P5-07課程QR待辦的SDK協調邊界，供Plugin及原QR活動共用；
 * 由正式課程的主要聯絡人new_contact_new_disciple_lessons解析教會，禁止按名稱猜測。
 * 主要流程：最小欄位讀來源課程→主要聯絡人→parentcustomerid正式account，確認每列
 * 表名、ID及lookup型別後才呼叫LessonLinkRule。缺聯絡人／教會或錯誤型別在待辦
 * 建立前拒絕，不回落其他教會、組織名稱或開發者帳號；SDK錯誤保留原例外。
 * 使用邊界與維護：metadata已確認兩個lookup與目標，讀取受平台當次UserId權限；
 * 本服務不提升權限或保存SDK。每次只有來源兩列及有界命令，沒有cache／Session、
 * static可變狀態或背景工作。store由本次using擁有並清除借用參照，不Dispose平台SDK，
 * 同步I/O完整排空後返回。Shadow由呼叫端傳記錄器，不讀token、不畫圖或連外。
 * 離線測試驗正常、缺歸屬、重送及影子完整性；不聲稱已實機執行或圖片已發布。
 */
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>來源歸屬核對完成後才借用SDK建立QR待辦，不保存任何執行資料。</summary>
public static class LessonLinkService
{
    /// <summary>以課程正式關聯驗證教會，維持舊Plugin及活動的問卷差異。</summary>
    /// <param name="sdk">平台當次權限的借用服務，不由本服務Dispose。</param>
    /// <param name="lessonId">平台主要課程資料庫ID。</param>
    /// <param name="includePoll">原QR活動包含問卷，原Plugin不包含。</param>
    /// <returns>store真正建立或讀回的正式待辦ID。</returns>
    public static IReadOnlyList<Guid> Enqueue(IOrganizationService sdk, Guid lessonId, bool includePoll)
    {
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#else
        ArgumentNullException.ThrowIfNull(sdk);
#endif
        var lesson = Read(sdk, "new_disciple_lessons", lessonId, "new_contact_new_disciple_lessons");
        var contactId = Lookup(lesson, "new_contact_new_disciple_lessons", "contact");
        var contact = Read(sdk, "contact", contactId, "parentcustomerid");
        var churchId = Lookup(contact, "parentcustomerid", "account");
        using var store = new D365OutboundEnqueueStore(sdk);
        return LessonLinkRule.Enqueue(store, churchId, lessonId, 1, includePoll);
    }

    /// <summary>兩次精確最小欄位讀取，任何回傳身分錯配不得繼續寫入。</summary>
    private static Entity Read(IOrganizationService sdk, string table, Guid id, string column)
    {
        if (id == Guid.Empty) throw Invalid();
        var row = sdk.Retrieve(table, id, new ColumnSet(column));
        return row is not null && row.Id == id && row.LogicalName == table ? row : throw Invalid();
    }
    /// <summary>只信正式lookup的非空資料庫ID，顯示Name及替代鍵均不能代替。</summary>
    private static Guid Lookup(Entity row, string field, string table) =>
        row.Attributes.TryGetValue(field, out var value) && value is EntityReference reference &&
        reference.Id != Guid.Empty && reference.LogicalName == table && reference.KeyAttributes.Count == 0
            ? reference.Id : throw Invalid();
    /// <summary>固定拒絕不回顯聯絡人、課程或SDK查詢本文。</summary>
    private static InvalidOperationException Invalid() => new("Invalid lesson QR source scope.");
}
