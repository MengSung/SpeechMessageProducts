/*
 * 檔案整體說明
 *
 * 目的與定位：集中 GoodShepherd 課程、主日、個人及小組的 QR 內容字串契約，
 * 讓 net462 與 net10.0 呼叫端共享資料列 GUID、欄位順序及分隔符解析規則。
 *
 * 主要功能與流程：純字串編碼依既有掃描器的 GUID、欄位順序及分隔符輸出，
 * 解碼先限制輸入為最多 2048 個 UTF-16 字元，再解析必需欄位，全部成功才發布輸出。
 * 保留主日／個人的 at 優先與課程 enroll 大小寫敏感特例；未消費尾欄維持舊解析語意。
 * 這個上限是有界解析預算，不是 QR 圖片容量保證；不產生圖片，也尚未接入掃描流程。
 *
 * 使用邊界與維護：本檔不做資料查詢、身分驗證、租戶授權、LINE 推播或圖片操作，
 * 不持有 Session、request、credential、可變靜態狀態、快取或需 Dispose 的資源。
 * 所有暫存陣列只屬於本次同步呼叫，返回後不再持有；沒有需 drain 或 Dispose 的 owner。
 * 以兩目標黃金及拒絕測試驗證契約，編碼參數錯誤只回固定錯誤，不夾帶輸入內容；
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
/// 提供不存狀態且有界的 QR 內容轉換；只驗證語法，不查詢或授權資料列。
/// 呼叫端仍須以伺服器端使用者及教會範圍驗證解出的資料庫 GUID。
/// </summary>
public static class QrCodeContentCodec
{
    private const int MaximumContentLength = 2048;

    /// <summary>
    /// 編碼課程資料列、課次與簽到代碼；含序位小寫 enroll 的索引維持雙欄格式，
    /// 其他課次必須有非空簽到欄位。拒絕空 GUID、分隔符注入與超出解析預算的結果。
    /// </summary>
    public static string EncodeLesson(Guid lessonId, string classIndex, string onboardType)
    {
        ValidateIdentity(lessonId);
        ValidateField(classIndex, allowUnderscore: false);
        var prefix = lessonId.ToString("D") + "_" + classIndex;
        if (IsEnrollment(classIndex)) return ValidateLength(prefix);
        ValidateField(onboardType, allowUnderscore: false);
        return ValidateLength(prefix + "_" + onboardType);
    }

    /// <summary>解析課程必需欄位；失敗時不得發布部分資料，解碼不是授權。</summary>
    public static bool TryDecodeLesson(string? content, out Guid lessonId, out string classIndex, out string onboardType)
    {
        lessonId = Guid.Empty;
        classIndex = string.Empty;
        onboardType = string.Empty;
        if (!TrySplit(content, '_', 2, out var fields) ||
            !TryReadIdentity(fields[0], out var candidate) ||
            !IsValidField(fields[1], allowUnderscore: false)) return false;
        var enrollment = IsEnrollment(fields[1]);
        if (!enrollment && (fields.Length < 3 || !IsValidField(fields[2], allowUnderscore: false))) return false;
        lessonId = candidate;
        classIndex = fields[1];
        onboardType = enrollment ? string.Empty : fields[2];
        return true;
    }

    /// <summary>依選定分隔符編碼聚會統計資料列、屬性代碼及簽到代碼。</summary>
    public static string EncodeMeeting(Guid meetingId, string attribute, string onboardType, QrCodePayloadSeparator separator)
    {
        ValidateIdentity(meetingId);
        if (separator != QrCodePayloadSeparator.Underscore && separator != QrCodePayloadSeparator.AtSign)
            throw new ArgumentException("Unsupported QR separator.", nameof(separator));
        var atSign = separator == QrCodePayloadSeparator.AtSign;
        ValidateField(attribute, allowUnderscore: atSign);
        ValidateField(onboardType, allowUnderscore: atSign);
        var delimiter = atSign ? "@" : "_";
        return ValidateLength(meetingId.ToString("D") + delimiter + attribute + delimiter + onboardType);
    }

    /// <summary>解析主日欄位及實際分隔符，讓後續呼叫端保留固定／動態聚會的分支。</summary>
    public static bool TryDecodeMeeting(string? content, out Guid meetingId, out string attribute, out string onboardType, out QrCodePayloadSeparator separator)
    {
        meetingId = Guid.Empty;
        attribute = string.Empty;
        onboardType = string.Empty;
        separator = QrCodePayloadSeparator.Underscore;
        if (!IsWithinBudget(content)) return false;
        // 舊掃描器只要任意處含 at 就選固定屬性分支，不能先按底線取到不同的 GUID。
        var atSign = ContainsOrdinal(content!, "@");
        if (!TrySplit(content, atSign ? '@' : '_', 3, out var fields) ||
            !TryReadIdentity(fields[0], out var candidate) ||
            !IsValidField(fields[1], allowUnderscore: atSign) ||
            !IsValidField(fields[2], allowUnderscore: atSign)) return false;
        meetingId = candidate;
        attribute = fields[1];
        onboardType = fields[2];
        separator = atSign ? QrCodePayloadSeparator.AtSign : QrCodePayloadSeparator.Underscore;
        return true;
    }

    /// <summary>以非空聯絡人資料庫 GUID 編碼個人內容，不接受顯示名稱作為身分。</summary>
    public static string EncodePersonal(Guid contactId)
    {
        ValidateIdentity(contactId);
        return contactId.ToString("D");
    }

    /// <summary>依舊個人掃描器的 at 優先規則取聯絡人 GUID，不執行任何查詢或授權。</summary>
    public static bool TryDecodePersonal(string? content, out Guid contactId)
    {
        contactId = Guid.Empty;
        if (!IsWithinBudget(content)) return false;
        var separator = ContainsOrdinal(content!, "@") ? "@" : "_";
        var boundary = content!.IndexOf(separator, StringComparison.Ordinal);
        var identity = boundary < 0 ? content : content.Substring(0, boundary);
        if (!TryReadIdentity(identity, out var candidate)) return false;
        contactId = candidate;
        return true;
    }

    /// <summary>編碼小組週報資料列 GUID 與簽到代碼；本方法不寫入週報。</summary>
    public static string EncodeSmallGroup(Guid weeklyReportId, string onboardType)
    {
        ValidateIdentity(weeklyReportId);
        ValidateField(onboardType, allowUnderscore: false);
        return ValidateLength(weeklyReportId.ToString("D") + "_" + onboardType);
    }

    /// <summary>解析小組週報 GUID 與簽到代碼；呼叫端仍須驗證其資料存取範圍。</summary>
    public static bool TryDecodeSmallGroup(string? content, out Guid weeklyReportId, out string onboardType)
    {
        weeklyReportId = Guid.Empty;
        onboardType = string.Empty;
        if (!TrySplit(content, '_', 2, out var fields) ||
            !TryReadIdentity(fields[0], out var candidate) ||
            !IsValidField(fields[1], allowUnderscore: false)) return false;
        weeklyReportId = candidate;
        onboardType = fields[1];
        return true;
    }

    /// <summary>以舊掃描器的序位、大小寫敏感比對決定雙欄 enrollment 分支，不更動索引。</summary>
    private static bool IsEnrollment(string index) => ContainsOrdinal(index, "enroll");

    /// <summary>兩目標都明定序位包含語意；Framework 無 Contains 比較多載，使用等價 IndexOf。</summary>
    private static bool ContainsOrdinal(string value, string fragment)
    {
#if NETFRAMEWORK
        return value.IndexOf(fragment, StringComparison.Ordinal) >= 0;
#else
        return value.Contains(fragment, StringComparison.Ordinal);
#endif
    }

    /// <summary>在任何切割配置前檢查固定預算；null、空白與超限輸入直接拒絕。</summary>
    private static bool IsWithinBudget(string? content) =>
        !string.IsNullOrWhiteSpace(content) && content!.Length <= MaximumContentLength;

    /// <summary>最多切割 2048 字元；陣列只屬於呼叫堆疊，未滿必需欄數不交給後續解析。</summary>
    private static bool TrySplit(string? content, char separator, int minimumFields, out string[] fields)
    {
        fields = Array.Empty<string>();
        if (!IsWithinBudget(content)) return false;
        var candidate = content!.Split(separator);
        if (candidate.Length < minimumFields) return false;
        fields = candidate;
        return true;
    }

    /// <summary>解析非空資料庫識別碼；失敗不把 Guid.TryParse 的中間值發布給呼叫端。</summary>
    private static bool TryReadIdentity(string value, out Guid identity)
    {
        identity = Guid.Empty;
        if (!Guid.TryParse(value, out var candidate) || candidate == Guid.Empty) return false;
        identity = candidate;
        return true;
    }

    /// <summary>欄位必須非空；at 一律保留給分隔符，底線只在 at 格式的欄位中保留。</summary>
    private static bool IsValidField(string? value, bool allowUnderscore) =>
        !string.IsNullOrWhiteSpace(value) && !ContainsOrdinal(value!, "@") &&
        (allowUnderscore || !ContainsOrdinal(value!, "_"));

    /// <summary>編碼拒絕空穩定識別碼；固定訊息不含原始 QR、個資或其他輸入。</summary>
    private static void ValidateIdentity(Guid identity)
    {
        if (identity == Guid.Empty) throw new ArgumentException("QR identity is required.", nameof(identity));
    }

    /// <summary>編碼參數錯誤在組合輸出前失敗，訊息不回顯無效欄位。</summary>
    private static void ValidateField(string value, bool allowUnderscore)
    {
        if (!IsValidField(value, allowUnderscore)) throw new ArgumentException("Invalid QR field.", nameof(value));
    }

    /// <summary>保證本編碼器輸出可被同一解析預算接受；不代表可渲染為任意 QR 版本。</summary>
    private static string ValidateLength(string content)
    {
        if (content.Length > MaximumContentLength) throw new ArgumentException("QR content exceeds the parsing budget.");
        return content;
    }
}
