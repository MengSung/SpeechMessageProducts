using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu API 的共用 workflow。
/// 此型別只負責建立 RichMenu、上傳 PNG、連結使用者、解除連結與刪除遠端選單；
/// 選單規則、使用者分群、畫面流程與產品 policy 都留在呼叫端或更上層 orchestrator。
/// </summary>
public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow
{
    private readonly ILineRichMenuProcessor _processor;

    public LineRichMenuWorkflow(ILineRichMenuProcessor processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    public async Task<LineRichMenuResult> CreateUploadAndLinkAsync(LineRichMenuCreateUploadAndLinkRequest request)
    {
        var validation = ValidateCreateRequest(request);
        if (validation != null)
        {
            return validation;
        }

        string? richMenuId = null;

        try
        {
            richMenuId = await _processor.CreateRichMenuAsync(request.RichMenu).ConfigureAwait(false);

            using var imageStream = request.PngImageStreamFactory();
            if (imageStream == null)
            {
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
            await _processor.LinkRichMenuToUserAsync(request.UserId, richMenuId).ConfigureAwait(false);

            return LineRichMenuResult.Success(request.UserId, richMenuId, request.Metadata);
        }
        catch (LineResponseException ex)
        {
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
        var result = await CreateUploadAndLinkAsync(request).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineRichMenuException(result);
        }
    }

    public async Task<LineRichMenuResult> DeleteLinkedRichMenuAsync(LineRichMenuDeleteLinkedRequest request)
    {
        var validation = ValidateDeleteRequest(request);
        if (validation != null)
        {
            return validation;
        }

        string? richMenuId = null;

        try
        {
            richMenuId = await _processor.GetRichMenuIdOfUserAsync(request.UserId).ConfigureAwait(false);
            await _processor.UnlinkRichMenuFromUserAsync(request.UserId).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(richMenuId))
            {
                await _processor.DeleteRichMenuAsync(richMenuId).ConfigureAwait(false);
            }

            return LineRichMenuResult.Success(request.UserId, richMenuId, request.Metadata);
        }
        catch (LineResponseException ex)
        {
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
            return LineRichMenuResult.Failure(null, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-request-required", "RichMenu request is required.", null, null);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-user-required", "LINE user id is required.", null, request.Metadata);
        }

        if (request.RichMenu == null)
        {
            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-definition-required", "RichMenu definition is required.", null, request.Metadata);
        }

        if (request.PngImageStreamFactory == null)
        {
            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-image-factory-required", "RichMenu PNG image stream factory is required.", null, request.Metadata);
        }

        return null;
    }

    private static LineRichMenuResult? ValidateDeleteRequest(LineRichMenuDeleteLinkedRequest? request)
    {
        if (request == null)
        {
            return LineRichMenuResult.Failure(null, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-request-required", "RichMenu request is required.", null, null);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-user-required", "LINE user id is required.", null, request.Metadata);
        }

        return null;
    }
}


