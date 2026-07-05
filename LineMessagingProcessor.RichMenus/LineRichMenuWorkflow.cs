using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu API 的共用 workflow。
/// 此型別只負責建立 RichMenu、上傳 PNG、連結使用者、解除連結與刪除遠端選單；
/// 選單規則、使用者分群、畫面流程與產品 policy 都留在呼叫端或更上層 orchestrator。
/// </summary>
/// <remarks>
/// 保母級說明：
/// 這個 workflow 是比較低階的 RichMenu API 封裝，主要服務舊流程或需要
/// 「建立一份 RichMenu -> 上傳圖片 -> 綁定某個使用者」的一次性操作。
///
/// 與 <see cref="LineRichMenuProvisioningWorkflow"/> 的差異：
/// - ProvisioningWorkflow：偏新版共用架構，依 catalog 建立可多人共用、可 fingerprint 比對的選單。
/// - LineRichMenuWorkflow：偏單次操作，適合舊流程或明確要求建立/刪除遠端選單的情境。
///
/// 未來產品若要做穩定、可維護、可多人共用的 RichMenu，優先使用 catalog + provisioning + assignment。
/// 只有在確定要建立使用者專屬或一次性 RichMenu 時，才直接使用本類別。
/// </remarks>
public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow
{
    // 對 LINE RichMenu API 的抽象。這裡不直接 new HTTP client，也不直接處理 token。
    // 這讓舊流程可以沿用 create/upload/link/delete 能力，同時由測試以 processor 假物件精準模擬 provider 回應。
    private readonly ILineRichMenuProcessor _processor;

    public LineRichMenuWorkflow(ILineRichMenuProcessor processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    public async Task<LineRichMenuResult> CreateUploadAndLinkAsync(LineRichMenuCreateUploadAndLinkRequest request)
    {
        // 先做本機資料驗證。
        // 如果 userId、RichMenu 定義或圖片 factory 不存在，不應該打 LINE API。
        var validation = ValidateCreateRequest(request);
        if (validation != null)
        {
            return validation;
        }

        // richMenuId 會在 CreateRichMenuAsync 成功後取得。
        // 若後續 upload 或 link 失敗，仍把 richMenuId 放進 result，方便管理端追查或清理。
        string? richMenuId = null;

        try
        {
            // LINE API 第一步：建立 RichMenu metadata / layout。
            // 注意：這一步只建立選單定義，還沒有圖片。
            richMenuId = await _processor.CreateRichMenuAsync(request.RichMenu).ConfigureAwait(false);

            // LINE API 第二步需要上傳 PNG。
            // 圖片來源由呼叫端提供，可能是檔案、內嵌資源或記憶體 stream。
            using var imageStream = request.PngImageStreamFactory();
            if (imageStream == null)
            {
                // 圖片 factory 回傳 null 是呼叫端設定錯誤。
                // 此時 RichMenu 可能已建立成功，但無法完成圖片上傳與使用者綁定。
                return LineRichMenuResult.Failure(
                    request.UserId,
                    richMenuId,
                    LineRichMenuStatus.ValidationFailed,
                    "line-richmenu-image-stream-required",
                    "RichMenu PNG image stream is required.",
                    null,
                    request.Metadata);
            }

            await _processor.UploadRichMenuPngImageAsync(richMenuId, imageStream).ConfigureAwait(false);

            // LINE API 第三步：把剛建立好的 RichMenu 綁到特定使用者。
            await _processor.LinkRichMenuToUserAsync(request.UserId, richMenuId).ConfigureAwait(false);

            return LineRichMenuResult.Success(request.UserId, richMenuId, request.Metadata);
        }
        catch (LineResponseException ex)
        {
            // LINE 有回應，但拒絕這次請求，例如參數錯誤、token 權限不足、richMenuId 無效。
            return LineRichMenuResult.Failure(
                request.UserId,
                richMenuId,
                LineRichMenuStatus.ProviderRejected,
                "line-richmenu-provider-rejected",
                ex.Message,
                ex,
                request.Metadata);
        }
        catch (HttpRequestException ex)
        {
            // 網路層錯誤，例如 DNS、連線中斷、TLS 或 HTTP pipeline 失敗。
            return LineRichMenuResult.Failure(
                request.UserId,
                richMenuId,
                LineRichMenuStatus.ProviderUnavailable,
                "line-richmenu-provider-unavailable",
                ex.Message,
                ex,
                request.Metadata);
        }
        catch (TaskCanceledException ex)
        {
            // 通常代表 timeout。這裡將它歸類成 provider unavailable，讓呼叫端可以決定重試。
            return LineRichMenuResult.Failure(
                request.UserId,
                richMenuId,
                LineRichMenuStatus.ProviderUnavailable,
                "line-richmenu-provider-timeout",
                ex.Message,
                ex,
                request.Metadata);
        }
        catch (Exception ex)
        {
            // 此低階 workflow 採 result 模式，保留 UnexpectedError 讓舊呼叫端不用被例外中斷。
            // 新版 assignment workflow 已改成只轉 provider error，未知程式錯誤直接往外拋。
            return LineRichMenuResult.Failure(
                request.UserId,
                richMenuId,
                LineRichMenuStatus.UnexpectedError,
                "line-richmenu-unexpected-error",
                ex.Message,
                ex,
                request.Metadata);
        }
    }

    public async Task CreateUploadAndLinkOrThrowAsync(LineRichMenuCreateUploadAndLinkRequest request)
    {
        // OrThrow 變體適合「這個流程必須成功」的情境。
        // 先走 result 版，讓所有錯誤分類邏輯集中在同一個方法。
        var result = await CreateUploadAndLinkAsync(request).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineRichMenuException(result);
        }
    }

    public async Task<LineRichMenuResult> DeleteLinkedRichMenuAsync(LineRichMenuDeleteLinkedRequest request)
    {
        // 刪除流程同樣先做本機驗證，避免空 userId 直接送到 LINE。
        var validation = ValidateDeleteRequest(request);
        if (validation != null)
        {
            return validation;
        }

        // 先查使用者目前被綁定的 richMenuId。
        // 若查不到或回空字串，仍會做 unlink，確保使用者端狀態被清掉。
        string? richMenuId = null;

        try
        {
            richMenuId = await _processor.GetRichMenuIdOfUserAsync(request.UserId).ConfigureAwait(false);
            await _processor.UnlinkRichMenuFromUserAsync(request.UserId).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(richMenuId))
            {
                // 舊流程通常是一人一份 RichMenu，所以解除後會順手刪除遠端選單。
                // 新版 catalog/provisioning 架構是一份選單多人共用，不應用這種刪除策略。
                await _processor.DeleteRichMenuAsync(richMenuId).ConfigureAwait(false);
            }

            return LineRichMenuResult.Success(request.UserId, richMenuId, request.Metadata);
        }
        catch (LineResponseException ex)
        {
            // LINE 明確拒絕查詢、解除或刪除請求。
            return LineRichMenuResult.Failure(
                request.UserId,
                richMenuId,
                LineRichMenuStatus.ProviderRejected,
                "line-richmenu-provider-rejected",
                ex.Message,
                ex,
                request.Metadata);
        }
        catch (HttpRequestException ex)
        {
            // 網路不可用，通常可以讓上層決定是否稍後重試。
            return LineRichMenuResult.Failure(
                request.UserId,
                richMenuId,
                LineRichMenuStatus.ProviderUnavailable,
                "line-richmenu-provider-unavailable",
                ex.Message,
                ex,
                request.Metadata);
        }
        catch (TaskCanceledException ex)
        {
            // timeout 一律回 provider timeout，讓呼叫端看到一致的錯誤碼。
            return LineRichMenuResult.Failure(
                request.UserId,
                richMenuId,
                LineRichMenuStatus.ProviderUnavailable,
                "line-richmenu-provider-timeout",
                ex.Message,
                ex,
                request.Metadata);
        }
        catch (Exception ex)
        {
            // 保留舊低階 workflow 的 result 模式：未知錯誤也會被放進 result。
            // 若未來要改成與 assignment workflow 一樣不吞未知錯誤，需先盤點現有呼叫端。
            return LineRichMenuResult.Failure(
                request.UserId,
                richMenuId,
                LineRichMenuStatus.UnexpectedError,
                "line-richmenu-unexpected-error",
                ex.Message,
                ex,
                request.Metadata);
        }
    }

    public async Task DeleteLinkedRichMenuOrThrowAsync(LineRichMenuDeleteLinkedRequest request)
    {
        // OrThrow 版提供給必要流程使用：失敗時直接丟 LineRichMenuException。
        var result = await DeleteLinkedRichMenuAsync(request).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineRichMenuException(result);
        }
    }

    private static LineRichMenuResult? ValidateCreateRequest(LineRichMenuCreateUploadAndLinkRequest? request)
    {
        if (request == null)
        {
            // request 為 null 表示呼叫端連基本資料都沒有傳入。
            return LineRichMenuResult.Failure(null, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-request-required", "RichMenu request is required.", null, null);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            // userId 是 LINE 綁定使用者的必要 key，缺少時不能呼叫 LINE。
            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-user-required", "LINE user id is required.", null, request.Metadata);
        }

        if (request.RichMenu == null)
        {
            // 沒有 RichMenu 版面定義，就無法建立 LINE 選單。
            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-definition-required", "RichMenu definition is required.", null, request.Metadata);
        }

        if (request.PngImageStreamFactory == null)
        {
            // LINE RichMenu 必須有圖片，呼叫端需提供圖片 stream factory。
            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-image-factory-required", "RichMenu PNG image stream factory is required.", null, request.Metadata);
        }

        return null;
    }

    private static LineRichMenuResult? ValidateDeleteRequest(LineRichMenuDeleteLinkedRequest? request)
    {
        if (request == null)
        {
            // request 為 null 時沒有 userId 可查詢或解除。
            return LineRichMenuResult.Failure(null, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-request-required", "RichMenu request is required.", null, null);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            // 刪除/解除綁定必須知道目標 LINE user id。
            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-user-required", "LINE user id is required.", null, request.Metadata);
        }

        return null;
    }
}


