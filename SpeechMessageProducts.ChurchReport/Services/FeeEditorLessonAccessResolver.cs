// ============================================================================
// 檔案：ChurchReport/Services/FeeEditorLessonAccessResolver.cs
// 目的：將目前登入者已載入的課程清單轉為費用編輯器唯讀端點可使用的短生命週期授權快照。
//
// 安全與生命週期契約：
// - 此 helper 不接受瀏覽器 locator、profile、endpoint、credential、owner 或 CRM Entity；上述資料不能成為授權來源。
// - 呼叫端必須先以 FeeList.IsLessonListLoadedFor 驗證目前 session login；false 時本 helper 不讀取或修復舊資料。
// - 成功結果會複製為新的不可寫清單，沒有 static cache、Session、CRM、連線、timer、task 或其他需釋放資源。
// - null、無效或重複 lesson ID 全部 fail closed，絕不掃描 CRM、猜選課程或在本 helper 建立 I/O。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ChurchReport.Models;

namespace ChurchReport.Services;

/// <summary>
/// 將已由伺服器登入流程建立的課程清單轉為 request-local 授權 allowlist。
///
/// browser 傳入的 GUID 只能在 controller 先確認目前 login 的 lesson list 已載入後，才可使用
/// <see cref="IsAuthorizedTarget"/> 比對。此類別不接觸 FeeList、Session、ToolUtility 或 CRM，故不能
/// 藉由查詢、快取或 shared state 把其他登入者的課程留給下一個 request；回傳集合的唯一 owner
/// 是目前 controller action，回應結束後可自然回收。
/// </summary>
public static class FeeEditorLessonAccessResolver
{
    /// <summary>
    /// 從已驗證為目前登入者的 server lesson snapshot 建立新的不可寫 GUID 清單。
    ///
    /// 若 snapshot 尚未載入、為 null、包含 null lesson、空白／無效 ID 或同一 GUID 的重複項目，
    /// 一律回傳 <see langword="false"/> 與空集合。這些狀態不能透過 browser input、legacy I/O 或
    /// CRM 補查修正，避免把舊 session cache 誤當目前使用者權限。空白但已驗證的課程清單是安全
    /// 的「沒有可存取課程」snapshot，會成功回傳空 allowlist，後續 target 比對仍固定拒絕。
    /// </summary>
    /// <param name="lessonListIsLoadedForCurrentLogin">由 controller 以目前 session login 執行的純 scope 驗證結果。</param>
    /// <param name="serverLessons">既有流程建立的 server lesson snapshot；本方法只讀取其 ID 並立刻複製。</param>
    /// <param name="authorizedLessonIds">成功時為新的、不可寫且排序固定的 target allowlist；失敗時為空集合。</param>
    /// <returns>snapshot 可以安全作為目前 request 的 target authorization 資料時為 <see langword="true"/>。</returns>
    public static bool TryCreateAuthorizedLessonIds(
        bool lessonListIsLoadedForCurrentLogin,
        IEnumerable<Lesson>? serverLessons,
        out IReadOnlyList<Guid> authorizedLessonIds)
    {
        authorizedLessonIds = Array.Empty<Guid>();

        if (!lessonListIsLoadedForCurrentLogin || serverLessons is null)
        {
            return false;
        }

        var ids = new List<Guid>();
        var uniqueIds = new HashSet<Guid>();
        foreach (var lesson in serverLessons)
        {
            if (lesson is null ||
                string.IsNullOrWhiteSpace(lesson.DiscipleLessonsId) ||
                !Guid.TryParse(lesson.DiscipleLessonsId, out var lessonId) ||
                !uniqueIds.Add(lessonId))
            {
                return false;
            }

            ids.Add(lessonId);
        }

        ids.Sort();
        authorizedLessonIds = new ReadOnlyCollection<Guid>(ids);
        return true;
    }

    /// <summary>
    /// 判斷 browser 已解析的 locator 是否存在於本 request 的 server-derived allowlist。
    /// 此方法不解析字串、不保留集合也不選取預設目標；空、null 或 snapshot 外 GUID 都固定拒絕，
    /// 使合法 GUID 本身無法跨課程或跨登入取得存取權。
    /// </summary>
    /// <param name="authorizedLessonIds">由 <see cref="TryCreateAuthorizedLessonIds"/> 於目前 request 建立的不可寫清單。</param>
    /// <param name="targetLessonId">controller 在授權 snapshot 建立後才解析出的 browser locator。</param>
    /// <returns>target 精確屬於目前 server snapshot 時為 <see langword="true"/>。</returns>
    public static bool IsAuthorizedTarget(
        IReadOnlyList<Guid>? authorizedLessonIds,
        Guid targetLessonId)
    {
        if (authorizedLessonIds is null || targetLessonId == Guid.Empty)
        {
            return false;
        }

        foreach (var lessonId in authorizedLessonIds)
        {
            if (lessonId == targetLessonId)
            {
                return true;
            }
        }

        return false;
    }
}
