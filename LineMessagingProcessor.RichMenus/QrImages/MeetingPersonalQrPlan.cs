/*
 * 檔案整體說明
 * 目的與定位：P5-09 Host 消費既有 QrImageGenerate 的 schema2 純值邊界，
 * 與 schema1 Lesson 共用 consumer，但不更改 Core、活動或待辦類型。
 * 主要流程：只接受五欄封閉 payload，重算來源鍵並驗證 contact；按研究順序展開
 * 固定 On 九張、Off 九張及動態一至十的 On／Off，共三十八張。個人姓名必須由
 * scoped SDK 重讀；候選只委派既有 codec，不採用 payload 姓名或組織名稱 fallback。
 * 使用邊界與維護：本檔不查資料、不授權、不持有 stream、Session 或背景資源。
 * 純值候選及姓名僅存活於單筆 scope；圖片與正式發布由 publisher 唯一負責。
 * 驗證方式：離線五欄拒絕矩陣與逐字內容測試保護欄位、順序及來源一致性，
 * 不能把 GUID 語法驗證當成資料庫授權或 D365 實機證據。
 */
using System.Globalization;
using System.Text.Json;
using SpeechMessage.Hosting.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using SpeechMessageProducts.GoodShepherd.Core.Rules;

namespace LineMessagingProcessor.RichMenus.QrImages;

/// <summary>封閉 schema2 的 detached 值；JSON kind 僅整數 0／1，內部名稱不擴張共用命令。</summary>
public sealed record MeetingPersonalQrPayload
{
    /// <summary>只有 Parse 可建立；避免 caller 繞過固定版本及數量驗證。</summary>
    private MeetingPersonalQrPayload(string kind, Guid recordId)
    {
        Kind = kind;
        RecordId = recordId;
    }

    /// <summary>核定的 meeting 或 personal，不代表任何授權。</summary>
    public string Kind { get; }
    /// <summary>正式來源記錄；個人必須與待辦 contact 完全一致。</summary>
    public Guid RecordId { get; }
    /// <summary>固定來源事件 ID，重送不因嘗試次數改變。</summary>
    public Guid SourceId => DeterministicGuid.Create("qrcodes." + Kind, RecordId, 1);
    /// <summary>同一來源固定的正式附件 ID；不以姓名或隨機值防重。</summary>
    public Guid AnnotationId => DeterministicGuid.Create("qrcodes.personal.annotation", SourceId, 1);
    /// <summary>核定圖片數量；版本一不接受任意擴張。</summary>
    public int ImageCount => Kind == "meeting" ? 38 : 1;

    /// <summary>先限制 JSON 預算，再選 schema；完整欄位仍由各 schema parser 重驗。</summary>
    public static bool IsSchemaTwo(string json)
    {
        using var document = Open(json);
        return document.RootElement.TryGetProperty("schema", out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var schema) && schema == 2;
    }

    /// <summary>只接受恰五欄、schema2、kind 整數零／一、版本一及相符圖片數；重複／額外欄位拒絕。</summary>
    public static MeetingPersonalQrPayload Parse(string json)
    {
        using var document = Open(json);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var root = document.RootElement;
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name) ||
                property.Name is not ("schema" or "kind" or "recordId" or "contentVersion" or "imageCount"))
                throw new ArgumentException("Invalid schema2 QR fields.");
        if (names.Count != 5 || !Integer(root.GetProperty("schema"), 2) ||
            !Integer(root.GetProperty("contentVersion"), 1) ||
            !(Integer(root.GetProperty("kind"), 0) || Integer(root.GetProperty("kind"), 1)) ||
            root.GetProperty("recordId").ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(root.GetProperty("recordId").GetString(), "D", out var id) || id == Guid.Empty)
            throw new ArgumentException("Invalid schema2 QR payload.");
        var kind = Integer(root.GetProperty("kind"), 0) ? "meeting" : "personal";
        if (!Integer(root.GetProperty("imageCount"), kind == "meeting" ? 38 : 1))
            throw new ArgumentException("Invalid schema2 QR image count.");
        return new(kind, id);
    }

    /// <summary>待辦的 contact 與穩定事件鍵都須相符；教會仍須由正式來源重驗。</summary>
    public void ValidateWorkItem(OutboundWorkItem item)
    {
        if (item.Type != OutboundWorkType.QrImageGenerate || item.ChurchId == Guid.Empty ||
            item.ContactId != (Kind == "personal" ? RecordId : (Guid?)null) ||
            item.IdempotencyKey != $"{item.ChurchId:N}-{(int)OutboundWorkType.QrImageGenerate}-{SourceId:N}")
            throw new ArgumentException("QR work source does not match its payload.");
    }

    /// <summary>JSON 物件最多四千字與八層；原本文字不儲存於候選。</summary>
    private static JsonDocument Open(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4000)
            throw new ArgumentException("QR payload exceeds its bounded contract.");
        var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
        document.Dispose();
        throw new ArgumentException("QR payload must be an object.");
    }

    /// <summary>拒絕字串、浮點及非核定整數，不作寬鬆轉換。</summary>
    private static bool Integer(JsonElement value, int expected) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var actual) && actual == expected;
}

/// <summary>兩種內容前綴必須明確設定；沿用 Lesson 的 URI／結尾驗證，沒有 fallback。</summary>
public sealed class MeetingPersonalQrOptions
{
    /// <summary>驗證部署前綴，不保存組態 provider 或其他教會資料。</summary>
    public MeetingPersonalQrOptions(string meetingPrefix, string personalPrefix,
        IReadOnlyCollection<Guid>? meetingIds = null)
    {
        var validated = new LessonQrBundleOptions(meetingPrefix, personalPrefix);
        MeetingPrefix = validated.RegularPrefix;
        PersonalPrefix = validated.PollPrefix;
        var ids = meetingIds ?? Array.Empty<Guid>();
        if (ids.Count > 10000 || ids.Any(id => id == Guid.Empty) ||
            ids.Distinct().Count() != ids.Count)
            throw new ArgumentException("Meeting QR bindings are invalid.", nameof(meetingIds));
        MeetingIds = new HashSet<Guid>(ids);
    }
    /// <summary>聚會固定及動態 QR 的部署前綴。</summary>
    public string MeetingPrefix { get; }
    /// <summary>個人 QR 的部署前綴；姓名仍由資料庫取得。</summary>
    public string PersonalPrefix { get; }
    /// <summary>部署者明確授權的 meeting 白名單；空集合代表沒有可發布的 meeting。</summary>
    public IReadOnlyCollection<Guid> MeetingIds { get; }

    /// <summary>要求來源 meeting 已由部署設定明確綁定，不接受出席資料推導的 fallback。</summary>
    public void RequireMeeting(Guid meetingId)
    {
        if (meetingId == Guid.Empty || !MeetingIds.Contains(meetingId))
            throw new InvalidOperationException("Meeting QR source is not deployment-bound.");
    }
}

/// <summary>建立完整純值候選，不產圖、不發布；不同正式 ID 同名仍保留獨立內容。</summary>
public static class MeetingPersonalQrPlan
{
    /// <summary>按舊固定 URL 欄位與 codec 規則展開；結果不可由 caller 修改。</summary>
    public static IReadOnlyList<LessonQrImageDescriptor> Create(MeetingPersonalQrPayload payload,
        MeetingPersonalQrOptions options, string fullName)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(options);
        if (payload.Kind == "personal")
        {
            var content = options.PersonalPrefix + QrCodeContentCodec.EncodePersonal(payload.RecordId, fullName);
            QrImageRenderer.ValidateContent(content);
            return Array.AsReadOnly(new[] { new LessonQrImageDescriptor("personal", content) });
        }

        var on = new[] { "new_sunday_first_qr_on_url", "new_sunday_second_qr_on_url",
            "new_saturday_worship_on", "new_yongmen_on", "new_prayer_meeting_qr_on_url",
            "new_child_on", "new_big_disciple_qr_on_url", "new_leadership_small_lecture_qr_on_url",
            "new_leaders_gather_qr_on_url" };
        var off = new[] { "new_sunday_first_qr_off_url", "new_sunday_second_qr_off_url",
            "new_saturday_worship_off", "new_yongmen_off", "new_prayer_meeting_qr_off_url",
            "new_child_off", "new_big_disciple_qr_off_url", "new_leadership_small_lecture_qr_off_url",
            "new_leaders_gather_qr_off_url" };
        var images = new List<LessonQrImageDescriptor>(38);
        foreach (var field in on) Add(field, field, "On", QrCodePayloadSeparator.AtSign);
        foreach (var field in off) Add(field, field, "Off", QrCodePayloadSeparator.AtSign);
        for (var i = 1; i <= 10; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            Add($"new_{index}_sign_on_url", index, "On", QrCodePayloadSeparator.Underscore);
            Add($"new_{index}_sign_off_url", index, "Off", QrCodePayloadSeparator.Underscore);
        }
        return images.AsReadOnly();

        // 欄位來自本檔固定白名單，不能由 caller 提供路徑；完整內容先驗再配置圖片。
        void Add(string field, string attribute, string state, QrCodePayloadSeparator separator)
        {
            var content = options.MeetingPrefix +
                QrCodeContentCodec.EncodeMeeting(payload.RecordId, attribute, state, separator);
            QrImageRenderer.ValidateContent(content);
            images.Add(new(field, content));
        }
    }
}
