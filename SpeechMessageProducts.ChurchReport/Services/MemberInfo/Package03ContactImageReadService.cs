// ============================================================================
// 檔案：ChurchReport/Services/MemberInfo/Package03ContactImageReadService.cs
// 用途：為已完成伺服器授權的 MemberInfo contact 建立 Package03 圖片唯讀、DTO-only 的 request-local 回應。
//
// 信任與生命週期邊界：
// 1. browser 只可傳入 controller 已驗證的 locator；profile 與 workload 永遠由 deployment composition 固定，
//    不接受 session、query、header、endpoint、connector、credential 或 CRM identity 作為 routing authority。
// 2. service 只保留 DI-owned typed client 參考及 immutable profile scalar；不保留 HttpContext、Session、Entity、
//    image cache、stream、lease、timer、background task 或 mutable static state。transport 資源由既有 process host 擁有。
// 3. 每次 image getter 都 defensive-copy bytes。取消、空 payload、未知 media kind 或 typed fault 均不發布 partial
//    response，不 fallback/retry，避免跨使用者／跨 profile 資料或不確定資源狀態被重用。
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;

namespace ChurchReport.Services.MemberInfo;

/// <summary>
/// Package03 contact-image 的 request-local typed coordinator。
///
/// controller 必須在呼叫前完成 server-side MemberInfo scope 與 target contact 授權；本 service 只以固定
/// deployment profile、固定 workload 和已授權 Guid 送出唯一 typed read。它不決定使用者權限，也不建立
/// ProductClient、executor 或 connector，因此不會在每個 HTTP request 建立新的 pool、provider 或 session。
/// </summary>
public sealed class Package03ContactImageReadService
{
    /// <summary>唯一允許的 server-owned workload；route、query、header 與 session 均不可覆寫。</summary>
    private const string WorkloadSubjectId = "church-report-member-info-image-read";

    /// <summary>由 bootstrap／DI 擁有的 stateless typed client；本 service 絕不 Dispose 或快取它。</summary>
    private readonly IPackage03SpecialResourceClient _package03Client;

    /// <summary>經 deployment configuration 驗證的固定 profile；只在目前呼叫堆疊傳遞，不含 endpoint 或 credential。</summary>
    private readonly string _profileAlias;

    /// <summary>
    /// 建立只讀 coordinator。constructor 不執行 I/O、不建立 connection、stream、provider、timer 或 cache；
    /// 空白 profile 立即拒絕，避免在任何 typed dispatch 前猜測既有 CrmConnection 或其他使用者的 profile。
    /// </summary>
    /// <param name="package03Client">由受控 bootstrap 或 DI 提供的 stateless typed client；service 不擁有其生命週期。</param>
    /// <param name="profileAlias">deployment-owned 非空 profile alias；不得來自 browser 或 session。</param>
    public Package03ContactImageReadService(
        IPackage03SpecialResourceClient package03Client,
        string profileAlias)
    {
        _package03Client = package03Client ?? throw new ArgumentNullException(nameof(package03Client));
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new InvalidOperationException(
                "DynamicsAccess:ProfileAlias is required for the Package03 contact-image read boundary.");
        }

        _profileAlias = profileAlias.Trim();
    }

    /// <summary>
    /// 讀取單一已授權 contact 的圖片，並把封閉 media kind 映射成安全 MIME type。
    ///
    /// upstream result 在所有 validation 完成前不會發布；空 payload、未知 kind、typed fault 或 cancellation
    /// 都保留 fail-closed 行為。取消 token 原樣傳遞給唯一 operation，既不 retry，也不以 legacy image、LINE URL
    /// 或 gender avatar fallback，因此不會開啟另一條 CRM 或外部圖片資料路徑。
    /// </summary>
    /// <param name="contactId">已由 controller 完成 server-side target authorization 的非空 locator。</param>
    /// <param name="cancellationToken">HTTP request 的取消 token；必須原樣傳遞並由下游 lease owner 處理清理。</param>
    /// <returns>含 defensive-copy image bytes 與封閉 MIME type 的 request-local result。</returns>
    public async Task<Package03ContactImageReadResult> RetrieveAsync(
        Guid contactId,
        CancellationToken cancellationToken = default)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty authorized contact ID is required.", nameof(contactId));
        }

        var upstream = await _package03Client.RetrieveContactImageAsync(
                new ContactImageRetrieveRequest
                {
                    ProfileAlias = _profileAlias,
                    WorkloadSubjectId = WorkloadSubjectId,
                    ContactId = contactId
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (upstream is null)
        {
            throw new InvalidOperationException("The Package03 contact-image response was incomplete.");
        }

        var imageBytes = upstream.GetImageBytes();
        if (imageBytes.Length == 0)
        {
            throw new InvalidOperationException("The Package03 contact-image response contained no image bytes.");
        }

        var contentType = upstream.MediaKind switch
        {
            ContactImageMediaKind.Png => "image/png",
            ContactImageMediaKind.Jpeg => "image/jpeg",
            _ => throw new InvalidOperationException("The Package03 contact-image response used an unsupported media kind.")
        };

        return new Package03ContactImageReadResult(imageBytes, contentType);
    }
}

/// <summary>
/// Package03 圖片唯讀結果。這個值是每一個 request 新建的防禦性 bytes 容器；它不擁有 stream、decoder、
/// cache、client、lease 或可延長到下一位使用者的資源。呼叫端取得的陣列副本只在目前 response 寫出期間存在。
/// </summary>
public sealed class Package03ContactImageReadResult
{
    /// <summary>結果唯一擁有的 image bytes；永不直接公開給 controller、serializer 或其他 request。</summary>
    private readonly byte[] _imageBytes;

    /// <summary>
    /// 以已驗證的 non-empty bytes 建立 defensive result copy。內容型別只能由 service 的封閉 kind mapping 產生，
    /// 不接受 upstream MIME string，避免把非預期內容或 browser-controlled 值放入 HTTP header。
    /// </summary>
    /// <param name="imageBytes">service 已完整驗證的 caller-owned bytes；constructor 立即複製。</param>
    /// <param name="contentType">service 已由 enum 映射的 PNG/JPEG MIME type。</param>
    internal Package03ContactImageReadResult(byte[] imageBytes, string contentType)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("An image result cannot be empty.", nameof(imageBytes));
        }

        if (!string.Equals(contentType, "image/png", StringComparison.Ordinal) &&
            !string.Equals(contentType, "image/jpeg", StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(contentType));
        }

        _imageBytes = (byte[])imageBytes.Clone();
        ContentType = contentType;
    }

    /// <summary>封閉 HTTP content type；不可由 CRM、browser、session 或動態設定覆寫。</summary>
    public string ContentType { get; }

    /// <summary>
    /// 取得 response writer 專屬的 image bytes copy。每次呼叫都配置新陣列，使任何 serializer 或 controller
    /// 修改都不會污染此結果、另一個 await continuation 或下一個使用者的 request。
    /// </summary>
    /// <returns>caller-owned image byte copy。</returns>
    public byte[] GetImageBytes() => (byte[])_imageBytes.Clone();
}
