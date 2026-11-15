/*
 * 檔案整體說明
 *
 * 目的與定位：定義 P5-07 課程 QR 圖片組的純值展開邊界，供待辦消費端在
 * 已完成課程／教會授權後建立完整的圖片候選。它承接 LessonLinkRule 的
 * 31／32 張組合，不查詢 D365、不寫回資料列，也不建立網路或檔案資源。
 *
 * 主要功能與流程：依固定的十五堂簽到／簽退、報名及可選問卷欄位產生
 * `LessonQrImageDescriptor`；一般圖片與問卷使用呼叫端提供的設定前綴，
 * 內容中的正式課程 ID 只來自參數，不依組織名稱或顯示名稱分支。
 * contentVersion 僅作為計畫版本邊界驗證，實際內容格式仍遵守既有 QR codec。
 *
 * 使用邊界與維護：前綴必須是絕對 URI 且以 `QrCodeId=` 結尾，避免把
 * 未驗證路徑、token 或任意查詢字串帶入圖片；空 ID、非正版本及重複欄位
 * 直接 fail closed。此檔沒有 Session、快取、背景工作、timer 或 IDisposable
 * owner；每次回傳的唯讀清單只屬呼叫端本次候選，實際產圖與原子回寫由上層
 * handler 負責並在完成或失敗時釋放資源。
 *
 * 驗證方式：Core 以合成正式 ID 驗證 31／32 張數量、欄位唯一性、一般與問卷
 * 前綴分離及非法設定拒絕；這些測試不代表 D365／LINE 實機發布成功。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;

namespace LineMessagingProcessor.RichMenus.QrImages;

/// <summary>
/// 課程 QR 產圖所需的兩個設定式內容前綴；不保存教會、使用者或 credential。
/// </summary>
public sealed record LessonQrBundleOptions
{
    /// <summary>建立設定並驗證前綴是可辨識的絕對 URI。</summary>
    public LessonQrBundleOptions(string regularPrefix, string pollPrefix)
    {
        RegularPrefix = ValidatePrefix(regularPrefix, nameof(regularPrefix));
        PollPrefix = ValidatePrefix(pollPrefix, nameof(pollPrefix));
    }

    /// <summary>十五堂及報名 QR 使用的設定前綴。</summary>
    public string RegularPrefix { get; }

    /// <summary>問卷 QR 使用的設定前綴，必須明確提供，不能回落一般前綴。</summary>
    public string PollPrefix { get; }

    /// <summary>檢查固定前綴，不解析或保存任何輸入身分。</summary>
    private static string ValidatePrefix(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "line") ||
            !value.EndsWith("QrCodeId=", StringComparison.Ordinal))
            throw new ArgumentException("QR prefix must be an absolute configured URI ending with QrCodeId=.", parameterName);
        return value;
    }
}

/// <summary>一張候選圖片的資料列欄位與 QR 內容；不持有圖片 stream。</summary>
public sealed record LessonQrImageDescriptor
{
    /// <summary>只接受固定欄位與非空內容，避免發布部分或無名圖片。</summary>
    public LessonQrImageDescriptor(string fieldName, string content)
    {
        if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("QR image descriptor is incomplete.");
        FieldName = fieldName;
        Content = content;
    }

    /// <summary>要回寫的課程欄位。</summary>
    public string FieldName { get; }

    /// <summary>要交給 QR renderer 的完整內容。</summary>
    public string Content { get; }
}

/// <summary>
/// 課程 QR 圖片的完整候選清單。建立後不可修改，供 handler 先全部驗證再發布。
/// </summary>
public sealed class LessonQrBundlePlan
{
    private LessonQrBundlePlan(Guid lessonId, int contentVersion,
        IReadOnlyList<LessonQrImageDescriptor> images)
    {
        LessonId = lessonId;
        ContentVersion = contentVersion;
        Images = images;
    }

    /// <summary>產生候選所屬的正式課程 ID。</summary>
    public Guid LessonId { get; }

    /// <summary>此候選所使用的內容版本。</summary>
    public int ContentVersion { get; }

    /// <summary>固定順序且不可變的 31／32 張圖片描述。</summary>
    public IReadOnlyList<LessonQrImageDescriptor> Images { get; }

    /// <summary>讓既有測試及消費端以清單語意讀取候選數量。</summary>
    public int Count => Images.Count;

    /// <summary>
    /// 依舊程式固定欄位展開圖片候選；全部資料先在記憶體內完成，任何錯誤都不
    /// 產生部分發布。includePoll 是入口契約，不是組織或教會名稱分支。
    /// </summary>
    public static LessonQrBundlePlan Create(Guid lessonId, int contentVersion,
        bool includePoll, LessonQrBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (lessonId == Guid.Empty) throw new ArgumentException("Lesson identity is required.", nameof(lessonId));
        if (contentVersion <= 0) throw new ArgumentOutOfRangeException(nameof(contentVersion));

        var images = new List<LessonQrImageDescriptor>(includePoll ? 32 : 31);
        for (var index = 1; index <= 15; index++)
        {
            Add(images, "new_" + index + "_signon_url",
                options.RegularPrefix + lessonId.ToString("D") + "_" + index + "_On");
            Add(images, "new_" + index + "_signoff_url",
                options.RegularPrefix + lessonId.ToString("D") + "_" + index + "_Off");
        }

        Add(images, "new_enroll_ursl",
            options.RegularPrefix + QrCodeContentCodec.EncodeLesson(lessonId, "enroll", ""));
        if (includePoll)
        {
            Add(images, "new_poll_url",
                options.PollPrefix + lessonId.ToString("D") + "_poll");
        }

        return new LessonQrBundlePlan(lessonId, contentVersion,
            new ReadOnlyCollection<LessonQrImageDescriptor>(images));
    }

    /// <summary>加入候選時檢查欄位唯一，避免原子發布前產生覆蓋。</summary>
    private static void Add(List<LessonQrImageDescriptor> images, string field, string content)
    {
        foreach (var image in images)
            if (StringComparer.Ordinal.Equals(image.FieldName, field))
                throw new InvalidOperationException("QR image field is duplicated.");
        images.Add(new LessonQrImageDescriptor(field, content));
    }
}
