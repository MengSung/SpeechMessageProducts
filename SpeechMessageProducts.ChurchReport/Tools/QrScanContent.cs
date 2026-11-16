/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 四種掃碼工具的純內容解析邊界，將原地切字串改為委派
 * GoodShepherd.Core 的有界 Codec；掃描者查詢與簽到副作用仍由原工具在原順序負責。
 *
 * 主要功能與流程：四個 internal 方法對應課程、主日、個人及小組，輸入是 QR 內容，
 * 輸出為資料庫 GUID 與原欄位。Codec 拒絕時統一丟固定英文 FormatException，
 * 不回顯原內容；呼叫位置留在原掃描者查詢之後，小組原有加好友亦先執行。
 *
 * 使用邊界與維護：純同步解析不是授權，不讀寫 D365、不推播、不持有 Session、request、
 * token、快取或可變共用狀態，沒有連線、背景工作或 Dispose owner。測試以合成黃金對照、
 * 六類刻意拒絕與語法樹守門驗證；離線測試不代表已通過頁面或 D365 實機驗收。
 */
using System;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;

namespace ChurchReport.Tools;

/// <summary>
/// 四種掃碼工具的無狀態語法邊界；只委派最多 2048 字元的共用解碼，不執行資料查詢或授權。
/// 輸出只由當次工具消費，無跨 request／使用者／教會的保留；失敗交既有控制器錯誤處理。
/// </summary>
internal static class QrScanContent
{
    private const string InvalidContentMessage = "QR content is not in a supported format.";

    /// <summary>
    /// 解析課程 ID、課次與簽到代碼，保留大小寫敏感 enroll 的空簽到欄位；
    /// 無效內容一律固定 FormatException，原工具負責在查完掃描者後才呼叫及寫回欄位。
    /// </summary>
    internal static void ReadLesson(string content, out Guid lessonId, out string classIndex, out string onboardType)
    {
        if (!QrCodeContentCodec.TryDecodeLesson(content, out lessonId, out classIndex, out onboardType))
            throw new FormatException(InvalidContentMessage);
    }

    /// <summary>
    /// 解析主日聚會 ID、屬性、簽到代碼及 at 優先分隔符；at 格式屬性內的底線完整保留。
    /// 不查聚會，失敗無部分業務發布；分隔符只控制既有簽到分支，不代表授權。
    /// </summary>
    internal static void ReadMeeting(string content, out Guid meetingId, out string attribute, out string onboardType, out bool usesAtSeparator)
    {
        if (!QrCodeContentCodec.TryDecodeMeeting(content, out meetingId, out attribute, out onboardType, out var separator))
            throw new FormatException(InvalidContentMessage);
        usesAtSeparator = separator == QrCodePayloadSeparator.AtSign;
    }

    /// <summary>
    /// 個人內容只取聯絡人資料庫 ID，尾欄維持舊規則而不當成身分或授權；
    /// 無效 GUID、全零或超限均固定拒絕，呼叫端仍須執行原資料讀取及業務檢查。
    /// </summary>
    internal static Guid ReadPersonal(string content)
    {
        if (!QrCodeContentCodec.TryDecodePersonal(content, out var contactId))
            throw new FormatException(InvalidContentMessage);
        return contactId;
    }

    /// <summary>
    /// 解析小組週報 ID 與簽到代碼；原工具查掃描者及必要時加好友後才呼叫，
    /// 本方法沒有 D365／LINE 相依，拒絕時不回顯內容也不執行週報或出席寫入。
    /// </summary>
    internal static void ReadSmallGroup(string content, out Guid weeklyReportId, out string onboardType)
    {
        if (!QrCodeContentCodec.TryDecodeSmallGroup(content, out weeklyReportId, out onboardType))
            throw new FormatException(InvalidContentMessage);
    }
}
