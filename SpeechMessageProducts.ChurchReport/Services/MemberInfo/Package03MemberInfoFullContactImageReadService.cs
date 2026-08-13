// ============================================================================
// 檔案：ChurchReport/Services/MemberInfo/Package03MemberInfoFullContactImageReadService.cs
// 用途：將 Package03 完整 contact-image display union 映射成 request-local、無 SDK 的安全結果。
// 信任邊界：controller 必須先完成 server-side scope、GUID locator 解析與目標授權；本 service
// 僅使用 deployment-owned profile、固定 workload 與已授權 contact。它不保存 HttpContext、Session、
// principal、cache、stream、lease、timer、client ownership 或 background task，取消由下游 owner 清理。
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;

namespace ChurchReport.Services.MemberInfo;

/// <summary>
/// P7.4 完整 contact-image display 的 request-local typed coordinator。
/// ProductClient 的單次 projection 會選出 image、LINE redirect 或 default avatar 唯一分支；本 service
/// 不 retry、fallback、cache 或攜帶上游 raw response，並原樣轉送 HTTP request cancellation token。
/// </summary>
public sealed class Package03MemberInfoFullContactImageReadService
{
    /// <summary>固定 server-owned workload；不可由 route、query、header、Session 或 caller 覆寫。</summary>
    private const string WorkloadSubjectId = "churchreport-memberinfo";

    /// <summary>stateless ProductClient facade；其 executor、lease、transport 與 disposal 由 process host 擁有。</summary>
    private readonly IPackage03SpecialResourceClient _package03Client;

    /// <summary>部署組合根驗證後的 profile alias，不含 endpoint、credential 或 token。</summary>
    private readonly string _profileAlias;

    /// <summary>建立不執行 I/O 且不取得任何外部資源的 request coordinator。</summary>
    /// <param name="package03Client">受控 DI 擁有的無狀態 ProductClient。</param>
    /// <param name="profileAlias">部署端固定、非空的 Dynamics profile alias。</param>
    public Package03MemberInfoFullContactImageReadService(
        IPackage03SpecialResourceClient package03Client,
        string profileAlias)
    {
        _package03Client = package03Client ?? throw new ArgumentNullException(nameof(package03Client));
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new InvalidOperationException(
                "DynamicsAccess:ProfileAlias is required for the Package03 full contact-image display boundary.");
        }

        _profileAlias = profileAlias.Trim();
    }

    /// <summary>
    /// 執行唯一 typed display projection 並驗證閉合三分支。任何空圖片、未知 media kind、無效 URL、
    /// null result 或未支援 discriminator 都在 response 發布前 fail closed；OperationCanceledException
    /// 不被攔截，讓 executor/lease owner 執行其既有 deterministic cleanup。
    /// </summary>
    /// <param name="contactId">controller 已授權的非空 contact locator。</param>
    /// <param name="cancellationToken">HTTP RequestAborted，必須原樣傳遞且不被 service 保存。</param>
    /// <returns>只含 image bytes、已驗證 redirect URL 或 optional gender scalar 的 immutable result。</returns>
    public async Task<Package03MemberInfoFullContactImageReadResult> RetrieveAsync(
        Guid contactId,
        CancellationToken cancellationToken = default)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty authorized contact ID is required.", nameof(contactId));
        }

        var upstream = await _package03Client.RetrieveContactImageDisplayAsync(
                new ContactImageDisplayRetrieveRequest
                {
                    ProfileAlias = _profileAlias,
                    WorkloadSubjectId = WorkloadSubjectId,
                    ContactId = contactId
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (upstream is null)
        {
            throw new InvalidOperationException("The Package03 contact-image display response was incomplete.");
        }

        return upstream.Kind switch
        {
            ContactImageDisplayKind.Image => CreateImageResult(upstream),
            ContactImageDisplayKind.LineRedirect => CreateLineRedirectResult(upstream),
            ContactImageDisplayKind.DefaultAvatar => new Package03MemberInfoFullContactImageReadResult(
                Package03MemberInfoFullContactImageReadResultKind.DefaultAvatar,
                imageBytes: null,
                contentType: null,
                lineRedirectUrl: null,
                upstream.GenderCode),
            _ => throw new InvalidOperationException("The Package03 contact-image display kind was unsupported.")
        };
    }

    /// <summary>驗證 image branch 的非空 bytes 與閉合 PNG/JPEG media kind，並立即複製陣列。</summary>
    private static Package03MemberInfoFullContactImageReadResult CreateImageResult(ContactImageDisplayResult upstream)
    {
        byte[] bytes;
        try
        {
            bytes = upstream.GetImageBytes();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException("The Package03 image branch did not contain bytes.", exception);
        }
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("The Package03 image branch contained no bytes.");
        }

        var contentType = upstream.MediaKind switch
        {
            ContactImageMediaKind.Png => "image/png",
            ContactImageMediaKind.Jpeg => "image/jpeg",
            _ => throw new InvalidOperationException("The Package03 image branch used an unsupported media kind.")
        };

        return new Package03MemberInfoFullContactImageReadResult(
            Package03MemberInfoFullContactImageReadResultKind.Image,
            bytes,
            contentType,
            lineRedirectUrl: null,
            genderCode: null);
    }

    /// <summary>
    /// 驗證 LINE redirect 只能是無 user-info、無 fragment、預設 HTTPS port 的絕對 URL；這與 Data8 connector
    /// 的 allowlist policy 一致。即使上游 union 因替身、未來重構或錯誤資料繞過 connector，本層仍在發布 MVC
    /// redirect 前 fail-closed，避免產品層產生跨層 policy drift 或將非預設 port 當成已核准圖片端點。
    /// </summary>
    private static Package03MemberInfoFullContactImageReadResult CreateLineRedirectResult(ContactImageDisplayResult upstream)
    {
        var uri = upstream.LineRedirectUri;
        if (uri is null || !uri.IsAbsoluteUri ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.IsDefaultPort || uri.Host.Length == 0 ||
            !IsApprovedLineHost(uri.Host))
        {
            throw new InvalidOperationException("The Package03 LINE redirect URL was invalid.");
        }

        return new Package03MemberInfoFullContactImageReadResult(
            Package03MemberInfoFullContactImageReadResultKind.LineRedirect,
            imageBytes: null,
            contentType: null,
            uri.AbsoluteUri,
            genderCode: null);
    }

    /// <summary>
    /// 限制外部 redirect 到與 Data8 connector 相同的兩個部署核准 LINE 圖片 host，避免 CRM 字串形成
    /// open redirect，同時避免跨層 allowlist 漂移使已驗證的 connector union 在產品層被錯誤拒絕。host 比較
    /// 刻意採精確值，不接受泛用子網域、caller-provided 規則或非預設 HTTPS port。
    /// </summary>
    private static bool IsApprovedLineHost(string host)
        => host.Equals("profile.line-scdn.net", StringComparison.OrdinalIgnoreCase) ||
           host.Equals("obs.line-apps.com", StringComparison.OrdinalIgnoreCase);
}

/// <summary>完整 display 的閉合三分支 discriminator；未知值不會被映射成預設分支。</summary>
public enum Package03MemberInfoFullContactImageReadResultKind
{
    /// <summary>已驗證 PNG/JPEG bytes。</summary>
    Image,
    /// <summary>已驗證 LINE HTTP(S) URL。</summary>
    LineRedirect,
    /// <summary>由既有 avatar factory 產生的 optional gender scalar。</summary>
    DefaultAvatar
}

/// <summary>
/// ChurchReport display result。constructor 立即複製 image bytes；getter 每次都回傳新陣列，且不擁有
/// stream、decoder、client、cache、lease、timer 或可延長至下一 request 的資源。
/// </summary>
public sealed class Package03MemberInfoFullContactImageReadResult
{
    private readonly byte[]? _imageBytes;

    /// <summary>建立已由 service 驗證的單一 branch；不接受可由外部同時填入的多 branch payload。</summary>
    internal Package03MemberInfoFullContactImageReadResult(
        Package03MemberInfoFullContactImageReadResultKind kind,
        byte[]? imageBytes,
        string? contentType,
        string? lineRedirectUrl,
        int? genderCode)
    {
        Kind = kind;
        _imageBytes = imageBytes?.ToArray();
        ContentType = contentType;
        LineRedirectUrl = lineRedirectUrl;
        GenderCode = genderCode;

        var valid = kind switch
        {
            Package03MemberInfoFullContactImageReadResultKind.Image =>
                _imageBytes is { Length: > 0 } &&
                (string.Equals(contentType, "image/png", StringComparison.Ordinal) ||
                 string.Equals(contentType, "image/jpeg", StringComparison.Ordinal)) &&
                lineRedirectUrl is null &&
                genderCode is null,
            Package03MemberInfoFullContactImageReadResultKind.LineRedirect =>
                _imageBytes is null &&
                contentType is null &&
                !string.IsNullOrWhiteSpace(lineRedirectUrl) &&
                genderCode is null,
            Package03MemberInfoFullContactImageReadResultKind.DefaultAvatar =>
                _imageBytes is null &&
                contentType is null &&
                lineRedirectUrl is null,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException("The full contact-image result must contain exactly one valid branch.");
        }
    }

    /// <summary>取得閉合 discriminator。</summary>
    public Package03MemberInfoFullContactImageReadResultKind Kind { get; }

    /// <summary>取得 PNG/JPEG MIME type；其他 branch 為 null。</summary>
    public string? ContentType { get; }

    /// <summary>取得 image bytes defensive copy；非 image branch 回傳空陣列。</summary>
    public byte[] GetImageBytes() => _imageBytes?.ToArray() ?? Array.Empty<byte>();

    /// <summary>取得已驗證的 LINE URL；非 redirect branch 為 null。</summary>
    public string? LineRedirectUrl { get; }

    /// <summary>取得 optional gender scalar；非 avatar branch 為 null。</summary>
    public int? GenderCode { get; }
}
