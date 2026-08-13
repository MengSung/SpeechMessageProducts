// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/Package03SpecialResourceContracts.cs
// 用途：定義 P7.3 image、OptionSet metadata 與 weekly statistic 的唯一封閉純值契約。
//
// 信任與生命週期邊界：
// 1. 產品、Gateway 與 connector 只能傳遞這些 bounded pure values；禁止 CRM SDK、raw Stream、IFormFile、
//    JsonElement、FetchXML、paging cookie、endpoint、credential、token 或 session 跨越此檔案定義的邊界。
// 2. 影像 bytes 在 ingress、response construction 與 getter 都 defensive copy；本檔不持有 stream、decoder、
//    ArrayPool rental、cache、lease、timer、CTS 或 client，所有那些資源必須在 connector request scope 釋放。
// 3. metadata target 是小型 server allowlist key，真正的 entity/attribute 解析只能在 connector 固定表完成；
//    weekly result 不保留 page token，避免跨 request/profile/generation 的資料或資源洩漏。
// ============================================================================

using System.Text.Json.Serialization;

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// P7.3 支援的封閉影像媒體種類。enum 不接受 MIME string 或副檔名作為 authority；connector 必須同時驗證
/// magic bytes、實際 decoder format、dimension 與 pixels，未知或不符的格式在取得可重用 client 前失敗關閉。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactImageMediaKind
{
    /// <summary>PNG；適用於已驗證的 PNG signature 與實際 decoder format。</summary>
    Png = 1,

    /// <summary>JPEG；適用於已驗證的 SOI signature 與實際 decoder format。</summary>
    Jpeg = 2
}

/// <summary>
/// 影像更新的最小成功結果。它不含 contact identity、image bytes、hash、baseline、CRM response、endpoint、
/// credential、profile 或 trace；未知 timeout、取消、read-back mismatch 與 cleanup uncertainty 不得建構此結果。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactImageUpdateDisposition
{
    /// <summary>固定 entityimage update 已完成，且精確 read-back 已確認。</summary>
    Changed = 1
}

/// <summary>
/// 唯一可公開的 image write correlation 類別。它不是 correlation ID，無法當作跨使用者、fixture 或 transport
/// identity；其存在只證明 connector 已完成當次 bounded read-back，而非允許 timeout retry。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactImageUpdateCorrelationCategory
{
    /// <summary>固定欄位 update 後的 image bytes 與媒體種類都已由 connector read-back 確認。</summary>
    ReadBackConfirmed = 1
}

/// <summary>
/// 封閉 image request/response payload。constructor 與 getter 均複製 byte array，讓 caller 或 serializer 無法
/// 透過可變陣列改寫另一個 request/response；最大 wire/image policy 由 executor 與 connector 分層執行，避免
/// abstraction 層單獨宣稱 decoder 或 transport 安全。此值不擁有 stream/decoder/buffer，不能作 shared cache。
/// </summary>
public sealed class ContactImageResponseData
{
    private readonly byte[] _imageBytes;

    /// <summary>以 caller-owned bytes 建立 response-safe image copy；空 payload 代表 CRM image 缺失，合法但不等同 write success。</summary>
    public ContactImageResponseData(byte[] imageBytes, ContactImageMediaKind mediaKind)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (!Enum.IsDefined(mediaKind))
        {
            throw new ArgumentOutOfRangeException(nameof(mediaKind));
        }

        _imageBytes = imageBytes.ToArray();
        MediaKind = mediaKind;
    }

    /// <summary>取得經建構時驗證的封閉媒體種類；不得用此 enum 跳過 connector 的實際 decoder 驗證。</summary>
    [JsonPropertyName("mediaKind")]
    public ContactImageMediaKind MediaKind { get; }

    /// <summary>
    /// 取得一份新的 bytes copy。JSON serializer 使用此屬性但每次皆複製，確保 response body、cache 或呼叫端修改
    /// 不會反向改寫 envelope 內部值；呼叫端成為該新陣列唯一 owner，應在自己的短生命週期內釋放引用。
    /// </summary>
    [JsonPropertyName("imageBytes")]
    public byte[] ImageBytes => _imageBytes.ToArray();

    /// <summary>明確回傳 defensive copy，供非序列化 ProductClient 使用，避免公開內部 byte array。</summary>
    public byte[] GetImageBytes() => _imageBytes.ToArray();
}

/// <summary>
/// P7.4 顯示回應唯一分支的種類。此 discriminator 僅表達輸出方式，不傳遞 CRM SDK、LINE 憑證、連線或請求身分資料。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactImageDisplayKind
{
    /// <summary>已驗證且由回應單獨擁有的 PNG/JPEG 位元組。</summary>
    Image = 1,

    /// <summary>已驗證的 HTTPS LINE 頭像重新導向網址。</summary>
    LineRedirect = 2,

    /// <summary>沒有影像與可接受 LINE 網址時使用的預設頭像性別碼。</summary>
    DefaultAvatar = 3
}

/// <summary>
/// P7.4 聯絡人影像顯示的封閉聯集。每個 factory 只建立一個分支；影像在輸入與輸出均複製，網址僅接受無帳密的絕對 HTTPS URI，避免跨請求保留可變資料或不受信任重新導向。
/// </summary>
public sealed class ContactImageDisplayResponseData
{
    private readonly byte[]? _imageBytes;

    private ContactImageDisplayResponseData(
        ContactImageDisplayKind kind,
        byte[]? imageBytes,
        ContactImageMediaKind? mediaKind,
        Uri? lineRedirectUri,
        int? genderCode)
    {
        Kind = kind;
        _imageBytes = imageBytes?.ToArray();
        MediaKind = mediaKind;
        LineRedirectUri = lineRedirectUri;
        GenderCode = genderCode;
    }

    /// <summary>建立影像分支；輸入陣列立即複製，呼叫端後續修改不會影響封裝結果。</summary>
    public static ContactImageDisplayResponseData ForImage(byte[] imageBytes, ContactImageMediaKind mediaKind)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0 || !Enum.IsDefined(mediaKind))
        {
            throw new ArgumentOutOfRangeException(nameof(imageBytes));
        }

        return new ContactImageDisplayResponseData(
            ContactImageDisplayKind.Image,
            imageBytes,
            mediaKind,
            lineRedirectUri: null,
            genderCode: null);
    }

    /// <summary>建立 LINE 重新導向分支；僅接受絕對 HTTPS URI，拒絕帳密、fragment 與超過安全長度的值。</summary>
    public static ContactImageDisplayResponseData ForLineRedirect(string lineRedirectUri)
    {
        if (string.IsNullOrWhiteSpace(lineRedirectUri) || lineRedirectUri.Length > 2048 ||
            !Uri.TryCreate(lineRedirectUri, UriKind.Absolute, out var parsedUri) ||
            !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsedUri.UserInfo) ||
            !string.IsNullOrEmpty(parsedUri.Fragment))
        {
            throw new ArgumentException("lineRedirectUri is invalid.", nameof(lineRedirectUri));
        }

        return new ContactImageDisplayResponseData(
            ContactImageDisplayKind.LineRedirect,
            imageBytes: null,
            mediaKind: null,
            lineRedirectUri: parsedUri,
            genderCode: null);
    }

    /// <summary>建立預設頭像分支；性別碼是可選純量，null 表示既有中性頭像策略。</summary>
    public static ContactImageDisplayResponseData ForDefaultAvatar(int? genderCode)
        => new(
            ContactImageDisplayKind.DefaultAvatar,
            imageBytes: null,
            mediaKind: null,
            lineRedirectUri: null,
            genderCode: genderCode);

    /// <summary>回應分支種類；消費者必須依此值選擇唯一輸出，而不可猜測其他屬性。</summary>
    [JsonPropertyName("kind")]
    public ContactImageDisplayKind Kind { get; }

    /// <summary>影像媒體種類；僅在 <see cref="ContactImageDisplayKind.Image"/> 分支存在。</summary>
    [JsonPropertyName("mediaKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactImageMediaKind? MediaKind { get; }

    /// <summary>已驗證的 LINE HTTPS URI；僅在 <see cref="ContactImageDisplayKind.LineRedirect"/> 分支存在。</summary>
    [JsonPropertyName("lineRedirectUri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? LineRedirectUri { get; }

    /// <summary>預設頭像的可選性別碼；僅在 <see cref="ContactImageDisplayKind.DefaultAvatar"/> 分支存在。</summary>
    [JsonPropertyName("genderCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? GenderCode { get; }

    /// <summary>取得影像的防禦性複本；非影像分支 fail-closed，不會回傳 null 或其他分支資料。</summary>
    public byte[] GetImageBytes()
        => Kind == ContactImageDisplayKind.Image
            ? _imageBytes!.ToArray()
            : throw new InvalidOperationException("The display response does not contain image bytes.");
}

/// <summary>
/// image update 的最小封閉 response data。兩個 enum 配對由 <see cref="OperationResponseData"/> 驗證，
/// 因此不能用未定義 enum 或未 read-back 的 transport success 偽造成功。
/// </summary>
public sealed record ContactImageUpdateResponseData
{
    /// <summary>唯一合法的寫入成功 disposition。</summary>
    [JsonPropertyName("disposition")]
    public required ContactImageUpdateDisposition Disposition { get; init; }

    /// <summary>唯一合法的 bounded read-back category。</summary>
    [JsonPropertyName("correlationCategory")]
    public required ContactImageUpdateCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>
/// metadata OptionSet 的封閉 server target。它不接受任意 entity/attribute 字串，只有 connector private allowlist
/// 會將此分類映射為固定 CRM request；這避免 ProductClient 重新成為任意 metadata query adapter。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MetadataOptionSetTarget
{
    /// <summary>contact.customertypecode 的既有 ChurchReport commitment metadata。</summary>
    ContactCustomerTypeCode = 1
}

/// <summary>
/// OptionSet 的有界 pure value projection。ConfiguredOrder 保留 CRM metadata 原始順序，不可由 raw numeric value
/// 或 label 自行排序；label 上限由 connector/projection 驗證，任何 AttributeMetadata/OptionMetadata graph 都不可保存。
/// </summary>
public sealed record OptionSetOptionRecord
{
    /// <summary>取得固定 OptionSet integer value。</summary>
    [JsonPropertyName("value")]
    public required int Value { get; init; }

    /// <summary>取得投影後的 locale label；不含 LocalizedLabel/SDK graph。</summary>
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>取得 CRM configured order，必須非負且在單一 response 中唯一。</summary>
    [JsonPropertyName("configuredOrder")]
    public required int ConfiguredOrder { get; init; }
}

/// <summary>
/// weekly meeting statistics 的固定 projection。每一列只可包含已證實的 ID/name/createdOn/Sunday 欄位；它不含
/// Entity、paging cookie、FetchXML、next link 或 session，因而無法被下一個 request 續用或改寫。
/// </summary>
public sealed record MeetingStatisticRecord
{
    /// <summary>取得 meeting statistic 的純值 identity；connector 拒絕空 GUID 或錯誤 entity logical name。</summary>
    [JsonPropertyName("meetingStatisticId")]
    public required Guid MeetingStatisticId { get; init; }

    /// <summary>取得可選、受 byte limit 保護的顯示名稱。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>取得可選 UTC 建立時間；connector 不使用本機時區重解釋 CRM 值。</summary>
    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; init; }

    /// <summary>取得可選 UTC Sunday key；它只供產品顯示/比對，不攜帶 continuation 狀態。</summary>
    [JsonPropertyName("sundayDate")]
    public DateTimeOffset? SundayDate { get; init; }
}
