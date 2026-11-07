/*
 * 檔案整體說明
 *
 * 目的與定位：集中 GoodShepherd 課程、主日、個人及小組的 QR 內容字串契約，
 * 讓 net462 與 net10.0 呼叫端共享資料列 GUID、欄位順序及分隔符解析規則。
 *
 * 主要功能與流程：目前先提供可編譯的 API 空殼，讓黃金測試以
 * NotImplementedException 真正執行紅燈，而不是把缺命名空間的編譯失敗當成行為證據。
 * 尚未實作內容轉換、圖片產生或任何掃描流程；產品呼叫端不可接用這個空殼。
 *
 * 使用邊界與維護：本檔不做資料查詢、身分驗證、租戶授權、LINE 推播或圖片操作，
 * 不持有 Session、request、credential、可變靜態狀態、快取或需 Dispose 的資源。
 * 完成實作時須同步更新此導讀，並以兩目標黃金、拒絕及並行測試證明契約，
 * 不得把純 GUID 語法檢查宣稱為伺服器授權或 D365／圖片實機驗收。
 */
using System;

namespace SpeechMessageProducts.GoodShepherd.Core.QrCodes;

/// <summary>選定主日掃描器的既有分支；分隔符不代表教會、使用者或授權範圍。</summary>
public enum QrCodePayloadSeparator
{
    /// <summary>以底線分隔動態聚會代碼；各欄不能含底線或 at 字元。</summary>
    Underscore,

    /// <summary>以 at 字元分隔固定屬性名稱；屬性欄位可以包含底線。</summary>
    AtSign,
}

/// <summary>
/// 提供不存狀態的 QR 內容轉換邊界；目前方法尚未實作，只用於建立明確的行為紅燈。
/// </summary>
public static class QrCodeContentCodec
{
    /// <summary>編碼課程資料列、課次與簽到代碼；enroll 索引不需要簽到欄位。</summary>
    public static string EncodeLesson(Guid lessonId, string classIndex, string onboardType) =>
        throw new NotImplementedException();

    /// <summary>解析課程必需欄位；失敗時不得發布部分資料，解碼不是授權。</summary>
    public static bool TryDecodeLesson(string? content, out Guid lessonId, out string classIndex, out string onboardType) =>
        throw new NotImplementedException();

    /// <summary>依選定分隔符編碼聚會統計資料列、屬性代碼及簽到代碼。</summary>
    public static string EncodeMeeting(Guid meetingId, string attribute, string onboardType, QrCodePayloadSeparator separator) =>
        throw new NotImplementedException();

    /// <summary>解析主日欄位及實際分隔符，讓後續呼叫端保留固定／動態聚會的分支。</summary>
    public static bool TryDecodeMeeting(string? content, out Guid meetingId, out string attribute, out string onboardType, out QrCodePayloadSeparator separator) =>
        throw new NotImplementedException();

    /// <summary>以非空聯絡人資料庫 GUID 編碼個人內容，不接受顯示名稱作為身分。</summary>
    public static string EncodePersonal(Guid contactId) =>
        throw new NotImplementedException();

    /// <summary>依舊個人掃描器的 at 優先規則取聯絡人 GUID，不執行任何查詢或授權。</summary>
    public static bool TryDecodePersonal(string? content, out Guid contactId) =>
        throw new NotImplementedException();

    /// <summary>編碼小組週報資料列 GUID 與簽到代碼；本方法不寫入週報。</summary>
    public static string EncodeSmallGroup(Guid weeklyReportId, string onboardType) =>
        throw new NotImplementedException();

    /// <summary>解析小組週報 GUID 與簽到代碼；呼叫端仍須驗證其資料存取範圍。</summary>
    public static bool TryDecodeSmallGroup(string? content, out Guid weeklyReportId, out string onboardType) =>
        throw new NotImplementedException();
}
