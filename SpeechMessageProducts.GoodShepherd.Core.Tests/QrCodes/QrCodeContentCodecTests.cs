/*
 * 檔案整體說明
 *
 * 目的與定位：保護 GoodShepherd.Core 雙目標 QR 內容編解碼契約，確認共用核心只
 * 接受現有 ChurchReport 掃描流程實際消費的穩定資料 ID 與欄位格式，不把資料列名稱、
 * 使用者輸入或暫存索引當成資料身分。
 *
 * 主要功能與流程：以合成 Guid 與固定字串比對課程、主日、個人及小組 payload 的
 * 黃金格式；主日分別驗證 `@` 與 `_` 兩種既有分支，課程涵蓋 `enroll` 特例，並以
 * 無效／含歧義輸入確認解碼 fail closed、輸出欄位清空且不攜回原始內容。這些測試
 * 只驗證純字串契約，不會產生 QR 圖片、連線 D365／LINE、讀取秘密或模擬實機收訊。
 *
 * 使用邊界與維護：所有資料均為本測試方法區域內的合成值，沒有共享可變狀態、
 * Session、網路、背景工作或需釋放資源。每個測試以明確字串／欄位斷言決定成敗；
 * 若調整格式，必須先提供現有產生端或消費端的來源證據，更新黃金案例後再改核心。
 * 本檔採 net48／net10.0 測試宿主，分別檢查產品實際載入 net462／net10.0 輸出。
 */
using System;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.QrCodes;

/// <summary>
/// 以固定黃金字串保護四種現行掃描器的資料列識別碼、必要欄位、分隔符優先序及拒絕行為。
/// </summary>
public sealed class QrCodeContentCodecTests
{
    private static readonly Guid LessonId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid MeetingId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ContactId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly Guid WeeklyReportId = Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210");

    /// <summary>
    /// 課程一般 payload 必須以課程資料列 GUID 開頭，並逐字保留課次索引與簽到代碼。
    /// </summary>
    [Fact(DisplayName = "課程 payload 使用資料列 GUID 與固定三欄格式")]
    public void Lesson_EncodeAndDecode_PreservesLegacyFields()
    {
        const string expected = "11111111-2222-3333-4444-555555555555_lesson-04_On";

        var encoded = QrCodeContentCodec.EncodeLesson(LessonId, "lesson-04", "On");

        Assert.Equal(expected, encoded);
        Assert.True(QrCodeContentCodec.TryDecodeLesson(
            encoded,
            out var lessonId,
            out var classIndex,
            out var onboardType));
        Assert.Equal(LessonId, lessonId);
        Assert.Equal("lesson-04", classIndex);
        Assert.Equal("On", onboardType);
    }

    /// <summary>
    /// 舊課程掃描器遇到含有大小寫相符 enroll 的索引時不讀第三欄，編碼器必須保留該特例。
    /// </summary>
    [Fact(DisplayName = "課程 enrollment payload 不加入未消費的簽到欄位")]
    public void Lesson_Enrollment_UsesTwoFields()
    {
        const string expected = "11111111-2222-3333-4444-555555555555_enroll";

        var encoded = QrCodeContentCodec.EncodeLesson(LessonId, "enroll", string.Empty);

        Assert.Equal(expected, encoded);
        Assert.True(QrCodeContentCodec.TryDecodeLesson(
            encoded,
            out var lessonId,
            out var classIndex,
            out var onboardType));
        Assert.Equal(LessonId, lessonId);
        Assert.Equal("enroll", classIndex);
        Assert.Equal(string.Empty, onboardType);
    }

    /// <summary>
    /// `enroll` 的比對需維持舊掃描器的序位大小寫語意，不能把不同索引誤判成雙欄格式。
    /// </summary>
    [Fact(DisplayName = "課程 enrollment 標記維持大小寫敏感")]
    public void Lesson_EnrollmentMarker_IsCaseSensitive()
    {
        const string expected = "11111111-2222-3333-4444-555555555555_Enroll_On";

        Assert.Equal(expected, QrCodeContentCodec.EncodeLesson(LessonId, "Enroll", "On"));
        Assert.True(QrCodeContentCodec.TryDecodeLesson(
            expected,
            out _,
            out var classIndex,
            out var onboardType));
        Assert.Equal("Enroll", classIndex);
        Assert.Equal("On", onboardType);
    }

    /// <summary>
    /// 主日 `@` 黃金格式保留屬性代碼內的底線，並回報實際使用的格式供呼叫端保留語意。
    /// </summary>
    [Fact(DisplayName = "主日 at 格式保留屬性代碼與分支")]
    public void Meeting_AtSeparator_PreservesCategoryAndFormat()
    {
        const string expected = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@new_sunday_first_qr@On";

        var encoded = QrCodeContentCodec.EncodeMeeting(
            MeetingId,
            "new_sunday_first_qr",
            "On",
            QrCodePayloadSeparator.AtSign);

        Assert.Equal(expected, encoded);
        Assert.True(QrCodeContentCodec.TryDecodeMeeting(
            encoded,
            out var meetingId,
            out var attribute,
            out var onboardType,
            out var separator));
        Assert.Equal(MeetingId, meetingId);
        Assert.Equal("new_sunday_first_qr", attribute);
        Assert.Equal("On", onboardType);
        Assert.Equal(QrCodePayloadSeparator.AtSign, separator);
    }

    /// <summary>
    /// 主日 `_` 格式必須維持舊動態類別分支，不可在解碼時正規化成 `@` 格式。
    /// </summary>
    [Fact(DisplayName = "主日底線格式保留動態類別分支")]
    public void Meeting_UnderscoreSeparator_PreservesLegacyBranch()
    {
        const string expected = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_new_meeting_qr_On";

        var encoded = QrCodeContentCodec.EncodeMeeting(
            MeetingId,
            "new_meeting_qr",
            "On",
            QrCodePayloadSeparator.Underscore);

        Assert.Equal(expected, encoded);
        Assert.True(QrCodeContentCodec.TryDecodeMeeting(
            encoded,
            out _,
            out var attribute,
            out var onboardType,
            out var separator));
        Assert.Equal("new_meeting_qr", attribute);
        Assert.Equal("On", onboardType);
        Assert.Equal(QrCodePayloadSeparator.Underscore, separator);
    }

    /// <summary>
    /// 個人 QR 新格式只輸出被掃描聯絡人的資料列 GUID；解析可讀取舊掃描器忽略的尾段。
    /// </summary>
    [Fact(DisplayName = "個人 payload 使用聯絡人資料列 GUID")]
    public void Personal_EncodeAndDecode_UsesContactIdentity()
    {
        const string expected = "01234567-89ab-cdef-0123-456789abcdef";

        var encoded = QrCodeContentCodec.EncodePersonal(ContactId);

        Assert.Equal(expected, encoded);
        Assert.True(QrCodeContentCodec.TryDecodePersonal(
            encoded + "_legacy-suffix",
            out var contactId));
        Assert.Equal(ContactId, contactId);
    }

    /// <summary>
    /// 個人掃描器只要 payload 任一處含 `@` 就優先取第一個 at 前綴，避免 `_` 前綴被錯誤授權成 GUID。
    /// </summary>
    [Fact(DisplayName = "個人 payload 維持 at 分隔優先序")]
    public void Personal_AtSeparator_TakesPrecedenceOverUnderscore()
    {
        var valid = "01234567-89ab-cdef-0123-456789abcdef@ignored";
        var ambiguous = "01234567-89ab-cdef-0123-456789abcdef_ignored@tail";

        Assert.True(QrCodeContentCodec.TryDecodePersonal(valid, out var contactId));
        Assert.Equal(ContactId, contactId);
        Assert.False(QrCodeContentCodec.TryDecodePersonal(ambiguous, out var rejectedId));
        Assert.Equal(Guid.Empty, rejectedId);
    }

    /// <summary>
    /// 小組週報格式必須以週報資料列 GUID 為首欄，並逐字保留簽到代碼。
    /// </summary>
    [Fact(DisplayName = "小組 payload 使用週報資料列 GUID")]
    public void SmallGroup_EncodeAndDecode_PreservesLegacyFields()
    {
        const string expected = "fedcba98-7654-3210-fedc-ba9876543210_Off";

        var encoded = QrCodeContentCodec.EncodeSmallGroup(WeeklyReportId, "Off");

        Assert.Equal(expected, encoded);
        Assert.True(QrCodeContentCodec.TryDecodeSmallGroup(
            encoded,
            out var weeklyReportId,
            out var onboardType));
        Assert.Equal(WeeklyReportId, weeklyReportId);
        Assert.Equal("Off", onboardType);
    }

    /// <summary>
    /// 空 GUID、損毀欄位、缺欄與格式歧義都必須拒絕，且輸出不殘留前次或輸入資料。
    /// </summary>
    [Fact(DisplayName = "無效 QR payload fail closed 並清空輸出")]
    public void Decode_InvalidPayloads_ReturnFalseAndClearOutputs()
    {
        Assert.False(QrCodeContentCodec.TryDecodeLesson(
            "not-a-guid_index_On",
            out var lessonId,
            out var classIndex,
            out var onboardType));
        Assert.Equal(Guid.Empty, lessonId);
        Assert.Equal(string.Empty, classIndex);
        Assert.Equal(string.Empty, onboardType);

        Assert.False(QrCodeContentCodec.TryDecodeMeeting(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@@On",
            out var meetingId,
            out var attribute,
            out var meetingOnboardType,
            out _));
        Assert.Equal(Guid.Empty, meetingId);
        Assert.Equal(string.Empty, attribute);
        Assert.Equal(string.Empty, meetingOnboardType);

        Assert.False(QrCodeContentCodec.TryDecodeSmallGroup(
            "fedcba98-7654-3210-fedc-ba9876543210_",
            out var weeklyReportId,
            out var groupOnboardType));
        Assert.Equal(Guid.Empty, weeklyReportId);
        Assert.Equal(string.Empty, groupOnboardType);

        Assert.False(QrCodeContentCodec.TryDecodePersonal(string.Empty, out var contactId));
        Assert.Equal(Guid.Empty, contactId);
    }

    /// <summary>
    /// 編碼器拒絕空穩定身分及會破壞固定欄位邊界的分隔字元，不輸出無法確定解讀的 payload。
    /// </summary>
    [Fact(DisplayName = "編碼拒絕空 GUID 與欄位分隔符注入")]
    public void Encode_EmptyIdentityOrAmbiguousFields_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            QrCodeContentCodec.EncodePersonal(Guid.Empty));
        Assert.Throws<ArgumentException>(() =>
            QrCodeContentCodec.EncodeLesson(LessonId, "lesson_04", "On"));
        Assert.Throws<ArgumentException>(() =>
            QrCodeContentCodec.EncodeMeeting(
                MeetingId,
                "new_sunday@first",
                "On",
                QrCodePayloadSeparator.AtSign));
        Assert.Throws<ArgumentException>(() =>
            QrCodeContentCodec.EncodeMeeting(
                MeetingId,
                "new_sunday@first",
                "On",
                QrCodePayloadSeparator.Underscore));
        Assert.Throws<ArgumentException>(() =>
            QrCodeContentCodec.EncodeSmallGroup(WeeklyReportId, "On_Off"));
    }

    /// <summary>
    /// 個人掃描內容有固定上限，超長輸入不進入切割或 GUID 解析流程。
    /// </summary>
    [Fact(DisplayName = "超過內容上限的 payload 被拒絕")]
    public void Decode_OverLimitPayload_ReturnsFalse()
    {
        var oversized = new string('x', 2049);

        Assert.False(QrCodeContentCodec.TryDecodePersonal(oversized, out var contactId));
        Assert.Equal(Guid.Empty, contactId);
    }
}
